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

    private sealed class Reader(string app) : ISessionActivityReader
    {
        public string App => app;

        public Task<SessionActivity?> ReadAsync(
            AgentSession session,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken token
        ) =>
            Task.FromResult<SessionActivity?>(
                new(
                    "2026-10-18T08:00:00Z",
                    "2026-10-18T09:00:00Z",
                    4,
                    [Path.Combine(Path.GetTempPath(), "fictional-owner", "quotes-service", "Quote.cs")],
                    6,
                    1,
                    ["dotnet test " + new string('x', 400)],
                    ["dotnet test: 12 passed"],
                    true,
                    ["FICTIONAL TRANSCRIPT EXCERPT"]
                )
            );
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
            new(AgentAppsTools.ListActivity.Id, ToolRisk.Read, "Activity."),
        ],
    };

    private (IToolbox Tools, Source Claude, Source Codex, AgentAppsToolProvider Provider) Create()
    {
        var claude = new Source("claude");
        var codex = new Source("codex");
        var provider = new AgentAppsToolProvider(_root, [claude, codex], readers: [new Reader("claude")]);
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

    // The daily brief's activity: only for enabled apps, counts and file names only, never an excerpt or a full path.
    [Fact]
    public async Task ADaysActivityIsCountsAndNamesOnly()
    {
        var (tools, claude, _, _) = Create();
        var token = TestContext.Current.CancellationToken;
        var day = DateOnly
            .FromDateTime(DateTimeOffset.Parse("2026-10-18T08:00:00Z").LocalDateTime)
            .ToString("yyyy-MM-dd");
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.ListActivity, new(day, 10), token)
        );
        Enable("claude");
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.ListActivity, new("yesterday", 10), token)
        );
        var activity = Assert.Single(await tools.InvokeAsync(AgentAppsTools.ListActivity, new(day, 10), token));
        Assert.Equal(
            (4, 6, 1, true),
            (activity.Turns, activity.Commands, activity.FailedCommands, activity.EndedWithQuestion)
        );
        Assert.Equal(["Quote.cs"], activity.Files);
        Assert.True(activity.FailedSamples.Single().Length <= 161);
        var json = JsonSerializer.Serialize(activity);
        Assert.DoesNotContain("FICTIONAL TRANSCRIPT EXCERPT", json);
        Assert.DoesNotContain("fictional-owner", json);
        Assert.Equal(1, claude.Scans);
    }
}
