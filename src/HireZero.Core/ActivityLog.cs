using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// ActorKind: owner, agent, role (a hired role such as developer; Actor is its id), a person's role (boss, trusted, visitor),
// or sender (someone with no workspace role, an email sender for example).
public sealed record ActivityRecord(
    string Id,
    string At,
    string? RequestId,
    string? RunId,
    string? WorkflowId,
    string Actor,
    string ActorKind,
    string Action,
    string Summary
);

// The durable record of who decided or did what, one row each: request created, allowed or declined, messages posted, workflow
// approvals, coding runs started and finished. Summaries are masked and clipped before they are stored; owner-only to read.
public sealed class ActivityLog
{
    public const string Owner = "owner",
        Agent = "agent",
        Role = "role",
        Sender = "sender";
    readonly string _path;

    public ActivityLog(string databasePath)
    {
        _path = databasePath;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS activity_log(id TEXT PRIMARY KEY, at TEXT NOT NULL, request_id TEXT, run_id TEXT, workflow_id TEXT,
                actor TEXT NOT NULL, actor_kind TEXT NOT NULL, action TEXT NOT NULL, summary TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS activity_log_by_request ON activity_log(request_id, at);
            """;
        command.ExecuteNonQuery();
    }

    SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }

    public static string Summary(string? text, int limit = 200)
    {
        var flat = ContentGuard.Mask(text).ReplaceLineEndings(" ").Trim();
        return flat.Length <= limit ? flat : flat[..(limit - 1)] + "…";
    }

    public ActivityRecord Record(
        string? requestId,
        string actor,
        string actorKind,
        string action,
        string summary,
        string? runId = null,
        string? workflowId = null
    )
    {
        var item = new ActivityRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.ToString("O"),
            requestId,
            runId,
            workflowId,
            Summary(actor, 120),
            actorKind,
            action,
            Summary(summary)
        );
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO activity_log VALUES($id,$at,$request,$run,$workflow,$actor,$kind,$action,$summary)";
        foreach (
            var (key, value) in new (string, string?)[]
            {
                ("$id", item.Id),
                ("$at", item.At),
                ("$request", requestId),
                ("$run", runId),
                ("$workflow", workflowId),
                ("$actor", item.Actor),
                ("$kind", actorKind),
                ("$action", action),
                ("$summary", item.Summary),
            }
        )
            command.Parameters.AddWithValue(key, (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
        return item;
    }

    // Newest first.
    public ActivityRecord[] ForRequest(string requestId, int limit = 30)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id,at,request_id,run_id,workflow_id,actor,actor_kind,action,summary FROM activity_log WHERE request_id=$request ORDER BY at DESC, rowid DESC LIMIT $limit";
        command.Parameters.AddWithValue("$request", requestId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var items = new List<ActivityRecord>();
        string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        while (reader.Read())
            items.Add(
                new(
                    reader.GetString(0),
                    reader.GetString(1),
                    Text(2),
                    Text(3),
                    Text(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8)
                )
            );
        return [.. items];
    }
}
