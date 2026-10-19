using System.Security.Cryptography;
using System.Text.Json;

namespace HireZero.Integrations.Codex;

// The Codex executable live company coding is pinned to, read from runtime/codex/pin.json (embedded at build time).
// Any other binary is refused before a process starts; upgrading is a reviewed change to the pin
// (docs/adr/0014-pinned-codex-runtime.md).
public static class CodexRuntimePin
{
    public static (string Version, string Sha256) Pinned { get; } = Read();

    private static (string, string) Read()
    {
        using var stream =
            typeof(CodexRuntimePin).Assembly.GetManifestResourceStream("HireZero.Integrations.Codex.codex-pin.json")
            ?? throw new InvalidOperationException("The Codex runtime pin is missing from this build.");
        using var pin = JsonDocument.Parse(stream);
        var version = pin.RootElement.GetProperty("version").GetString();
        var hash = pin.RootElement.GetProperty("sha256").GetString();
        if (string.IsNullOrWhiteSpace(version) || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("The Codex runtime pin is malformed.");
        return (version, hash.ToUpperInvariant());
    }

    public static string Sha256(string executable)
    {
        using var file = File.OpenRead(executable);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    // Null when the executable is the pinned binary, otherwise why it is refused.
    public static string? Refusal(string executable, string? expectedSha256 = null)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            return "Select the full path of the pinned Codex executable.";
        var expected = (expectedSha256 ?? Pinned.Sha256).ToUpperInvariant();
        return Sha256(executable) == expected
            ? null
            : "This Codex executable is not the pinned codex-cli "
                + Pinned.Version
                + " build. Upgrading Codex is a reviewed change to runtime/codex/pin.json.";
    }
}
