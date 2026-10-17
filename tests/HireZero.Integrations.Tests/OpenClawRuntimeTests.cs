using HireZero.Core;
using HireZero.Integrations.OpenClaw;

// The embedded pin matches runtime/openclaw/package.json, and only a runtime of exactly that version may start.
public sealed class OpenClawRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openclaw-pin-" + Guid.NewGuid().ToString("N"));

    public OpenClawRuntimeTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Entry(string? manifest)
    {
        if (manifest is not null)
            File.WriteAllText(Path.Combine(_root, "package.json"), manifest);
        var entry = Path.Combine(_root, "openclaw.mjs");
        File.WriteAllText(entry, "// fictional entry");
        return entry;
    }

    [Fact]
    public void PinMatchesTheRuntimeManifest()
    {
        var manifest = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../runtime/openclaw/package.json")
        );
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.Equal(
            document.RootElement.GetProperty("dependencies").GetProperty("openclaw").GetString(),
            OpenClawRuntime.PinnedVersion
        );
    }

    [Fact]
    public void AcceptsThePinnedVersion()
    {
        var entry = Entry($$"""{"name":"openclaw","version":"{{OpenClawRuntime.PinnedVersion}}"}""");
        OpenClawRuntime.EnsurePinned(entry);
    }

    [Theory]
    [InlineData("""{"name":"openclaw","version":"2000.1.1"}""")]
    [InlineData("""{"name":"not-openclaw","version":"PINNED"}""")]
    [InlineData("not json")]
    [InlineData(null)]
    public void RefusesAnyOtherRuntime(string? manifest)
    {
        var entry = Entry(manifest?.Replace("PINNED", OpenClawRuntime.PinnedVersion));
        var refusal = Assert.Throws<ModelUnavailableException>(() => OpenClawRuntime.EnsurePinned(entry));
        Assert.Contains(OpenClawRuntime.PinnedVersion, refusal.Message);
    }
}
