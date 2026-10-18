using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.AgentApps;
using HireZero.Tools.AgentApps;

// Coding-agent session listing for the company workspace: off until the owner lists apps, only those apps are scanned,
// and a session's folder is reported by name, never by full path. Sessions are fictional.
public sealed class AgentAppsToolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agent-apps-" + Guid.NewGuid().ToString("N"));

    public AgentAppsToolProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class Source(string app) : ISessionSource
    {
        public int Scans;

        public string App => app;

        public Task<AgentSession[]> ScanAsync(CancellationToken token)
        {
            Scans++;
            return Task.FromResult(
                new[]
                {
                    new AgentSession(
                        app + ":s-1",
                        app,
                        "s-1",
                        "Fictional rounding fix",
                        Path.Combine(Path.GetTempPath(), "fictional-owner", "quotes-service"),
                        "owner/quotes",
                        "fix/rounding",
                        "idle",
                        null,
                        null,
                        null,
                        "2026-10-18T08:00:00Z",
                        [],
                        LogPath: Path.Combine(Path.GetTempPath(), "fictional-owner", "log.jsonl")
                    ),
                }
            );
        }
    }

    private static readonly EmployeeManifest Manifest = new(
        "sessions-test",
        "Sessions test",
        "Fictional manifest declaring the agent-app tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Sessions tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(AgentAppsTools.Status.Id, ToolRisk.Read, "Status."),
            new(AgentAppsTools.ListSessions.Id, ToolRisk.Read, "List."),
        ],
    };

    private (IToolbox Tools, Source Claude, Source Codex, AgentAppsToolProvider Provider) Create()
    {
        var claude = new Source("claude");
        var codex = new Source("codex");
        var provider = new AgentAppsToolProvider(_root, [claude, codex]);
        var registry = new ToolRegistry(new MemoryToolAudit());
        provider.Register(registry);
        return (registry.For(Manifest), claude, codex, provider);
    }

    private void Enable(params string[] apps) =>
        File.WriteAllText(Path.Combine(_root, "agent-apps.json"), JsonSerializer.Serialize(new { apps }));

    [Fact]
    public async Task NothingIsListedUntilTheOwnerEnablesAnApp()
    {
        var (tools, claude, codex, provider) = Create();
        Assert.False(provider.Status().Active);
        Assert.Contains("agent-apps.json", provider.Status().Reason);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.ListSessions, new(null, 10), TestContext.Current.CancellationToken)
        );
        Enable("telnet");
        Assert.False(provider.Status().Active);
        Assert.Equal(0, claude.Scans + codex.Scans);
    }

    [Fact]
    public async Task OnlyEnabledAppsAreScannedAndFoldersAreNamesOnly()
    {
        var (tools, claude, codex, provider) = Create();
        Enable("claude");
        Assert.True(provider.Status().Active);
        var token = TestContext.Current.CancellationToken;
        var sessions = await tools.InvokeAsync(AgentAppsTools.ListSessions, new(null, 10), token);
        var session = Assert.Single(sessions);
        Assert.Equal(("claude", "quotes-service"), (session.App, session.Folder));
        Assert.DoesNotContain("fictional-owner", JsonSerializer.Serialize(sessions));
        Assert.Equal((1, 0), (claude.Scans, codex.Scans));
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.ListSessions, new("codex", 10), token)
        );
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.ListSessions, new(null, 100), token)
        );
        Assert.Equal(0, codex.Scans);
    }
}
