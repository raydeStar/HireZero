using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.AgentApps;
using HireZero.Tools.AgentApps;

// Read-only steering through the agentapps.* tools: only for apps whose installed command the owner names, only into
// a listed, idle, quiet session with an existing folder, one turn at a time, and never with edits or network.
// The session app and driver are fakes; nothing is started.
public sealed class AgentAppsSteeringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agent-steer-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    public AgentAppsSteeringTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class Source(string app, Func<AgentSession> session) : ISessionSource
    {
        public string App => app;

        public Task<AgentSession[]> ScanAsync(CancellationToken token) => Task.FromResult(new[] { session() });
    }

    private sealed class Driver : ISessionDriver
    {
        public List<(AgentSession Session, SteerRequest Request)> Sent = [];
        public string State = "running";

        public string App => "claude";

        public CodingRun Send(AgentSession session, SteerRequest request)
        {
            Sent.Add((session, request));
            return Find("run-" + Sent.Count)!;
        }

        public CodingRun Start(string folder, string title, SteerRequest request) =>
            throw new InvalidOperationException("Starting sessions is not part of company steering.");

        public CodingRun? Find(string runId) =>
            new(runId, State, "", "claude session default", "default", null, null, null, [], "", "");

        public void Cancel(string runId) { }
    }

    private static readonly EmployeeManifest Manifest = new(
        "steer-test",
        "Steer test",
        "Fictional manifest declaring the agent-app tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Steer tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(AgentAppsTools.CheckSteer.Id, ToolRisk.Read, "Check."),
            new(AgentAppsTools.SendReadOnly.Id, ToolRisk.External, "Send."),
        ],
    };

    private sealed class Allow : IToolAuthorityVerifier
    {
        public string Kind => "fictional";

        public bool Authorizes(ToolInvocation invocation) => true;
    }

    private string _status = "idle";
    private string _updated = "2026-10-18T08:50:00Z";
    private string? _folder;

    private (ToolRegistry Registry, Driver Driver) Create(bool steer = true)
    {
        _folder ??= Directory.CreateDirectory(Path.Combine(_root, "quotes-service")).FullName;
        var command = Path.Combine(_root, "claude.exe");
        File.WriteAllText(command, "fictional");
        File.WriteAllText(
            Path.Combine(_root, "agent-apps.json"),
            JsonSerializer.Serialize(
                steer
                    ? new { apps = new[] { "claude" }, steer = new Dictionary<string, string> { ["claude"] = command } }
                    : (object)new { apps = new[] { "claude" } }
            )
        );
        var driver = new Driver();
        var provider = new AgentAppsToolProvider(
            _root,
            [
                new Source(
                    "claude",
                    () =>
                        new(
                            "claude:s-1",
                            "claude",
                            "s-1",
                            "Fictional rounding fix",
                            _folder,
                            null,
                            null,
                            _status,
                            null,
                            null,
                            null,
                            _updated,
                            []
                        )
                ),
            ],
            (_, _) => driver
        )
        {
            Clock = () => _now,
        };
        var registry = new ToolRegistry(new MemoryToolAudit(), [new Allow()]);
        provider.Register(registry);
        return (registry, driver);
    }

    private static readonly ToolAuthority Authority = new("fictional", "allow");

    [Fact]
    public void SteeringNeedsTheOwnersNamedCommand()
    {
        var (registry, _) = Create(steer: false);
        Assert.False(registry.Has(AgentAppsTools.SendReadOnly.Id));
        Assert.False(registry.Has(AgentAppsTools.CheckSteer.Id));
    }

    [Fact]
    public async Task AReadyIdleSessionGetsOneReadOnlyMessage()
    {
        var (registry, driver) = Create();
        var tools = registry.For(Manifest);
        var token = TestContext.Current.CancellationToken;
        var message = new SteerMessage("claude:s-1", "  Explain the rounding change.  ");
        Assert.True((await tools.InvokeAsync(AgentAppsTools.CheckSteer, message, token)).Ready);
        var receipt = await tools.InvokeAsync(AgentAppsTools.SendReadOnly, message, token, Authority);
        Assert.Equal(("run-1", "running"), (receipt.RunId, receipt.State));
        var (_, request) = Assert.Single(driver.Sent);
        Assert.Equal(new SteerRequest("Explain the rounding change.", false, false), request);

        // While HireZero's turn runs, no second message is sent.
        Assert.False((await tools.InvokeAsync(AgentAppsTools.CheckSteer, message, token)).Ready);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.SendReadOnly, message, token, Authority)
        );
        Assert.Single(driver.Sent);
    }

    [Theory]
    [InlineData("working", "2026-10-18T08:50:00Z", "working")]
    [InlineData("needs-you", "2026-10-18T08:50:00Z", "approval")]
    [InlineData("idle", "2026-10-18T08:59:30Z", "last minute")]
    public async Task ABusyOrRecentlyActiveSessionIsLeftAlone(string status, string updated, string reason)
    {
        _status = status;
        _updated = updated;
        var (registry, driver) = Create();
        var tools = registry.For(Manifest);
        var token = TestContext.Current.CancellationToken;
        var message = new SteerMessage("claude:s-1", "Explain the rounding change.");
        var check = await tools.InvokeAsync(AgentAppsTools.CheckSteer, message, token);
        Assert.False(check.Ready);
        Assert.Contains(reason, check.Reason);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AgentAppsTools.SendReadOnly, message, token, Authority)
        );
        Assert.Empty(driver.Sent);
    }

    [Fact]
    public async Task UnlistedSessionsMissingFoldersAndBadMessagesAreRefused()
    {
        _folder = Path.Combine(_root, "deleted-folder");
        var (registry, driver) = Create();
        var tools = registry.For(Manifest);
        var token = TestContext.Current.CancellationToken;
        foreach (
            var message in new[]
            {
                new SteerMessage("claude:s-1", "Explain."),
                new SteerMessage("claude:other", "Explain."),
                new SteerMessage("codex:s-1", "Explain."),
                new SteerMessage("claude:s-1", new string('x', 4001)),
            }
        )
            Assert.False((await tools.InvokeAsync(AgentAppsTools.CheckSteer, message, token)).Ready);
        Assert.Empty(driver.Sent);
    }
}
