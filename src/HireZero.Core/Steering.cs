namespace HireZero.Core;

// What the owner sends into a session, and what that turn may do. Edits stay inside the session's folder.
public sealed record SteerRequest(string Message, bool AllowEdits, bool AllowNetwork);

// An app that can take a follow-up in an existing session, or start a new one that shows up in that app.
public interface ISessionDriver
{
    string App { get; }
    CodingRun Send(AgentSession session, SteerRequest request);
    CodingRun Start(string folder, string title, SteerRequest request);
    CodingRun? Find(string runId);
    void Cancel(string runId);
}

// One driver at a time: HireZero sends only when the owner clicks, only into a session that is idle or waiting,
// never while it is working or holding an approval in its own app, and never right after recent activity.
// The owner's own input in the app always wins; HireZero never locks a session.
public sealed class SessionSteering(SessionStore sessions, IEnumerable<ISessionDriver> drivers)
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, ISessionDriver> _drivers = drivers.ToDictionary(
        driver => driver.App,
        StringComparer.Ordinal
    );
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public bool Supports(string app) => _drivers.ContainsKey(app);

    public CodingRun Send(string sessionId, SteerRequest request)
    {
        var session =
            sessions.Find(sessionId) ?? throw new WorkspaceException("That session is no longer listed.", 404);
        var driver =
            _drivers.GetValueOrDefault(session.App)
            ?? throw new WorkspaceException("HireZero can't send into " + session.App + " sessions.", 400);
        Validate(request);
        if (session.Cwd is not { Length: > 0 } folder || !Directory.Exists(folder))
            throw new WorkspaceException("The session's folder no longer exists on this computer.");
        if (session.Status is "working" or "needs-you")
            throw new WorkspaceException(
                session.Status == "working"
                    ? "It's working in its app right now. Send when it's idle."
                    : "It's waiting on an approval in its own app. Answer it there first."
            );
        if (Clock() - DateTimeOffset.Parse(session.UpdatedAt) < QuietPeriod)
            throw new WorkspaceException(
                "It was active in the last minute. Wait a moment so you're not both driving it."
            );
        if (Active(session) is { } running)
            throw new WorkspaceException(
                "HireZero is already sending a turn into this session (" + running.State + ")."
            );
        var run = driver.Send(session, request with { Message = request.Message.Trim() });
        sessions.SetSteer(session.Id, run.Id, Clock());
        AgentActivity.Note(
            "Sent your message to " + (session.App == "codex" ? "Codex" : "Claude") + ": " + session.Title
        );
        return run;
    }

    // A new session started from here, in a folder you choose; it appears in its app when the scan next runs.
    public CodingRun Start(string app, string folder, string title, SteerRequest request)
    {
        var driver =
            _drivers.GetValueOrDefault(app)
            ?? throw new WorkspaceException("HireZero can't start " + app + " sessions.", 400);
        Validate(request);
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full) || Path.GetPathRoot(full) == full)
            throw new WorkspaceException("Choose an existing project folder (not a drive root).", 400);
        var clean = ContentGuard.Clean(title, 80).Text;
        var run = driver.Start(
            full,
            clean.Length > 0 ? clean : "Started from HireZero",
            request with
            {
                Message = request.Message.Trim(),
            }
        );
        AgentActivity.Note(
            "Started a " + (app == "codex" ? "Codex" : "Claude") + " session in " + Path.GetFileName(full) + "."
        );
        return run;
    }

    public CodingRun? Turn(string sessionId) =>
        sessions.Find(sessionId) is { SteerRunId: { } runId } session
        && _drivers.GetValueOrDefault(session.App) is { } driver
            ? driver.Find(runId)
            : null;

    public void Cancel(string sessionId)
    {
        var session =
            sessions.Find(sessionId) ?? throw new WorkspaceException("That session is no longer listed.", 404);
        if (Active(session) is { } running)
            _drivers[session.App].Cancel(running.Id);
    }

    public CodingRun? FindRun(string app, string runId) => _drivers.GetValueOrDefault(app)?.Find(runId);

    // When HireZero's turn ends, the session is waiting on you again (hooks may say so sooner).
    public void Reconcile()
    {
        foreach (
            var session in sessions.Recent(60).Where(item => item.SteerRunId is not null && item.Status == "working")
        )
            if (
                _drivers.GetValueOrDefault(session.App)?.Find(session.SteerRunId!) is
                { State: not ("queued" or "running") } finished
            )
                sessions.Apply(new(session.App, "stop", session.ExternalId, null), Clock());
    }

    private CodingRun? Active(AgentSession session) =>
        session.SteerRunId is { } runId && _drivers[session.App].Find(runId) is { State: "queued" or "running" } run
            ? run
            : null;

    private static void Validate(SteerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length > 4000)
            throw new WorkspaceException("Write a message of up to 4,000 characters.", 400);
        if (request.AllowNetwork && !request.AllowEdits)
            throw new WorkspaceException("Network access only applies when edits are allowed.", 400);
    }
}
