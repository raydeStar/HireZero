using System.Security.Cryptography;

namespace HireZero.Integrations.Sandbox;

// Only stable engine/runtime configuration enters the receipt. Full docker info can contain private proxy settings.
public sealed record SandboxEngineProfile(
    string Id,
    string ServerVersion,
    string KernelVersion,
    string Architecture,
    string RuntimeDigest
);

public sealed record SandboxQualification(
    string Protocol,
    string Image,
    string InstallationId,
    string Endpoint,
    string ExecutableDigest,
    SandboxEngineProfile Engine,
    DateTimeOffset QualifiedAt,
    DateTimeOffset ValidUntil,
    string[] Checks,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string[]? ExtendedChecks = null
)
{
    public const string CurrentProtocol = "sandbox-qualification-v2";
    public static string[] RequiredExtendedChecks =>
        ["extended-duration", "extended-deadline", "extended-recovery-and-release"];
    public bool ExtendedQualified =>
        ExtendedChecks is not null
        && ExtendedChecks
            .Order(StringComparer.Ordinal)
            .SequenceEqual(RequiredExtendedChecks.Order(StringComparer.Ordinal));
    public static string[] RequiredChecks =>
        [
            "unprivileged-script",
            "workload-task-limit",
            "protected-supervisor",
            "readonly-inputs-and-image",
            "durable-output-recovery",
            "script-deadline",
            "linked-output-refused",
            "resources-released",
        ];

    public bool BoundTo(string image, string installation, string endpoint) =>
        Protocol == CurrentProtocol
        && Image == image
        && InstallationId == installation
        && Endpoint == endpoint
        && Engine is not null
        && new[] { Engine.Id, Engine.ServerVersion, Engine.KernelVersion, Engine.Architecture }.All(f =>
            !string.IsNullOrWhiteSpace(f) && f.Length <= 200
        )
        && Hash(Engine.RuntimeDigest)
        && Hash(ExecutableDigest)
        && Checks is not null
        && Checks.Order(StringComparer.Ordinal).SequenceEqual(RequiredChecks.Order(StringComparer.Ordinal))
        && (ExtendedChecks is null || ExtendedQualified)
        && ValidUntil > QualifiedAt
        && ValidUntil - QualifiedAt <= TimeSpan.FromDays(30);

    public bool Matches(
        string image,
        string installation,
        string endpoint,
        string executableDigest,
        DateTimeOffset now
    ) =>
        BoundTo(image, installation, endpoint)
        && ExecutableDigest == executableDigest
        && QualifiedAt <= now
        && ValidUntil > now;

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static SandboxQualification? Read(string path)
    {
        try
        {
            using var reader = File.OpenText(path);
            var buffer = new char[16 * 1024 + 1];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            return count < buffer.Length
                ? CompanyJson.Decode<SandboxQualification>(new string(buffer, 0, count))
                : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    public static string ExecutableHash(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 128 * 1024 * 1024)
            throw new IOException("The configured executable exceeds the qualification bound.");
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }
}
