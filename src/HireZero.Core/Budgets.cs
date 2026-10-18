using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Spend controls. Every model call and coding run is recorded against a role and, when known, a task (its conversation).
// The owner sets a monthly budget per role and a cap per task, in tokens, coding runs and coding minutes (whichever the
// provider reports; Codex and OpenClaw report no price, so there is no money figure). At the soft threshold Home gets a
// notice; at the limit new agent-initiated work pauses (fix rounds, coding starts, proactive shift work) until the owner
// raises the limit or resumes. A running round or coding run is never interrupted; owner asks in chat still get answers.
// Scopes: "role:<id>" (Tokens/Runs/Minutes are monthly, Task* are the default cap for each of its tasks) and
// "task:<requestId>" (Tokens/Runs/Minutes replace that one task's cap). ResumedFor is the month (role) or "task" the owner waived.
public sealed record BudgetLimit(
    long? Tokens = null,
    int? Runs = null,
    int? Minutes = null,
    long? TaskTokens = null,
    int? TaskRuns = null,
    int? TaskMinutes = null,
    int SoftPercent = 80,
    string? ResumedFor = null
);

// Unreported counts entries whose provider gave no usage: model calls are then recorded at a conservative estimate
// (their input plus the output cap), coding runs at zero tokens. Either way they are visible, never silently zero.
public sealed record BudgetUse(long Tokens, int Runs, int Minutes, int Calls, int Unreported = 0);

public sealed record BudgetStatus(
    string Scope,
    BudgetUse Used,
    long? Tokens,
    int? Runs,
    int? Minutes,
    int SoftPercent,
    int Percent,
    string State,
    bool Resumed,
    string Summary
)
{
    public bool Blocks => State == "reached" && !Resumed;
}

public sealed class BudgetLedger
{
    public const string CodingUsage = CodingEvents.Usage;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly RequestStore? _requests;
    private readonly string? _owner;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Func<DateTimeOffset>? Clock { get; init; }

    public BudgetLedger(string path, RequestStore? requests = null, string? ownerLogin = null)
    {
        _path = path;
        _requests = requests;
        _owner = ownerLogin;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS budget_usage(id TEXT PRIMARY KEY, at TEXT NOT NULL, month TEXT NOT NULL, role TEXT NOT NULL, task TEXT,
              kind TEXT NOT NULL, model TEXT, tokens INTEGER NOT NULL, runs INTEGER NOT NULL, seconds INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS budget_usage_by_role ON budget_usage(role, month);
            CREATE INDEX IF NOT EXISTS budget_usage_by_task ON budget_usage(task);
            CREATE TABLE IF NOT EXISTS budget_limits(scope TEXT PRIMARY KEY, payload TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('budget_usage') WHERE name='unreported'";
        if ((long)columns.ExecuteScalar()! == 0)
        {
            using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE budget_usage ADD COLUMN unreported INTEGER NOT NULL DEFAULT 0";
            add.ExecuteNonQuery();
        }
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

    private DateTimeOffset Now => Clock?.Invoke() ?? DateTimeOffset.UtcNow;

    public static string MonthOf(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM");

    public static bool ValidScope(string scope) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            scope,
            "^(role:[a-z][a-z0-9-]{0,39}|task:[A-Za-z0-9_-]{1,64})$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(50)
        );

    // Idempotent by id, so a run seen twice counts once. Crossing a threshold posts its notice once.
    public bool Record(
        string id,
        string role,
        string? task,
        string kind,
        string? model,
        long tokens,
        int runs,
        double seconds,
        bool unreported = false
    )
    {
        int added;
        lock (_gate)
        {
            var at = Now;
            using var connection = Open();
            using var insert = Command(
                connection,
                "INSERT OR IGNORE INTO budget_usage(id,at,month,role,task,kind,model,tokens,runs,seconds,unreported) VALUES($id,$at,$month,$role,$task,$kind,$model,$tokens,$runs,$seconds,$unreported)",
                ("$id", id),
                ("$at", at.ToString("O")),
                ("$month", MonthOf(at)),
                ("$role", role),
                ("$task", task),
                ("$kind", kind),
                ("$model", model),
                ("$tokens", Math.Max(0, tokens)),
                ("$runs", Math.Max(0, runs)),
                ("$seconds", (long)Math.Max(0, Math.Round(seconds))),
                ("$unreported", unreported ? 1 : 0)
            );
            added = insert.ExecuteNonQuery();
        }
        if (added == 1)
            Announce(role, task);
        return added == 1;
    }

    // A finished coding run: one run, its wall-clock time, and the tokens Codex reported in its usage event (if any).
    public bool RecordCoding(CodingRun run, string role, string? task)
    {
        if (run.State is "queued" or "running")
            return false;
        var seconds =
            DateTimeOffset.TryParse(run.CreatedAt, out var started)
            && DateTimeOffset.TryParse(run.UpdatedAt, out var ended)
                ? (ended - started).TotalSeconds
                : 0;
        long? tokens = null;
        if (run.Events.LastOrDefault(item => item.Type == CodingUsage) is { } usage)
            try
            {
                using var data = JsonDocument.Parse(usage.DataJson);
                if (data.RootElement.TryGetProperty("tokens", out var value) && value.TryGetInt64(out var count))
                    tokens = count;
            }
            catch (JsonException) { }
        return Record(
            "coding:" + run.Id,
            role,
            task,
            "coding",
            run.ResolvedModel ?? run.RequestedModel,
            tokens ?? 0,
            1,
            seconds,
            unreported: tokens is null
        );
    }

    private BudgetUse Use(string where, params (string, object?)[] values)
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT COALESCE(SUM(tokens),0),COALESCE(SUM(runs),0),COALESCE(SUM(CASE kind WHEN 'coding' THEN seconds ELSE 0 END),0),COALESCE(SUM(kind='model'),0),COALESCE(SUM(unreported),0) FROM budget_usage WHERE "
                + where,
            values
        );
        using var reader = command.ExecuteReader();
        reader.Read();
        return new(
            reader.GetInt64(0),
            (int)reader.GetInt64(1),
            (int)Math.Ceiling(reader.GetInt64(2) / 60.0),
            (int)reader.GetInt64(3),
            (int)reader.GetInt64(4)
        );
    }

    public BudgetUse RoleMonth(string role) =>
        Use("role=$role AND month=$month", ("$role", role), ("$month", MonthOf(Now)));

    public BudgetUse TaskTotal(string task) => Use("task=$task", ("$task", task));

    // Tasks with usage this month, most used first.
    public string[] BusyTasks(int count)
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT task FROM budget_usage WHERE task IS NOT NULL AND month=$month GROUP BY task ORDER BY SUM(runs) DESC, SUM(seconds) DESC, SUM(tokens) DESC LIMIT $n",
            ("$month", MonthOf(Now)),
            ("$n", count)
        );
        using var reader = command.ExecuteReader();
        var tasks = new List<string>();
        while (reader.Read())
            tasks.Add(reader.GetString(0));
        return [.. tasks];
    }

    // The role a task's spending mostly belongs to, whose default cap applies when the task has none of its own.
    public string TaskRole(string task)
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT role FROM budget_usage WHERE task=$task GROUP BY role ORDER BY SUM(runs) DESC, COUNT(*) DESC LIMIT 1",
            ("$task", task)
        );
        return command.ExecuteScalar() as string ?? "workspace";
    }

    public string[] RoleScopes()
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT scope FROM budget_limits WHERE scope LIKE 'role:%' ORDER BY scope"
        );
        using var reader = command.ExecuteReader();
        var scopes = new List<string>();
        while (reader.Read())
            scopes.Add(reader.GetString(0));
        return [.. scopes];
    }

    public BudgetLimit? Limit(string scope)
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT payload FROM budget_limits WHERE scope=$scope",
            ("$scope", scope)
        );
        return command.ExecuteScalar() is string payload
            ? JsonSerializer.Deserialize<BudgetLimit>(payload, Json)
            : null;
    }

    public BudgetLimit SetLimit(string scope, BudgetLimit limit)
    {
        if (!ValidScope(scope))
            throw new WorkspaceException("Unknown budget scope.");
        if (
            limit.Tokens < 0
            || limit.Runs < 0
            || limit.Minutes < 0
            || limit.TaskTokens < 0
            || limit.TaskRuns < 0
            || limit.TaskMinutes < 0
            || limit.SoftPercent is < 1 or > 100
        )
            throw new WorkspaceException("Limits must be zero or more, and the warning between 1% and 100%.");
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(
                connection,
                "INSERT INTO budget_limits VALUES($scope,$payload) ON CONFLICT(scope) DO UPDATE SET payload=excluded.payload",
                ("$scope", scope),
                ("$payload", JsonSerializer.Serialize(limit, Json))
            );
            command.ExecuteNonQuery();
        }
        return limit;
    }

    // Waives the hard stop: a role for the rest of this month, a task for good. Warnings still show.
    public BudgetLimit Resume(string scope) =>
        SetLimit(
            scope,
            (Limit(scope) ?? new()) with
            {
                ResumedFor = scope.StartsWith("role:", StringComparison.Ordinal) ? MonthOf(Now) : "task",
            }
        );

    public BudgetStatus RoleStatus(string role)
    {
        var scope = "role:" + role;
        var limit = Limit(scope) ?? new();
        return Status(
            scope,
            RoleMonth(role),
            limit.Tokens,
            limit.Runs,
            limit.Minutes,
            limit.SoftPercent,
            limit.ResumedFor == MonthOf(Now)
        );
    }

    // A task's own cap when the owner set one, else its role's default cap.
    public BudgetStatus TaskStatus(string role, string task)
    {
        var own = Limit("task:" + task);
        var fallback = Limit("role:" + role) ?? new();
        var soft = own?.SoftPercent ?? fallback.SoftPercent;
        return own is { Tokens: not null } or { Runs: not null } or { Minutes: not null }
            ? Status("task:" + task, TaskTotal(task), own.Tokens, own.Runs, own.Minutes, soft, own.ResumedFor == "task")
            : Status(
                "task:" + task,
                TaskTotal(task),
                fallback.TaskTokens,
                fallback.TaskRuns,
                fallback.TaskMinutes,
                soft,
                own?.ResumedFor == "task"
            );
    }

    private static BudgetStatus Status(
        string scope,
        BudgetUse used,
        long? tokens,
        int? runs,
        int? minutes,
        int soft,
        bool resumed
    )
    {
        (double Share, string Text)[] measures =
        [
            .. new (long Used, long? Limit, string Unit)[]
            {
                (used.Tokens, tokens, "tokens"),
                (used.Runs, runs, "coding runs"),
                (used.Minutes, minutes, "coding minutes"),
            }
                .Where(item => item.Limit is not null)
                .Select(item =>
                    (
                        item.Limit == 0 ? 1.0 : (double)item.Used / item.Limit!.Value,
                        item.Used.ToString("N0") + " of " + item.Limit!.Value.ToString("N0") + " " + item.Unit
                    )
                ),
        ];
        var top = measures.Length == 0 ? (Share: 0.0, Text: "no limit set") : measures.MaxBy(item => item.Share);
        var percent = (int)Math.Min(999, Math.Floor(top.Share * 100));
        var state =
            measures.Length == 0 ? "unlimited"
            : top.Share >= 1 ? "reached"
            : percent >= soft ? "warning"
            : "ok";
        return new(scope, used, tokens, runs, minutes, soft, percent, state, resumed, top.Text);
    }

    // Why new work for this role (and task) must wait, or null when it may start.
    public string? Blocked(string role, string? task)
    {
        var month = RoleStatus(role);
        if (month.Blocks)
            return "the "
                + role
                + " role's "
                + Now.UtcDateTime.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)
                + " budget is used up ("
                + month.Summary
                + ")";
        if (task is not null && TaskStatus(role, task) is { Blocks: true } capped)
            return "this task's cap is used up (" + capped.Summary + ")";
        return null;
    }

    // Proactive shift work pauses while any role's monthly budget is used up.
    public string? ShiftBlocked() =>
        RoleScopes().Select(scope => Blocked(scope[5..], null)).FirstOrDefault(reason => reason is not null);

    private void Announce(string role, string? task)
    {
        if (_requests is null || _owner is null)
            return;
        var month = MonthOf(Now);
        var status = RoleStatus(role);
        if (status.State is "warning" or "reached")
        {
            var body =
                status.State == "reached"
                    ? "Budget reached: the "
                        + role
                        + " role used its "
                        + Now.UtcDateTime.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)
                        + " budget ("
                        + status.Summary
                        + "). New fix rounds, coding runs and proactive shift work are paused; anything already running finishes. Raise the limit or resume in the cockpit's Budget panel."
                    : "Budget: the "
                        + role
                        + " role has used "
                        + status.Percent
                        + "% of its "
                        + Now.UtcDateTime.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)
                        + " budget ("
                        + status.Summary
                        + "). At 100% new agent work pauses until you raise the limit or resume.";
            foreach (
                var home in new[] { _requests.Home(_owner), _requests.FindHome(WorkspaceOwner.LocalIdentity) }
                    .OfType<WorkRequest>()
                    .DistinctBy(value => value.Id)
            )
                _requests.AddOwnerAgentNotice(
                    home.Id,
                    body,
                    "home:budget:"
                        + home.Id
                        + ":"
                        + status.Scope
                        + ":"
                        + month
                        + ":"
                        + status.State
                        + ":"
                        + Fingerprint(status)
                );
        }
        if (task is null || _requests.FindRequest(task) is null)
            return;
        var capped = TaskStatus(role, task);
        if (capped.State is not ("warning" or "reached"))
            return;
        // Posted in the task's thread; Home echoes it with a link.
        _requests.AddOwnerAgentNotice(
            task,
            capped.State == "reached"
                ? "Budget reached for this task ("
                    + capped.Summary
                    + "). New rounds and coding runs here wait until you raise its cap or resume it in the cockpit's Budget panel."
                : "Budget: this task has used " + capped.Percent + "% of its cap (" + capped.Summary + ").",
            "budget:" + capped.Scope + ":" + capped.State + ":" + Fingerprint(capped)
        );
    }

    private static string Fingerprint(BudgetStatus status) => status.Tokens + "/" + status.Runs + "/" + status.Minutes;
}

// Who is spending: the role and task a model call or coding run belongs to, carried through async calls like AgentActivity.
// Integrations report usage here without knowing about roles; the host attaches the ledger once at startup.
public static class UsageMeter
{
    private static readonly AsyncLocal<(string Role, string? Task)?> Current = new();
    public static BudgetLedger? Ledger { get; set; }
    public static string Role => Current.Value?.Role ?? "workspace";
    public static string? Task => Current.Value?.Task;

    public static IDisposable For(string role, string? task)
    {
        var previous = Current.Value;
        Current.Value = (role, task);
        return new Restore(() => Current.Value = previous);
    }

    // Same task, another role (a role's own model prompts).
    public static IDisposable As(string role) => For(role, Task);

    // Tokens the provider reported, or null with a conservative estimate when it reported none.
    public static void Model(string? model, long? tokens, double seconds, long estimate = 0) =>
        Ledger?.Record(
            "model:" + Guid.NewGuid().ToString("N"),
            Role,
            Task,
            "model",
            model,
            tokens ?? Math.Max(0, estimate),
            0,
            seconds,
            unreported: tokens is null
        );

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
