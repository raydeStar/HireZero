using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Roles.Developer;

public sealed class WikiDraftStore
{
    private readonly string _path;
    private readonly object _gate = new();
    public WikiDraftStore(string path)
    {
        _path = path;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS wiki_projects(project TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS wiki_drafts(id TEXT PRIMARY KEY, request_id TEXT NOT NULL REFERENCES requests(id),
                project TEXT NOT NULL, client_key TEXT NOT NULL UNIQUE, payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS wiki_drafts_by_project ON wiki_drafts(project);
            CREATE TABLE IF NOT EXISTS wiki_scope_reviews(draft_id TEXT NOT NULL, request_id TEXT NOT NULL,
                PRIMARY KEY(draft_id,request_id));
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, ForeignKeys = true }.ToString());
        connection.Open();
        return connection;
    }
    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return command;
    }
    public WikiProject[] Projects()
    {
        using var connection = Open(); using var command = Command(connection, "SELECT payload FROM wiki_projects ORDER BY project");
        using var reader = command.ExecuteReader(); var result = new List<WikiProject>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<WikiProject>(reader.GetString(0))!);
        return [.. result];
    }
    public WikiProject? Project(string project) => Projects().FirstOrDefault(item => item.Project == project);
    public bool SaveProject(WikiProject project)
    {
        lock (_gate)
        {
            var current = Project(project.Project);
            if (current is not null && current != project && Drafts().Any(item => item.Project == project.Project)) return false;
            if (Projects().Any(item => item.Project != project.Project && item.WikiProjectId == project.WikiProjectId)) return false;
            using var connection = Open(); using var command = Command(connection,
                "INSERT INTO wiki_projects VALUES($project,$payload) ON CONFLICT(project) DO UPDATE SET payload=excluded.payload",
                ("$project", project.Project), ("$payload", JsonSerializer.Serialize(project)));
            command.ExecuteNonQuery(); return true;
        }
    }
    public WikiDraft[] Drafts()
    {
        using var connection = Open(); using var command = Command(connection, "SELECT payload FROM wiki_drafts");
        using var reader = command.ExecuteReader(); var result = new List<WikiDraft>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<WikiDraft>(reader.GetString(0))!);
        return [.. result.OrderByDescending(item => item.UpdatedAt)];
    }
    public WikiDraft? Find(string id) => Drafts().FirstOrDefault(item => item.Id == id);
    public WikiDraft? FindCreate(string key)
    {
        using var connection = Open(); using var command = Command(connection,
            "SELECT payload FROM wiki_drafts WHERE client_key=$key", ("$key", key));
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<WikiDraft>(payload) : null;
    }
    public WikiDraft? Create(WorkRequest request, WikiProject destination, WikiDraftContent content, WikiTarget? before,
        WikiEvidence[] evidence, string key)
    {
        lock (_gate)
        {
            using var connection = Open(); using var existing = Command(connection, "SELECT payload FROM wiki_drafts WHERE client_key=$key", ("$key", key));
            if (existing.ExecuteScalar() is string payload)
            {
                var prior = JsonSerializer.Deserialize<WikiDraft>(payload)!;
                return prior.RequestId == request.Id && prior.Content == content && prior.Before == before ? prior : null;
            }
            var draft = new WikiDraft(Guid.NewGuid().ToString("N"), request.Id, request.Project, destination, content, before,
                evidence, 1, "draft", null, null, DateTimeOffset.UtcNow.ToString("O"));
            using var command = Command(connection, "INSERT INTO wiki_drafts VALUES($id,$request,$project,$key,$payload)",
                ("$id", draft.Id), ("$request", request.Id), ("$project", request.Project), ("$key", key), ("$payload", JsonSerializer.Serialize(draft)));
            command.ExecuteNonQuery(); return draft;
        }
    }
    public WikiDraft? Edit(string id, int expectedVersion, WikiDraftContent content)
    {
        lock (_gate)
        {
            var prior = Find(id);
            if (prior is null || prior.Version != expectedVersion || !WikiDraftPolicy.Editable(prior.Status)) return null;
            if ((prior.Content.Conflict && !content.Conflict || prior.Content.MissingInformation && !content.MissingInformation) &&
                string.IsNullOrWhiteSpace(content.ResolutionNote)) return null;
            if (prior.Before is not null && (content.Kind != prior.Before.Kind || content.KnowledgeKey != prior.Before.KnowledgeKey)) return null;
            return Save(prior with { Content = content, Version = prior.Version + 1, UpdatedAt = DateTimeOffset.UtcNow.ToString("O") });
        }
    }
    public WikiDraft? Reserve(string id, int version)
    {
        lock (_gate)
        {
            var prior = Find(id);
            if (prior is null || prior.Version != version || prior.Status is not ("draft" or "uncertain" or "publication-blocked" or "submitted") || !WikiDraftPolicy.CanSubmit(prior)) return null;
            return Save(prior with { Status = "submitting" });
        }
    }
    public WikiDraft Complete(string id, string status, WikiReceipt? receipt = null)
    {
        lock (_gate)
        {
            var draft = Find(id)!;
            return Save(draft with { Status = status, RevisionId = receipt?.RevisionId ?? draft.RevisionId,
                EntryId = receipt?.EntryId ?? draft.EntryId, UpdatedAt = DateTimeOffset.UtcNow.ToString("O") });
        }
    }
    public WikiDraft ConfirmKnowledge(string draftId, string status, string[] requestIds)
    {
        lock (_gate)
        {
            var draft = Find(draftId)!;
            if (draft.Status == status) return draft;
            using var connection = Open(); using var transaction = connection.BeginTransaction();
            foreach (var requestId in requestIds)
            {
                using var command = Command(connection, "INSERT OR IGNORE INTO wiki_scope_reviews VALUES($draft,$request)",
                    ("$draft", draftId), ("$request", requestId)); command.Transaction = transaction; command.ExecuteNonQuery();
            }
            var changed = draft with { Status = status, UpdatedAt = DateTimeOffset.UtcNow.ToString("O") };
            if (status == "approved" && draft.RevisionId is { } revision)
            {
                using var notice = Command(connection,
                    "INSERT OR IGNORE INTO owner_notices VALUES($revision,$request,$body,$created)",
                    ("$revision", revision), ("$request", draft.RequestId),
                    ("$body", WikiDraftPolicy.PublicationSummary(draft)), ("$created", changed.UpdatedAt));
                notice.Transaction = transaction; notice.ExecuteNonQuery();
            }
            using var update = Command(connection, "UPDATE wiki_drafts SET payload=$payload WHERE id=$id",
                ("$id", draftId), ("$payload", JsonSerializer.Serialize(changed)));
            update.Transaction = transaction; update.ExecuteNonQuery();
            transaction.Commit();
            return changed;
        }
    }
    public object[] ScopeReviews()
    {
        using var connection = Open(); using var command = Command(connection, "SELECT draft_id,request_id FROM wiki_scope_reviews");
        using var reader = command.ExecuteReader(); var result = new List<object>();
        while (reader.Read()) result.Add(new { draftId = reader.GetString(0), requestId = reader.GetString(1) });
        return [.. result];
    }
    public void AcknowledgeScope(string draftId, string requestId)
    {
        lock (_gate)
        {
            using var connection = Open(); using var command = Command(connection, "DELETE FROM wiki_scope_reviews WHERE draft_id=$draft AND request_id=$request",
                ("$draft", draftId), ("$request", requestId)); command.ExecuteNonQuery();
        }
    }
    private WikiDraft Save(WikiDraft draft)
    {
        using var connection = Open(); using var command = Command(connection, "UPDATE wiki_drafts SET payload=$payload WHERE id=$id",
            ("$id", draft.Id), ("$payload", JsonSerializer.Serialize(draft))); command.ExecuteNonQuery(); return draft;
    }
    public void RecoverInterrupted()
    {
        foreach (var draft in Drafts().Where(item => item.Status == "submitting")) Complete(draft.Id, "uncertain");
    }
}
