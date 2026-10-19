using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

// How the keys that encrypt stored tokens are themselves protected at rest (docs/adr/0027-secrets-from-files.md):
// - with HIREZERO_KEY_ENCRYPTION_KEY (32 bytes, base64 or hex, usually a mounted secret file), AES-256-GCM on any OS;
//   HIREZERO_KEY_ENCRYPTION_KEY_PREVIOUS still opens keys written before a rotation;
// - otherwise DPAPI on Windows;
// - otherwise not at all, which the startup report says plainly.
public static class CompanyKeyProtection
{
    public const string Variable = "HIREZERO_KEY_ENCRYPTION_KEY";
    public const string PreviousVariable = "HIREZERO_KEY_ENCRYPTION_KEY_PREVIOUS";

    public static IServiceCollection AddCompanyKeyProtection(
        this IServiceCollection services,
        string dataRoot,
        SecretSource? secrets = null
    )
    {
        secrets ??= SecretSource.Default;
        var builder = services
            .AddDataProtection()
            .SetApplicationName("HireZero")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRoot, "keys")));
        if (secrets.Read(Variable) is { } current)
        {
            var keys = new KeyEncryptionKeys(
                Parse(current, Variable),
                secrets.Read(PreviousVariable) is { } previous ? Parse(previous, PreviousVariable) : null
            );
            services.AddSingleton(keys);
            services.Configure<KeyManagementOptions>(o => o.XmlEncryptor = new SecretKeyXmlEncryptor(keys));
        }
        else if (OperatingSystem.IsWindows())
            builder.ProtectKeysWithDpapi();
        return services;
    }

    public static string Protection(SecretSource? secrets = null) =>
        (secrets ?? SecretSource.Default).Describe(Variable).Origin != SecretOrigin.Missing ? "key-encryption key"
        : OperatingSystem.IsWindows() ? "Windows DPAPI"
        : "none (keys are stored unencrypted; supply " + Variable + ")";

    internal static byte[] Parse(string text, string name)
    {
        text = text.Trim();
        byte[]? key = null;
        try
        {
            key = text.Length == 64 ? Convert.FromHexString(text) : Convert.FromBase64String(text);
        }
        catch (FormatException) { }
        return key is { Length: 32 }
            ? key
            : throw new InvalidOperationException(name + " must be 32 bytes, as base64 or 64 hexadecimal characters.");
    }
}

public sealed class KeyEncryptionKeys(byte[] current, byte[]? previous)
{
    public byte[] Current { get; } = current;
    public byte[]? Previous { get; } = previous;

    // Names a key without revealing it, so a stored key records which key-encryption key wrapped it.
    public static string Fingerprint(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant();
}

public sealed class SecretKeyXmlEncryptor(KeyEncryptionKeys keys) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(keys.Current, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        CryptographicOperations.ZeroMemory(plaintext);
        return new EncryptedXmlInfo(
            new XElement(
                "encryptedKey",
                new XAttribute("kek", KeyEncryptionKeys.Fingerprint(keys.Current)),
                new XAttribute("nonce", Convert.ToBase64String(nonce)),
                new XAttribute("tag", Convert.ToBase64String(tag)),
                Convert.ToBase64String(ciphertext)
            ),
            typeof(SecretKeyXmlDecryptor)
        );
    }
}

// Activated by Data Protection when it reads a key this encryptor wrote.
public sealed class SecretKeyXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var keys =
            services.GetService<KeyEncryptionKeys>()
            ?? throw new InvalidOperationException(
                "A stored key was wrapped with a key-encryption key, but "
                    + CompanyKeyProtection.Variable
                    + " is not set."
            );
        var fingerprint = (string?)encryptedElement.Attribute("kek");
        var key =
            new[] { keys.Current, keys.Previous }.FirstOrDefault(k =>
                k is not null && KeyEncryptionKeys.Fingerprint(k) == fingerprint
            )
            ?? throw new InvalidOperationException(
                "A stored key was wrapped with a key-encryption key that is neither "
                    + CompanyKeyProtection.Variable
                    + " nor "
                    + CompanyKeyProtection.PreviousVariable
                    + "."
            );
        var nonce = Convert.FromBase64String((string)encryptedElement.Attribute("nonce")!);
        var tag = Convert.FromBase64String((string)encryptedElement.Attribute("tag")!);
        var ciphertext = Convert.FromBase64String(encryptedElement.Value);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, tag.Length))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        try
        {
            return XElement.Parse(System.Text.Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
