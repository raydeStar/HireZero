using HireZero.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Secrets at rest (docs/adr/0027-secrets-from-files.md): stored token keys wrapped by a mounted key-encryption key on
// any OS, opened again after a rotation, and an audit key supplied as a secret. Keys are generated per test.
public sealed class SecretProtectionTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("secret-protection-");

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    private static string NewKey() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private IDataProtector Protector(string? current, string? previous = null)
    {
        var secrets = new SecretSource(
            name =>
                name switch
                {
                    CompanyKeyProtection.Variable => current,
                    CompanyKeyProtection.PreviousVariable => previous,
                    _ => null,
                },
            []
        );
        var services = new ServiceCollection().AddCompanyKeyProtection(_root.Path, secrets).BuildServiceProvider();
        return services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fictional-tokens");
    }

    [Fact]
    public void AKeyEncryptionKeyWrapsStoredKeysAndStillOpensThemAfterRotation()
    {
        var first = NewKey();
        var sealedToken = Protector(first).Protect("fictional-refresh-token");

        var stored = File.ReadAllText(Directory.GetFiles(Path.Combine(_root.Path, "keys"), "key-*.xml").Single());
        Assert.Contains("<encryptedKey", stored);
        Assert.DoesNotContain("<masterKey", stored);

        // Rotated: the new key opens nothing written before, until the old one is supplied as the previous key.
        var second = NewKey();
        Assert.ThrowsAny<Exception>(() => Protector(second).Unprotect(sealedToken));
        Assert.Equal("fictional-refresh-token", Protector(second, first).Unprotect(sealedToken));
    }

    [Fact]
    public void AKeyEncryptionKeyMustBe32Bytes() =>
        Assert.Throws<InvalidOperationException>(() => Protector(Convert.ToBase64String(new byte[16])));

    [Fact]
    public void AnAuditKeySecretIsUsedAndMustMatchAnExistingKeyFile()
    {
        var path = Path.Combine(_root.Path, "company.sqlite");
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var store = new CompanyStore(
            new SqliteStoreDatabase(path) { AuditKey = key },
            Owner,
            CompanyWorkspace.Definitions()
        );
        store.Apply(new CompanyCommand(Guid.NewGuid().ToString("D"), "department.create") with { Name = "Ops" });
        Assert.True(store.VerifyAudit().Intact);
        Assert.False(File.Exists(Path.Combine(_root.Path, "audit.key")));

        // A second host on the same database with the same secret verifies the same chain.
        Assert.True(
            new CompanyStore(new SqliteStoreDatabase(path) { AuditKey = key }, Owner, CompanyWorkspace.Definitions())
                .VerifyAudit()
                .Intact
        );

        // A key file from before the secret, with a different key, refuses rather than silently forking the chain.
        File.WriteAllText(
            Path.Combine(_root.Path, "audit.key"),
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        );
        Assert.Throws<InvalidOperationException>(() =>
            new CompanyStore(
                new SqliteStoreDatabase(path) { AuditKey = key },
                Owner,
                CompanyWorkspace.Definitions()
            ).VerifyAudit()
        );
    }

    [Fact]
    public void TheStartupReportNamesSourcesNeverValues()
    {
        var file = Path.Combine(_root.Path, "client-secret");
        File.WriteAllText(file, "fictional-client-secret");
        var secrets = new SecretSource(
            name =>
                name switch
                {
                    "HIREZERO_OIDC_CLIENT_SECRET_FILE" => file,
                    "HIREZERO_OPENROUTER_API_KEY" => "fictional-api-key",
                    _ => null,
                },
            []
        );
        var report = string.Join('\n', CompanySecrets.Report(secrets));
        Assert.Contains("HIREZERO_OIDC_CLIENT_SECRET: file", report);
        Assert.Contains("HIREZERO_OPENROUTER_API_KEY: environment", report);
        Assert.DoesNotContain("fictional-client-secret", report);
        Assert.DoesNotContain("fictional-api-key", report);
    }
}
