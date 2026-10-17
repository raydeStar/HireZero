using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// One agent works one task at a time, like a person. Asks (replies) run on or off shift; proactive work
// that hired roles sense runs only during a shift. Roles decide what a task does; anything consequential
// still waits for the owner's explicit approval inside that role.
public sealed record AgentTask(
    string Id,
    string Kind,
    string? RequestId,
    string Title,
    string Source,
    string State,
    string? Detail,
    string? DedupeKey,
    string? Target,
    string CreatedAt,
    string UpdatedAt
);

public sealed record ShiftSchedule(bool Enabled, int[] Days, string Start, string End, string TimeZone);

public sealed record ShiftState(
    bool On,
    string? Origin,
    string? StartedAt,
    string? EndsAt,
    string? StoppedDay,
    string? LastScheduledDay,
    int ProactiveDone,
    int ProactiveBudget,
    string? LastSenseAt,
    string? SenseNote
);

// Work a role found on its own. DedupeKey makes a finding at most one task ever, even after a cancel;
// Target is the role's own JSON payload for its handler.
public sealed record ProactiveWork(
    string Kind,
    string DedupeKey,
    string Title,
    string? Target,
    string? RequestId = null
);

public sealed record AgentTaskResult(string Detail, string? RequestId = null);

// A hired role's way of finding work on shift; Note summarizes what it checked for the Cockpit.
public interface IShiftSensor
{
    Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token);
}

// Executes one task kind. canceled() reports an owner cancel during the turn; check it before saving anything visible.
public interface IAgentTaskHandler
{
    string Kind { get; }
    Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token);
}

public sealed class AgentStore
{
    public const int DefaultProactiveBudget = 6;
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AgentStore(string path)
    {
        _path = path;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS agent_tasks(id TEXT PRIMARY KEY, kind TEXT NOT NULL, request_id TEXT, title TEXT NOT NULL,
              source TEXT NOT NULL, state TEXT NOT NULL, detail TEXT, dedupe_key TEXT UNIQUE, target TEXT,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS agent_tasks_by_state ON agent_tasks(state, created_at);
            CREATE TABLE IF NOT EXISTS agent_settings(key TEXT PRIMARY KEY, payload TEXT NOT NULL);
            """;
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

    private static SqliteCommand Command(
        SqliteConnection connection,
        string sql,
        params (string Key, object? Value)[] values
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (key, value) in values)
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private const string Columns =
        "id,kind,request_id,title,source,state,detail,dedupe_key,target,created_at,updated_at";

    private static AgentTask Read(SqliteDataReader r) =>
        new(
            r.GetString(0),
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.GetString(9),
            r.GetString(10)
        );

    private AgentTask[] Query(string where, params (string, object?)[] values)
    {
        using var connection = Open();
        using var command = Command(connection, $"SELECT {Columns} FROM agent_tasks {where}", values);
        using var reader = command.ExecuteReader();
        var results = new List<AgentTask>();
        while (reader.Read())
            results.Add(Read(reader));
        return [.. results];
    }

    public AgentTask? Find(string id) => Query("WHERE id=$id", ("$id", id)).SingleOrDefault();

    // One queued reply per conversation: a newer message folds into the waiting reply, which reads the whole thread.
    // Asking again for the same message revives a failed or canceled reply.
    public AgentTask EnqueueReply(string requestId, string title, string latestMessageKey)
    {
        lock (_gate)
        {
            var key = "reply:" + requestId + ":" + latestMessageKey;
            var existing = Query("WHERE dedupe_key=$key", ("$key", key)).SingleOrDefault();
            if (existing is { State: "failed" or "canceled" } && Transition(existing.Id, existing.State, "queued"))
                return Find(existing.Id)!;
            if (existing is not null)
                return existing;
            var waiting = Query(
                    "WHERE kind='reply' AND request_id=$request AND state='queued'",
                    ("$request", requestId)
                )
                .FirstOrDefault();
            using var connection = Open();
            if (waiting is not null)
            {
                using var update = Command(
                    connection,
                    "UPDATE agent_tasks SET dedupe_key=$key,updated_at=$now WHERE id=$id",
                    ("$key", key),
                    ("$now", Now()),
                    ("$id", waiting.Id)
                );
                update.ExecuteNonQuery();
                return Find(waiting.Id)!;
            }
            return Insert(connection, "reply", requestId, title, "ask", key, null);
        }
    }

    public AgentTask? RecoverMissingReply(
        string requestId,
        string title,
        string latestMessageKey,
        string pending,
        string ownerLogin,
        long consentGeneration
    )
    {
        lock (_gate)
        {
            using var connection = Open();
            var id = Guid.NewGuid().ToString("N");
            var now = Now();
            // One conditional write checks current request, grant, access generation and latest unanswered message.
            // SQLite serializes this with archive/revocation writes even through another RequestStore instance.
            const string currentMessage =
                "m.request_id=$request AND NOT (m.author='workspace-agent' AND COALESCE(k.client_key,'') IN ($ack,$decision))";
            using var insert = Command(
                connection,
                $"""
                INSERT OR IGNORE INTO agent_tasks({Columns})
                SELECT $id,'reply',$request,$title,'ask','queued',NULL,$key,NULL,$now,$now FROM requests r
                JOIN agent_permissions p ON p.request_id=r.id LEFT JOIN request_agent_epochs e ON e.request_id=r.id
                WHERE r.id=$request AND r.status NOT IN ('archived','closed','canceled','completed','done')
                    AND e.generation>0 AND p.consent_generation=e.generation AND e.generation=$generation
                    AND NOT EXISTS(SELECT 1 FROM request_agent_upgrade_review WHERE request_id=r.id)
                    AND ('allowed:'||p.granted_at||':'||$pending)=$queue
                    AND (r.requester IN ($owner,$local) OR EXISTS(SELECT 1 FROM people WHERE login=r.requester AND revoked=0))
                    AND NOT EXISTS(SELECT 1 FROM request_triage WHERE request_id=r.id AND json_extract(payload,'$.decision')='declined')
                    AND NOT EXISTS(SELECT 1 FROM agent_tasks WHERE request_id=r.id AND kind='reply' AND state='queued')
                    AND (CASE WHEN NOT EXISTS(SELECT 1 FROM messages m LEFT JOIN message_keys k ON k.message_id=m.id WHERE {currentMessage})
                        THEN 'request' ELSE (SELECT CASE WHEN m.author='workspace-agent' THEN NULL ELSE m.id END
                            FROM messages m LEFT JOIN message_keys k ON k.message_id=m.id WHERE {currentMessage}
                            ORDER BY m.created_at DESC,m.id DESC LIMIT 1) END)=$pending
                """,
                ("$id", id),
                ("$request", requestId),
                ("$title", title.Length > 200 ? title[..200] : title),
                ("$key", "reply:" + requestId + ":" + latestMessageKey),
                ("$now", now),
                ("$pending", pending),
                ("$queue", latestMessageKey),
                ("$owner", ownerLogin),
                ("$local", WorkspaceOwner.LocalIdentity),
                ("$generation", consentGeneration),
                ("$ack", requestId + ":visitor-ack"),
                ("$decision", requestId + ":decision")
            );
            return insert.ExecuteNonQuery() == 1 ? Find(id) : null;
        }
    }

    // A finding becomes at most one task, including after the owner cancels it.
    public AgentTask? EnqueueProactive(ProactiveWork work)
    {
        lock (_gate)
        {
            if (Query("WHERE dedupe_key=$key", ("$key", work.DedupeKey)).Length > 0)
                return null;
            using var connection = Open();
            return Insert(connection, work.Kind, work.RequestId, work.Title, "proactive", work.DedupeKey, work.Target);
        }
    }

    private AgentTask Insert(
        SqliteConnection connection,
        string kind,
        string? requestId,
        string title,
        string source,
        string key,
        string? target
    )
    {
        var id = Guid.NewGuid().ToString("N");
        var now = Now();
        using var insert = Command(
            connection,
            $"INSERT INTO agent_tasks({Columns}) VALUES($id,$kind,$request,$title,$source,'queued',NULL,$key,$target,$now,$now)",
            ("$id", id),
            ("$kind", kind),
            ("$request", requestId),
            ("$title", title.Length > 200 ? title[..200] : title),
            ("$source", source),
            ("$key", key),
            ("$target", target),
            ("$now", now)
        );
        insert.ExecuteNonQuery();
        return Find(id)!;
    }

    // Asks first, then proactive work (only on shift); oldest first within each.
    public AgentTask? Next(bool onShift) =>
        Query(
                "WHERE state='queued'"
                    + (onShift ? "" : " AND source='ask'")
                    + " ORDER BY CASE source WHEN 'ask' THEN 0 ELSE 1 END, created_at, id LIMIT 1"
            )
            .SingleOrDefault();

    public AgentTask[] Active() => Query("WHERE state='active'");

    public AgentTask[] OpenTasks() =>
        Query(
            "WHERE state IN ('queued','active') ORDER BY CASE state WHEN 'active' THEN 0 ELSE 1 END, CASE source WHEN 'ask' THEN 0 ELSE 1 END, created_at, id"
        );

    public AgentTask[] ForRequest(string requestId, int count) =>
        Query("WHERE request_id=$request ORDER BY created_at DESC LIMIT $n", ("$request", requestId), ("$n", count));

    public AgentTask[] Recent(int count) =>
        Query("WHERE state IN ('done','failed') ORDER BY updated_at DESC LIMIT $n", ("$n", count));

    public AgentTask? Latest(string requestId) =>
        Query("WHERE request_id=$request ORDER BY updated_at DESC LIMIT 1", ("$request", requestId)).SingleOrDefault();

    public bool Transition(string id, string from, string to, string? detail = null, string? requestId = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(
                connection,
                "UPDATE agent_tasks SET state=$to,detail=COALESCE($detail,detail),request_id=COALESCE($request,request_id),updated_at=$now WHERE id=$id AND state=$from",
                ("$to", to),
                ("$detail", detail),
                ("$request", requestId),
                ("$now", Now()),
                ("$id", id),
                ("$from", from)
            );
            return command.ExecuteNonQuery() == 1;
        }
    }

    // Canceling a running task lets its current model turn finish but discards the result.
    public bool Cancel(string id) =>
        Transition(id, "queued", "canceled", "Canceled by the owner.")
        || Transition(id, "active", "canceled", "Canceled by the owner.");

    public int CancelProactive()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(
                connection,
                "UPDATE agent_tasks SET state='canceled',detail='Canceled by the owner.',updated_at=$now WHERE state='queued' AND source='proactive'",
                ("$now", Now())
            );
            return command.ExecuteNonQuery();
        }
    }

    // A restart interrupts at most one turn; replies and diagnoses are idempotent, so they simply run again.
    public void RecoverInterrupted()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(
                connection,
                "UPDATE agent_tasks SET state='queued',updated_at=$now WHERE state='active'",
                ("$now", Now())
            );
            command.ExecuteNonQuery();
        }
    }

    private T Setting<T>(string key, T fallback)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT payload FROM agent_settings WHERE key=$key", ("$key", key));
        return command.ExecuteScalar() is string payload
            ? JsonSerializer.Deserialize<T>(payload, Json) ?? fallback
            : fallback;
    }

    private void SaveSetting<T>(string key, T value)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(
                connection,
                "INSERT INTO agent_settings VALUES($key,$payload) ON CONFLICT(key) DO UPDATE SET payload=excluded.payload",
                ("$key", key),
                ("$payload", JsonSerializer.Serialize(value, Json))
            );
            command.ExecuteNonQuery();
        }
    }

    public ShiftSchedule Schedule() =>
        Setting("schedule", new ShiftSchedule(false, [1, 2, 3, 4, 5], "08:00", "17:00", "UTC"));

    public void SaveSchedule(ShiftSchedule schedule) => SaveSetting("schedule", schedule);

    public ShiftState Shift() =>
        Setting("shift", new ShiftState(false, null, null, null, null, null, 0, DefaultProactiveBudget, null, null));

    public void SaveShift(ShiftState shift) => SaveSetting("shift", shift);

    // Serialize observation publication with shift changes; a stopped/replaced shift cannot be revived by an awaited scan.
    public bool WithCurrentShift(ShiftState expected, Action action, Func<DateTimeOffset>? clock = null)
    {
        lock (_gate)
        {
            var current = Shift();
            if (
                !current.On
                || current.StartedAt != expected.StartedAt
                || !DateTimeOffset.TryParse(current.EndsAt, out var end)
                || (clock?.Invoke() ?? DateTimeOffset.UtcNow) >= end
            )
                return false;
            action();
            return true;
        }
    }

    public ShiftState ExpireShift(DateTimeOffset now)
    {
        lock (_gate)
        {
            var current = Shift();
            if (current.On && (!DateTimeOffset.TryParse(current.EndsAt, out var end) || now >= end))
            {
                current = current with { On = false };
                SaveShift(current);
            }
            return current;
        }
    }
}

public static class ShiftClock
{
    public static bool ValidTime(string? value) => TimeOnly.TryParseExact(value, "HH:mm", out _);

    public static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    // Returns the window end if a scheduled shift should start now. Nothing starts in the last 15 minutes,
    // a day the owner stopped is not restarted, and a missed day is skipped rather than made up.
    public static DateTimeOffset? ScheduledStart(DateTimeOffset now, ShiftSchedule schedule, ShiftState shift)
    {
        if (!schedule.Enabled || shift.On || !ValidTime(schedule.Start) || !ValidTime(schedule.End))
            return null;
        var zone = Zone(schedule.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var day = DateOnly.FromDateTime(local.DateTime).ToString("yyyy-MM-dd");
        if (!schedule.Days.Contains((int)local.DayOfWeek) || shift.StoppedDay == day || shift.LastScheduledDay == day)
            return null;
        var start = TimeOnly.ParseExact(schedule.Start, "HH:mm");
        var end = TimeOnly.ParseExact(schedule.End, "HH:mm");
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (end <= start || time < start || time >= end.AddMinutes(-15))
            return null;
        var endLocal = local.Date + end.ToTimeSpan();
        return new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal));
    }

    public static string Today(DateTimeOffset now, ShiftSchedule schedule) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone(schedule.TimeZone)).DateTime).ToString("yyyy-MM-dd");
}

public sealed class AgentWorker(
    AgentStore store,
    IEnumerable<IShiftSensor> sensors,
    IEnumerable<IAgentTaskHandler> handlers,
    ILogger<AgentWorker> log,
    HomeFeed? home = null,
    BudgetLedger? budgets = null
) : BackgroundService
{
    public static readonly TimeSpan SenseInterval = TimeSpan.FromMinutes(15);
    public Func<DateTimeOffset>? Clock { get; init; }
    private int _scanning;
    private readonly object _scanGate = new();
    private CancellationTokenSource? _scanStop;
    public bool Scanning => Volatile.Read(ref _scanning) != 0;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Dictionary<string, IAgentTaskHandler> _handlers = handlers.ToDictionary(
        handler => handler.Kind,
        StringComparer.Ordinal
    );

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    public ShiftState Start(TimeSpan length, string origin, DateTimeOffset now)
    {
        var shift = store.Shift();
        if (shift.On)
            return shift;
        shift = shift with
        {
            On = true,
            Origin = origin,
            StartedAt = now.ToString("O"),
            EndsAt = now.Add(length).ToString("O"),
            ProactiveDone = 0,
            ProactiveBudget = AgentStore.DefaultProactiveBudget,
            LastSenseAt = null,
            SenseNote = null,
        };
        store.SaveShift(shift);
        Wake();
        return shift;
    }

    public ShiftState Stop(DateTimeOffset now, bool byOwner)
    {
        var shift = store.Shift();
        shift = shift with
        {
            On = false,
            EndsAt = now.ToString("O"),
            StoppedDay = byOwner ? ShiftClock.Today(now, store.Schedule()) : shift.StoppedDay,
        };
        store.SaveShift(shift);
        lock (_scanGate)
            _scanStop?.Cancel();
        return shift;
    }

    // One scheduler step: shift clock, sensing, then at most one task. Returns true when a task ran.
    public async Task<bool> TickAsync(DateTimeOffset now, CancellationToken token)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        DateTimeOffset CurrentTime() => Clock?.Invoke() ?? now + System.Diagnostics.Stopwatch.GetElapsedTime(started);
        var shift = store.Shift();
        var schedule = store.Schedule();
        if (shift.On && DateTimeOffset.TryParse(shift.EndsAt, out var ends) && now >= ends)
            shift = Stop(now, byOwner: false);
        if (ShiftClock.ScheduledStart(now, schedule, shift) is { } end)
        {
            var day = ShiftClock.Today(now, schedule);
            shift = Start(end - now, "schedule-" + day, now) with { LastScheduledDay = day };
            store.SaveShift(shift);
        }
        if (shift.On && (!DateTimeOffset.TryParse(shift.LastSenseAt, out var sensed) || now - sensed >= SenseInterval))
        {
            using var scanStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            lock (_scanGate)
                _scanStop = scanStop;
            Interlocked.Exchange(ref _scanning, 1);
            try
            {
                shift = await SenseAsync(shift, now, CurrentTime, scanStop.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                shift = store.ExpireShift(CurrentTime());
            }
            finally
            {
                lock (_scanGate)
                    _scanStop = null;
                Interlocked.Exchange(ref _scanning, 0);
            }
        }
        shift = store.ExpireShift(CurrentTime());
        // A used-up role budget holds queued proactive work; the owner's own asks still run.
        var task = store.Next(shift.On && budgets?.ShiftBlocked() is null);
        if (task is null)
            return false;
        var claimed = false;
        if (task.Source == "proactive")
            store.WithCurrentShift(shift, () => claimed = store.Transition(task.Id, "queued", "active"), CurrentTime);
        else
            claimed = store.Transition(task.Id, "queued", "active");
        if (!claimed)
            return false;
        bool Canceled() => store.Find(task.Id)?.State == "canceled";
        using var scope = AgentActivity.Begin(task.Id);
        AgentActivity.Note("Started: " + task.Title);
        try
        {
            // A task whose role was fired stays visible as failed rather than silently disappearing.
            if (!_handlers.TryGetValue(task.Kind, out var handler))
            {
                store.Transition(task.Id, "active", "failed", "No hired role handles this kind of work.");
                return true;
            }
            AgentTaskResult result;
            using (UsageMeter.For("workspace", task.RequestId))
                result = await handler.RunAsync(task, Canceled, token);
            AgentActivity.Note("Done: " + result.Detail);
            if (
                store.Transition(task.Id, "active", "done", result.Detail, result.RequestId)
                && task.Source == "proactive"
            )
            {
                var current = store.Shift();
                store.SaveShift(current with { ProactiveDone = current.ProactiveDone + 1 });
            }
            if (store.Find(task.Id) is { State: "done" } completed)
                home?.TaskFinished(completed);
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            store.Transition(
                task.Id,
                "active",
                "failed",
                error is WorkspaceException or ModelUnavailableException
                    ? error.Message
                    : "The task stopped unexpectedly. Try again or cancel it."
            );
            AgentActivity.Note(
                "Stopped: "
                    + (
                        error is WorkspaceException or ModelUnavailableException
                            ? error.Message
                            : "an unexpected error."
                    )
            );
            if (store.Find(task.Id) is { State: "failed" } failed)
                home?.TaskFinished(failed);
            log.LogWarning(error, "Agent task {Kind} failed.", task.Kind);
        }
        return true;
    }

    private async Task<ShiftState> SenseAsync(
        ShiftState shift,
        DateTimeOffset now,
        Func<DateTimeOffset> currentTime,
        CancellationToken token
    )
    {
        shift = shift with { LastSenseAt = now.ToString("O") };
        if (budgets?.ShiftBlocked() is { } spent)
        {
            var note =
                "Budget reached: "
                + spent
                + ". No proactive work is queued until you raise the limit or resume in the Budget panel.";
            store.WithCurrentShift(
                shift,
                () => store.SaveShift(store.Shift() with { LastSenseAt = shift.LastSenseAt, SenseNote = note }),
                currentTime
            );
            return store.ExpireShift(currentTime());
        }
        var notes = new List<string>();
        var queued = 0;
        var room =
            shift.ProactiveBudget - shift.ProactiveDone - store.OpenTasks().Count(task => task.Source == "proactive");
        foreach (var sensor in sensors)
        {
            if (!store.WithCurrentShift(shift, () => { }, currentTime))
                return store.ExpireShift(currentTime());
            try
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
                var remaining = DateTimeOffset.Parse(shift.EndsAt!) - currentTime();
                bounded.CancelAfter(
                    remaining < TimeSpan.FromMinutes(2)
                        ? remaining > TimeSpan.Zero
                            ? remaining
                            : TimeSpan.Zero
                        : TimeSpan.FromMinutes(2)
                );
                var (work, note) = await sensor.SenseAsync(bounded.Token);
                if (
                    !store.WithCurrentShift(
                        shift,
                        () =>
                        {
                            foreach (var item in work)
                                if (queued < room && store.EnqueueProactive(item) is not null)
                                    queued++;
                        },
                        currentTime
                    )
                )
                    return store.ExpireShift(currentTime());
                notes.Add(note);
            }
            catch (Exception error)
                when (error is WorkspaceException or HttpRequestException or JsonException or InvalidOperationException)
            {
                notes.Add(
                    "A check failed: " + (error is WorkspaceException known ? known.Message : "the request failed.")
                );
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                notes.Add("A source check reached its time limit; it will retry on the next bounded check.");
            }
        }
        shift = shift with
        {
            SenseNote =
                notes.Count == 0
                    ? "No hired role has anything to check."
                    : string.Join("\n", notes) + (queued > 0 ? "\nQueued " + queued + " for this shift." : ""),
        };
        store.WithCurrentShift(
            shift,
            () => store.SaveShift(store.Shift() with { LastSenseAt = shift.LastSenseAt, SenseNote = shift.SenseNote }),
            currentTime
        );
        return store.ExpireShift(currentTime());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        store.RecoverInterrupted();
        while (!stoppingToken.IsCancellationRequested)
        {
            var ran = false;
            try
            {
                ran = await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                log.LogWarning(error, "Agent scheduler step failed.");
            }
            if (!ran)
                await _wake.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
