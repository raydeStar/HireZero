using System.Collections.Concurrent;
using System.Text.Json;
using HireZero.Integrations.Codex;
using HireZero.Tools.AgentApps;

namespace HireZero.Integrations.AgentApps;

// The agentapps.* tools for the company workspace (docs/adr/0009-legacy-features-are-optional-integrations.md): off
// until the owner lists apps in <data root>/agent-apps.json, then an on-demand scan of those apps' sessions. No
// watcher, hooks or session store; nothing is sent to a session.
//
// Read-only steering (docs/history/PHASE2B5E2_REPORT.md) is available for an app only when agent-apps.json also names
// the full path of that installed app under "steer". Each message is checked again when it is sent: the session must
// still be listed, its folder must exist, it must be idle and quiet for a minute, and no HireZero turn may be running
// in it. The turn may not edit files or use the network.
public sealed class AgentAppsToolProvider(
    string dataRoot,
    IEnumerable<ISessionSource> sources,
    Func<string, string, ISessionDriver>? drivers = null
) : IToolProvider, IIntegrationStatusSource
{
    public static readonly string[] Known = ["claude", "codex"];
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(60);
    private readonly ISessionSource[] _sources = [.. sources];
    private readonly ConcurrentDictionary<string, ISessionDriver> _drivers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (ISessionDriver Driver, string RunId)> _active = new(
        StringComparer.Ordinal
    );
    private readonly object _gate = new();
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    private sealed record Settings(string[]? Apps, Dictionary<string, string>? Steer);

    private Settings Read()
    {
        var file = Path.Combine(dataRoot, "agent-apps.json");
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), CompanyJson.Options) ?? new(null, null)
                : new(null, null);
        }
        catch (JsonException)
        {
            return new(null, null);
        }
    }

    public string[] Enabled() => [.. (Read().Apps ?? []).Where(Known.Contains).Distinct()];

    // Enabled apps whose installed command the owner named with a full path.
    public string[] Steerable()
    {
        var settings = Read();
        return
        [
            .. Enabled()
                .Where(app =>
                    settings.Steer?.GetValueOrDefault(app) is { } command
                    && Path.IsPathFullyQualified(command)
                    && File.Exists(command)
                ),
        ];
    }

    public IntegrationStatus Status()
    {
        var apps = Enabled();
        return new(
            "agent-apps",
            "Coding-agent sessions",
            apps.Length > 0,
            apps.Length > 0
                ? "Listing "
                    + string.Join(" and ", apps)
                    + " sessions on request."
                    + (
                        Steerable() is { Length: > 0 } steer
                            ? " Read-only steering: " + string.Join(" and ", steer) + "."
                            : " Name an app's full command path under \"steer\" to allow read-only steering."
                    )
                : "List the apps to read (claude, codex) in agent-apps.json in the data root."
        );
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(AgentAppsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AgentAppsTools.ListSessions, ListAsync);
        if (Steerable().Length > 0)
        {
            registry.Register(
                AgentAppsTools.CheckSteer,
                async (message, token) => (await PrepareAsync(message, token)).Check
            );
            registry.Register(AgentAppsTools.SendReadOnly, SendAsync);
        }
    }

    private async Task<(SteerCheck Check, AgentSession? Session, string Message)> PrepareAsync(
        SteerMessage request,
        CancellationToken token
    )
    {
        (SteerCheck, AgentSession?, string) No(string reason) => (new(false, reason), null, "");
        var message = (request.Message ?? "").Trim();
        if (message.Length is 0 or > 4000)
            return No("Write a message of up to 4,000 characters.");
        var app = (request.SessionId ?? "").Split(':')[0];
        if (!Steerable().Contains(app))
            return No("Read-only steering is not enabled for this app.");
        var source = _sources.FirstOrDefault(s => s.App == app);
        if (source is null)
            return No("This app's sessions cannot be read.");
        var session = (await source.ScanAsync(token)).FirstOrDefault(s => s.Id == request.SessionId);
        if (session is null)
            return No("That session is no longer listed.");
        if (session.Cwd is not { Length: > 0 } folder || !Directory.Exists(folder))
            return No("The session's folder no longer exists on this computer.");
        if (session.Status is "working" or "needs-you")
            return No(
                session.Status == "working"
                    ? "It is working in its app right now."
                    : "It is waiting on an approval in its own app."
            );
        if (DateTimeOffset.TryParse(session.UpdatedAt, out var updated) && Clock() - updated < QuietPeriod)
            return No("It was active in the last minute.");
        if (
            _active.TryGetValue(session.Id, out var running)
            && running.Driver.Find(running.RunId) is { State: "queued" or "running" }
        )
            return No("A HireZero turn is already running in this session.");
        return (new(true, "Ready for one read-only message."), session, message);
    }

    private async Task<SteerReceipt> SendAsync(SteerMessage request, CancellationToken token)
    {
        var (check, session, message) = await PrepareAsync(request, token);
        if (!check.Ready || session is null)
            throw new ToolFailureException("refused", check.Reason + " Nothing was sent.", 409);
        lock (_gate)
        {
            if (
                _active.TryGetValue(session.Id, out var running)
                && running.Driver.Find(running.RunId) is { State: "queued" or "running" }
            )
                throw new ToolFailureException("refused", "A HireZero turn is already running. Nothing was sent.", 409);
            var driver = Driver(session.App);
            // Read-only: no edits, no network, whatever the request.
            var run = driver.Send(session, new SteerRequest(message, AllowEdits: false, AllowNetwork: false));
            _active[session.Id] = (driver, run.Id);
            return new(run.Id, run.State);
        }
    }

    private ISessionDriver Driver(string app) =>
        _drivers.GetOrAdd(
            app,
            name =>
            {
                var command = Read().Steer![name];
                if (drivers is not null)
                    return drivers(name, command);
                var store = new CodingRunStore(Path.Combine(dataRoot, "steer-runs.sqlite"));
                return name == "claude"
                    ? new ClaudeSessionDriver(store, command)
                    : new CodexSessionDriver(new CodexRunner(store, command));
            }
        );

    private async Task<SessionSummary[]> ListAsync(SessionListRequest request, CancellationToken token)
    {
        if (request.Max is < 1 or > 25)
            throw new ToolFailureException("refused", "List between 1 and 25 sessions.", 422);
        var enabled = Enabled();
        if (request.App is { } app && !enabled.Contains(app) || enabled.Length == 0)
            throw new ToolFailureException("refused", "That app is not enabled: " + Status().Reason, 409);
        var found = new List<AgentSession>();
        foreach (
            var source in _sources.Where(s => enabled.Contains(s.App) && (request.App is null || s.App == request.App))
        )
        {
            try
            {
                found.AddRange(await source.ScanAsync(token));
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                throw new ToolFailureException("unavailable", source.App + " sessions could not be listed.");
            }
        }
        return
        [
            .. found
                .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)
                .Take(request.Max)
                .Select(s => new SessionSummary(
                    s.Id,
                    s.App,
                    s.Title,
                    s.Cwd is null ? null : Path.GetFileName(s.Cwd.TrimEnd(Path.DirectorySeparatorChar, '/')),
                    s.Repository,
                    s.Branch,
                    s.Status,
                    s.UpdatedAt,
                    s.Flags
                )),
        ];
    }

    // The company workspace's provider: the installed apps' own session sources, scanned only on request.
    public static AgentAppsToolProvider Company(string dataRoot) =>
        new(dataRoot, [new ClaudeSessionSource(), new LazySource("codex", () => new CodexSessionSource())]);

    // Defers resolving the Codex command until a listing actually needs it.
    private sealed class LazySource(string app, Func<ISessionSource> create) : ISessionSource
    {
        private readonly Lazy<ISessionSource> _source = new(create);

        public string App => app;

        public Task<AgentSession[]> ScanAsync(CancellationToken token) => _source.Value.ScanAsync(token);
    }
}
