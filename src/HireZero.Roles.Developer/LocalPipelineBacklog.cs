using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Roles.Developer;

// Only saved workflow metadata is read. This adapter cannot connect to GitHub, agents or a model.
public sealed class LocalPipelineBacklog(string database) : IBacklogSnapshotSource
{
    public string MembershipSql => "SELECT id AS item_id FROM github_pr_workflows";
    public BacklogSourceInfo Info => new("local-pipeline", "Saved PR workflow observations", 10, true, true, "saved-pr-workflows", null);
    public Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.id,w.payload,s.updated_at FROM github_pr_workflows w LEFT JOIN github_workflow_stamps s ON s.workflow_id=w.id
            WHERE w.id>$cursor AND (s.updated_at IS NULL OR julianday(s.updated_at)<=julianday($through)) ORDER BY w.id LIMIT $limit
            """;
        command.Parameters.AddWithValue("$cursor", read.Cursor ?? ""); command.Parameters.AddWithValue("$through", read.Through.ToString("O")); command.Parameters.AddWithValue("$limit", read.Limit + 1);
        using var reader = command.ExecuteReader(); var items = new List<BacklogEvent>(); var gaps = new List<string>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var workflow = JsonSerializer.Deserialize<PullRequestWorkflow>(reader.GetString(1))!;
            var updated = reader.IsDBNull(2) ? DateTimeOffset.MinValue : DateTimeOffset.Parse(reader.GetString(2));
            if (reader.IsDBNull(2)) gaps.Add("Some saved workflow rows lack an observation timestamp; historical completeness is unknown.");
            var state = PullRequestWorkflowStates.BacklogStatus(workflow.State);
            var revision = BacklogPolicy.Hash(JsonSerializer.Serialize(new { workflow.HeadSha, workflow.EvidenceSnapshot, workflow.State, workflow.ConsentGeneration, updated }));
            items.Add(new(workflow.Id, revision, "pipeline", ContentGuard.Clean(workflow.Repository + " #" + workflow.Number, 240).Text,
                state, updated, ["https://github.com/" + workflow.Repository + "/pull/" + workflow.Number], workflow.RequestId));
        }
        var more = items.Count > read.Limit; var page = items.Take(read.Limit).ToArray();
        return Task.FromResult(new BacklogPage(page, more ? page[^1].ItemId : null, !more, [.. gaps.Distinct().Take(8)]));
    }
}
