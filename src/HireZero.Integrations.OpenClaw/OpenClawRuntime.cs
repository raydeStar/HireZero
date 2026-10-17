using System.Text.Json;

namespace HireZero.Integrations.OpenClaw;

// The OpenClaw version this build is qualified against, read from runtime/openclaw/package.json (embedded at build
// time). A runtime of any other version is refused before a process starts; see docs/adr/0004-pinned-agent-runtimes.md.
public static class OpenClawRuntime
{
    public static string PinnedVersion { get; } = ReadPin();

    private static string ReadPin()
    {
        using var stream = typeof(OpenClawRuntime).Assembly.GetManifestResourceStream("HireZero.Integrations.OpenClaw.runtime-package.json")
            ?? throw new InvalidOperationException("The OpenClaw runtime pin is missing from this build.");
        using var manifest = JsonDocument.Parse(stream);
        return manifest.RootElement.GetProperty("dependencies").GetProperty("openclaw").GetString()
            ?? throw new InvalidOperationException("The OpenClaw runtime pin is empty.");
    }

    // Reads the version of the package that owns the entry script (openclaw.mjs sits beside its package.json).
    public static string? InstalledVersion(string entry)
    {
        var manifest = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(entry)) ?? "", "package.json");
        if (!File.Exists(manifest)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            return document.RootElement.TryGetProperty("name", out var name) && name.GetString() == "openclaw" &&
                document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static void EnsurePinned(string entry)
    {
        var installed = InstalledVersion(entry);
        if (installed != PinnedVersion)
            throw new ModelUnavailableException("The installed OpenClaw runtime is " + (installed ?? "unidentified") + ", but this build is pinned to " +
                PinnedVersion + ". Run tools/Install-OpenClawRuntime.ps1 for this data root.");
    }
}
