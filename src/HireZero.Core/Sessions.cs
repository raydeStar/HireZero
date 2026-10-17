using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// A coding session in another app (Codex, Claude Code). Only metadata and a cleaned title are kept here,
// never transcript content. Status comes from live hook events when installed, otherwise from activity time.
public sealed record AgentSession(
    string Id,
    string App,
    string ExternalId,
    string Title,
    string? Cwd,
    string? Repository,
    string? Branch,
    string Status,
    string? Origin,
    string? PrUrl,
    int? PrNumber,
    string UpdatedAt,
    string[] Flags,
    string? LastEvent = null,
    string? LastEventAt = null,
    string? SteerRunId = null,
    string? LogPath = null
);

public sealed record SessionEvent(string App, string Event, string SessionId, string? Cwd);

// An app whose sessions can be listed. Implementations read metadata only and pass titles through ContentGuard.
public interface ISessionSource
{
    string App { get; }
    Task<AgentSession[]> ScanAsync(CancellationToken token);
}

public sealed class SessionStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SessionStore(string path)
    {
        _path = path;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS agent_sessions(id TEXT PRIMARY KEY, app TEXT NOT NULL, updated_at TEXT NOT NULL, payload TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }

    public AgentSession? Find(string id) => Query("WHERE id=$id", ("$id", id)).SingleOrDefault();

    public AgentSession[] Recent(int count) => Query("ORDER BY updated_at DESC LIMIT $n", ("$n", count));

    private AgentSession[] Query(string where, params (string Key, object Value)[] values)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM agent_sessions " + where;
        foreach (var (key, value) in values)
            command.Parameters.AddWithValue(key, value);
        using var reader = command.ExecuteReader();
        var results = new List<AgentSession>();
        while (reader.Read())
            results.Add(JsonSerializer.Deserialize<AgentSession>(reader.GetString(0), Json)!);
        return [.. results];
    }

    private void Save(AgentSession session)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO agent_sessions VALUES($id,$app,$updated,$payload) ON CONFLICT(id) DO UPDATE SET updated_at=excluded.updated_at,payload=excluded.payload";
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$app", session.App);
        command.Parameters.AddWithValue("$updated", session.UpdatedAt);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, Json));
        command.ExecuteNonQuery();
    }

    // A scan refreshes metadata but keeps a live status a hook reported more recently than the scan saw.
    public void Merge(AgentSession scanned)
    {
        lock (_gate)
        {
            var existing = Find(scanned.Id);
            var liveIsNewer = existing?.LastEventAt is { } at && string.CompareOrdinal(at, scanned.UpdatedAt) >= 0;
            Save(
                scanned with
                {
                    Status = liveIsNewer ? existing!.Status : scanned.Status,
                    LastEvent = existing?.LastEvent,
                    LastEventAt = existing?.LastEventAt,
                    SteerRunId = existing?.SteerRunId,
                    UpdatedAt =
                        string.CompareOrdinal(existing?.UpdatedAt ?? "", scanned.UpdatedAt) > 0
                            ? existing!.UpdatedAt
                            : scanned.UpdatedAt,
                }
            );
        }
    }

    // HireZero sent a turn: the session is working on it until the turn ends.
    public void SetSteer(string id, string runId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var session = Find(id) ?? throw new WorkspaceException("That session is no longer listed.", 404);
            var at = now.ToString("O");
            Save(
                session with
                {
                    SteerRunId = runId,
                    Status = "working",
                    LastEvent = "hirezero-turn",
                    LastEventAt = at,
                    UpdatedAt = at,
                }
            );
        }
    }

    // Live events from installed hooks. A session we haven't scanned yet is recorded with what the event says.
    public AgentSession Apply(SessionEvent update, DateTimeOffset now)
    {
        var status =
            update.Event switch
            {
                "session-start" => "open",
                "prompt" => "working",
                "tool" => "working",
                "stop" => "waiting",
                "notification" => "needs-you",
                "session-end" => "ended",
                _ => null,
            } ?? throw new WorkspaceException("Unknown session event.", 400);
        lock (_gate)
        {
            var id = update.App + ":" + update.SessionId;
            var at = now.ToString("O");
            var existing =
                Find(id)
                ?? new AgentSession(
                    id,
                    update.App,
                    update.SessionId,
                    "Untitled " + (update.App == "codex" ? "Codex" : "Claude") + " session",
                    update.Cwd is null ? null : ContentGuard.Clean(update.Cwd, 300).Text,
                    update.Cwd is null ? null : Path.GetFileName(update.Cwd.TrimEnd('\\', '/')),
                    null,
                    status,
                    null,
                    null,
                    null,
                    at,
                    []
                );
            var session = existing with { Status = status, LastEvent = update.Event, LastEventAt = at, UpdatedAt = at };
            Save(session);
            return session;
        }
    }
}

// A random secret in <data root>\hook.token that the session hooks send with each event.
public sealed class HookToken
{
    private readonly string _value;
    public string Path { get; }

    public HookToken(string dataRoot)
    {
        Path = System.IO.Path.Combine(dataRoot, "hook.token");
        if (!File.Exists(Path))
        {
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(
                Path,
                Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
            );
        }
        _value = File.ReadAllText(Path).Trim();
    }

    public bool Matches(string? candidate) =>
        candidate is { Length: > 0 }
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(candidate),
            System.Text.Encoding.UTF8.GetBytes(_value)
        );
}

// Rescans every source each minute; one failing app never stops the others.
public sealed class SessionWatcher(
    IEnumerable<ISessionSource> sources,
    SessionStore store,
    ILogger<SessionWatcher> log,
    SessionSteering? steering = null
) : BackgroundService
{
    public Dictionary<string, string> Health { get; } = new();

    public async Task ScanAsync(CancellationToken token)
    {
        foreach (var source in sources)
        {
            try
            {
                var found = await source.ScanAsync(token);
                foreach (var session in found)
                    store.Merge(session);
                lock (Health)
                    Health[source.App] =
                        "Saw "
                        + found.Length
                        + " session(s)"
                        + (
                            source is FallbackSessionSource fallback
                                ? fallback.Using == "preferred"
                                    ? " through OpenClaw"
                                    : " directly (OpenClaw " + fallback.Using[10..] + ")"
                                : ""
                        )
                        + " at "
                        + DateTimeOffset.UtcNow.ToString("O");
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                lock (Health)
                    Health[source.App] =
                        "Couldn't read "
                        + source.App
                        + " sessions: "
                        + (error is WorkspaceException known ? known.Message : error.GetType().Name);
                log.LogWarning(error, "Session scan failed for {App}.", source.App);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanAsync(stoppingToken);
            steering?.Reconcile();
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}

// Uses the preferred source (for example OpenClaw's catalog) and falls back to another for the same app when it fails.
public sealed class FallbackSessionSource(ISessionSource preferred, ISessionSource fallback) : ISessionSource
{
    public string App => preferred.App;
    public string Using { get; private set; } = "";

    public async Task<AgentSession[]> ScanAsync(CancellationToken token)
    {
        try
        {
            var found = await preferred.ScanAsync(token);
            Using = "preferred";
            return found;
        }
        catch (Exception error)
            when (error is WorkspaceException or ModelUnavailableException or IOException or InvalidOperationException)
        {
            Using = "fallback: " + error.Message;
            return await fallback.ScanAsync(token);
        }
    }
}
