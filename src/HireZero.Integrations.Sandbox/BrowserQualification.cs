namespace HireZero.Integrations.Sandbox;

// Browser qualification is separate from script qualification, even when both use the same engine.
public sealed record BrowserQualification(
    string Protocol,
    string Image,
    string InstallationId,
    string Endpoint,
    string ExecutableDigest,
    SandboxEngineProfile Engine,
    DateTimeOffset QualifiedAt,
    DateTimeOffset ValidUntil,
    string[] Checks
)
{
    public const string CurrentProtocol = "browser-qualification-v1";
    public static string[] RequiredChecks =>
        [
            "runsc-profile",
            "dynamic-itinerary",
            "broker-scope",
            "bounded-captures",
            "authority-cancellation",
            "browser-deadline",
            "durable-output-recovery",
            "resources-released",
        ];

    public bool BoundTo(string image, string installation, string endpoint) =>
        Protocol == CurrentProtocol
        && Image == image
        && InstallationId == installation
        && Endpoint == endpoint
        && image is { Length: 71 }
        && image.StartsWith("sha256:", StringComparison.Ordinal)
        && image[7..].All(Uri.IsHexDigit)
        && Guid.TryParseExact(installation, "N", out _)
        && endpoint is "unix:///var/run/docker.sock" or "npipe:////./pipe/dockerDesktopLinuxEngine"
        && Engine is not null
        && new[] { Engine.Id, Engine.ServerVersion, Engine.KernelVersion, Engine.Architecture }.All(f =>
            !string.IsNullOrWhiteSpace(f) && f.Length <= 200
        )
        && Hash(Engine.RuntimeDigest)
        && Hash(ExecutableDigest)
        && Checks is not null
        && Checks.Order(StringComparer.Ordinal).SequenceEqual(RequiredChecks.Order(StringComparer.Ordinal))
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

    public static BrowserQualification? Read(string path)
    {
        try
        {
            using var reader = File.OpenText(path);
            var buffer = new char[16 * 1024 + 1];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            return count < buffer.Length
                ? CompanyJson.Decode<BrowserQualification>(new string(buffer, 0, count))
                : null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
    }
}
