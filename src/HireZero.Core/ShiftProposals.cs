using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Local observations only. This store has no execution/approval API; presentation decisions never grant consent.
public sealed class ShiftProposalStore(string path) : IOwnerTaskContributor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS shift_proposals(source TEXT NOT NULL,id TEXT PRIMARY KEY,payload TEXT NOT NULL)";
        command.ExecuteNonQuery(); return connection;
    }
    public void Replace(string source, OwnerTask[] tasks)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var remove = connection.CreateCommand(); remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM shift_proposals WHERE source=$source"; remove.Parameters.AddWithValue("$source", source); remove.ExecuteNonQuery();
        foreach (var task in tasks)
        {
            using var insert = connection.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO shift_proposals VALUES($source,$id,$payload)";
            insert.Parameters.AddWithValue("$source", source); insert.Parameters.AddWithValue("$id", task.Id);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(task, Json)); insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public OwnerTask[] Tasks()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM shift_proposals ORDER BY id LIMIT 100";
        using var reader = command.ExecuteReader(); var tasks = new List<OwnerTask>();
        while (reader.Read()) tasks.Add(JsonSerializer.Deserialize<OwnerTask>(reader.GetString(0), Json)!);
        return [.. tasks];
    }
}

// Uses metadata already recorded by permitted session sources, never reads native logs or steers another agent.
public sealed class RecentWorkShiftSensor(SessionStore sessions, ShiftProposalStore proposals, AgentStore agents, WorkspaceOwner owner) : IShiftSensor
{
    public static OwnerTask[] Discover(AgentSession[] sessions, WorkspaceOwner owner, DateTimeOffset now) => sessions
        .Where(session => session.App is "codex" or "claude" && session.Flags.Length == 0 &&
            session.Status is "needs-you" or "waiting" or "idle" && DateTimeOffset.TryParse(session.UpdatedAt, out var at) &&
            at <= now && now - at <= TimeSpan.FromDays(7))
        .DistinctBy(session => session.Id)
        .OrderBy(session => session.Status == "needs-you" ? 0 : session.Status == "waiting" ? 1 : 2)
        .ThenByDescending(session => DateTimeOffset.Parse(session.UpdatedAt)).Take(8)
        .Select(session => (session, title: ContentGuard.Clean(session.Title, 180)))
        .Where(item => item.title.Flags.Length == 0 && item.title.Text.Length > 0)
        .Select(item => new OwnerTask("recent-work:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { item.session.Id, item.session.Status, item.session.UpdatedAt, item.session.LastEventAt, Title = item.title.Text }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToLowerInvariant(),
            "Review recent work: " + item.title.Text, "Recorded " + item.session.App + " metadata", null, null, "proposal",
            item.session.UpdatedAt, owner.Login, item.session.Status == "needs-you" ? "high" : "low",
            ["Metadata only; current outcome and priority need confirmation."],
            item.session.Status == "needs-you" ? "Review the session's pending question and choose how to help." :
                "Confirm the outcome and next task; offer help with a follow-up or validation.", "needs-owner",
            Kind: item.session.Status == "needs-you" ? item.session.App + " session needs you" : "Recent " + item.session.App + " work", Detail: item.title.Text)) .ToArray();

    public Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var shift = agents.Shift();
        if (!shift.On) return Task.FromResult<(ProactiveWork[], string)>(([], "Recent-work discovery is off shift."));
        var recorded = sessions.Recent(60); var found = Discover(recorded, owner, DateTimeOffset.UtcNow);
        var saved = agents.WithCurrentShift(shift, () => { token.ThrowIfCancellationRequested(); proposals.Replace("recent-work", found); });
        return Task.FromResult<(ProactiveWork[], string)>(([], saved ?
            "Checked up to 60 recorded session metadata entries; " + found.Length + " recent-work proposals. Transcripts, task documents and external history were not collected; local requests are on Working On." :
            "Shift changed; recent-work proposals were discarded."));
    }
}
