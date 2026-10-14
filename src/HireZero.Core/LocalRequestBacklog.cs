using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Re-scan retained request metadata on each completed cycle. This does not claim an event history.
public sealed class LocalRequestBacklog(string database) : IBacklogSnapshotSource
{
    public string MembershipSql => "SELECT id AS item_id FROM requests";
    public BacklogSourceInfo Info => new("local-requests", "Saved request metadata", 20, true, true, "workspace-requests", null);
    public Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id,r.title,r.status,r.created_at,
                COALESCE((SELECT MAX(created_at) FROM messages WHERE request_id=r.id),r.created_at),
                (SELECT id FROM messages WHERE request_id=r.id ORDER BY created_at DESC,id DESC LIMIT 1),
                COALESCE(json_extract(t.payload,'$.decision'),'pending'),COALESCE(e.generation,0)
            FROM requests r LEFT JOIN request_triage t ON t.request_id=r.id LEFT JOIN request_agent_epochs e ON e.request_id=r.id
            WHERE r.id>$cursor AND julianday(COALESCE((SELECT MAX(created_at) FROM messages WHERE request_id=r.id),r.created_at))<=julianday($through) ORDER BY r.id LIMIT $limit
            """;
        command.Parameters.AddWithValue("$cursor", read.Cursor ?? ""); command.Parameters.AddWithValue("$through", read.Through.ToString("O")); command.Parameters.AddWithValue("$limit", read.Limit + 1);
        using var reader = command.ExecuteReader(); var items = new List<BacklogEvent>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var id = reader.GetString(0); var state = reader.GetString(2); var decision = reader.GetString(6);
            var status = state == "queued" && decision == "pending" ? "needs-owner" : state;
            var updated = DateTimeOffset.Parse(reader.GetString(4));
            var revision = BacklogPolicy.Hash(JsonSerializer.Serialize(new { id, title = reader.GetString(1), state, decision, message = reader.IsDBNull(5) ? null : reader.GetString(5), generation = reader.GetInt64(7) }));
            items.Add(new(id, revision, "request", ContentGuard.Clean(reader.GetString(1), 240).Text, status, updated, [], id));
        }
        var more = items.Count > read.Limit; var page = items.Take(read.Limit).ToArray();
        return Task.FromResult(new BacklogPage(page, more ? page[^1].ItemId : null, !more, []));
    }
}
