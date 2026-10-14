using Microsoft.Data.Sqlite;

namespace HireZero.Core;

public sealed record Person(string Login, string Role, bool Revoked);
public sealed record WorkRequest(string Id, string Requester, string Project, string Title, string Description,
    string Status, string? SemaForgeId, string CreatedAt, string? ShortTitle = null)
{
    // What lists and headers show: the short title once there is one, otherwise the typed title tidied.
    public string DisplayTitle => ShortTitle ?? ThreadTitles.Readable(Title);
}
public sealed record ConversationMessage(string Id, string RequestId, string Author, string Body, string CreatedAt,
    string Visibility = "conversation");
public sealed record SharedWork(string RequestId, string Summary, string Priority, string Status,
    string? Outcome, string UpdatedAt);
public sealed record MessageDraft(string Body, string ClientRequestId);
public sealed record ConversationSummary(string Id, string Project, string Title, string Status, string CreatedAt, string Requester, string LastAt,
    bool LastFromAgent, int Messages, string? Match, string FullTitle, bool Home = false, ConversationWorkState? WorkState = null, bool AgentAsked = false);

public sealed class RequestStore
{
    private readonly string _databasePath;
    private readonly object _gate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _replyGates = new();
    private readonly Dictionary<string, Dictionary<Guid, CancellationTokenSource>> _agentConsent = [];
    public event Action<string>? AgentWorkRevoked;
    public ActivityLog Activity { get; }
    // Set by the host so activity names the owner as the owner rather than by login.
    public string? OwnerLogin { get; set; }
    public string ActorKind(string login) =>
        login == "workspace-agent" ? ActivityLog.Agent
        : login == WorkspaceOwner.LocalIdentity || login.Equals(OwnerLogin, StringComparison.OrdinalIgnoreCase) ? ActivityLog.Owner
        : FindPerson(login) is { } person ? PersonRoles.Normalize(person.Role) : ActivityLog.Sender;
    public void LogOwner(string? requestId, string action, string summary, string? runId = null, string? workflowId = null) =>
        Activity.Record(requestId, OwnerLogin ?? WorkspaceOwner.LocalIdentity, ActivityLog.Owner, action, summary, runId, workflowId);

    internal T Synchronize<T>(Func<T> action) { lock (_gate) return action(); }
    internal SemaphoreSlim ReplyGate(string requestId) => _replyGates.GetOrAdd(requestId, _ => new(1, 1));
    internal SemaphoreSlim ModelGate(string requestId, string operation) => _replyGates.GetOrAdd(operation + ":" + requestId, _ => new(1, 1));

    public T WithAgentPermission<T>(WorkRequest item, WorkspaceOwner owner, Func<T> action) => Synchronize(() =>
    {
        if (!CanUseAgent(item, owner)) throw new WorkspaceException("Agent work is not allowed on this request.");
        return action();
    });
    public RequestAgentConsent LeaseAgentConsent(WorkRequest item, WorkspaceOwner owner, CancellationToken token) =>
        WithAgentPermission(item, owner, () =>
        {
            var id = Guid.NewGuid(); var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (!_agentConsent.TryGetValue(item.Id, out var active)) _agentConsent[item.Id] = active = [];
            active[id] = source;
            return new RequestAgentConsent(source, () => Synchronize(() =>
            {
                if (_agentConsent.TryGetValue(item.Id, out var leases)) { leases.Remove(id); if (leases.Count == 0) _agentConsent.Remove(item.Id); }
                return true;
            }));
        });
    internal void RevokeAgentWork(string requestId)
    {
        lock (_gate)
        {
            if (_agentConsent.TryGetValue(requestId, out var leases))
                foreach (var source in leases.Values.ToArray())
                    try { source.Cancel(); } catch (ObjectDisposedException) { } catch (AggregateException) { }
            AgentWorkRevoked?.Invoke(requestId);
        }
    }

    public RequestStore(string databasePath)
    {
        _databasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        Activity = new ActivityLog(databasePath);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS people(login TEXT PRIMARY KEY, role TEXT NOT NULL, revoked INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS requests(id TEXT PRIMARY KEY, requester TEXT NOT NULL, project TEXT NOT NULL,
                title TEXT NOT NULL, description TEXT NOT NULL, status TEXT NOT NULL, semaforge_id TEXT, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS messages(id TEXT PRIMARY KEY, request_id TEXT NOT NULL REFERENCES requests(id),
                author TEXT NOT NULL, body TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS messages_by_request ON messages(request_id, created_at);
            CREATE TABLE IF NOT EXISTS owner_notices(revision_id TEXT PRIMARY KEY,
                request_id TEXT NOT NULL REFERENCES requests(id), body TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS owner_notices_by_request ON owner_notices(request_id, created_at);
            CREATE TABLE IF NOT EXISTS owner_agent_notices(id TEXT PRIMARY KEY,request_id TEXT NOT NULL REFERENCES requests(id),body TEXT NOT NULL,created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS shared_work(request_id TEXT PRIMARY KEY REFERENCES requests(id), summary TEXT NOT NULL,
                priority TEXT NOT NULL, status TEXT NOT NULL, outcome TEXT, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS request_keys(requester TEXT NOT NULL, client_key TEXT NOT NULL,
                request_id TEXT NOT NULL REFERENCES requests(id), PRIMARY KEY(requester,client_key));
            CREATE TABLE IF NOT EXISTS message_keys(author TEXT NOT NULL, client_key TEXT NOT NULL,
                message_id TEXT NOT NULL REFERENCES messages(id), PRIMARY KEY(author,client_key));
            CREATE TABLE IF NOT EXISTS message_drafts(request_id TEXT NOT NULL REFERENCES requests(id),
                author TEXT NOT NULL, body TEXT NOT NULL, client_key TEXT NOT NULL, PRIMARY KEY(request_id,author));
            CREATE TABLE IF NOT EXISTS intake_order(request_id TEXT PRIMARY KEY REFERENCES requests(id), position INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS home_threads(login TEXT PRIMARY KEY, request_id TEXT NOT NULL REFERENCES requests(id));
            CREATE TABLE IF NOT EXISTS home_tasks(request_id TEXT PRIMARY KEY REFERENCES requests(id), home_id TEXT NOT NULL REFERENCES requests(id), task_key TEXT);
            CREATE TABLE IF NOT EXISTS thread_titles(request_id TEXT PRIMARY KEY REFERENCES requests(id), title TEXT NOT NULL, source TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS agent_permissions(request_id TEXT PRIMARY KEY REFERENCES requests(id), granted_at TEXT NOT NULL, consent_generation INTEGER NOT NULL DEFAULT -1);
            CREATE TABLE IF NOT EXISTS request_agent_epochs(request_id TEXT PRIMARY KEY REFERENCES requests(id), generation INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS request_triage(request_id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS request_authority_versions(id TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS request_agent_upgrade_review(request_id TEXT PRIMARY KEY REFERENCES requests(id));
            """;
        command.ExecuteNonQuery();
        using var columns = Command(connection, "PRAGMA table_info(agent_permissions)");
        var hasGeneration = false;
        using (var reader = columns.ExecuteReader()) while (reader.Read()) if (reader.GetString(1) == "consent_generation") hasGeneration = true;
        if (!hasGeneration)
        {
            using var migrate = Command(connection, "ALTER TABLE agent_permissions ADD COLUMN consent_generation INTEGER NOT NULL DEFAULT -1"); migrate.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using var version = Command(connection, "SELECT 1 FROM request_authority_versions WHERE id='positive-consent-v1'"); version.Transaction = transaction;
        if (version.ExecuteScalar() is null)
        {
            // An older database never recorded enough history to distinguish a safe grant from revoke/restore.
            // Preserve records and receipts, but require a new request grant AND a new workflow proposal/approval.
            using var epochs = Command(connection, "UPDATE request_agent_epochs SET generation=MAX(generation,0)+1; INSERT OR IGNORE INTO request_agent_epochs SELECT id,1 FROM requests; INSERT OR IGNORE INTO request_agent_upgrade_review SELECT id FROM requests");
            epochs.Transaction = transaction; epochs.ExecuteNonQuery();
            using var table = Command(connection, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='agent_tasks'"); table.Transaction = transaction;
            if (table.ExecuteScalar() is not null)
            {
                using var stop = Command(connection, "UPDATE agent_tasks SET state='canceled',detail='Upgrade requires fresh owner confirmation; historical consent is unknown.',updated_at=$at WHERE state IN ('queued','active')", ("$at", DateTimeOffset.UtcNow.ToString("O")));
                stop.Transaction = transaction; stop.ExecuteNonQuery();
            }
            using var stamp = Command(connection, "INSERT INTO request_authority_versions VALUES('positive-consent-v1')"); stamp.Transaction = transaction; stamp.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString());
        connection.Open();
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys=ON";
        foreignKeys.ExecuteNonQuery();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Key, object? Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    public Person? FindPerson(string login)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT login,role,revoked FROM people WHERE login=$login", ("$login", login));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0) : null;
    }

    public Person[] People()
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT login,role,revoked FROM people ORDER BY login");
        using var reader = command.ExecuteReader();
        var results = new List<Person>();
        while (reader.Read()) results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0));
        return [.. results];
    }

    // The owner let the agent work on this request (needed for visitors, whose requests otherwise get no agent effort).
    public void AllowAgent(string requestId)
    {
        lock (_gate)
        {
            if (AgentDeclined(requestId)) throw new WorkspaceException("This request was declined; agent work remains blocked.");
            if (FindRequest(requestId) is not { } request || AgentRequestClosed(request.Status)) throw new WorkspaceException("This request is closed or no longer exists.");
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = Command(connection, "INSERT INTO agent_permissions(request_id,granted_at,consent_generation) VALUES($id,$at,$generation) " +
                "ON CONFLICT(request_id) DO UPDATE SET granted_at=$at,consent_generation=$generation WHERE agent_permissions.consent_generation!=$generation",
                ("$id", requestId), ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$generation", AgentConsentGeneration(requestId)));
            command.Transaction = transaction; var granted = command.ExecuteNonQuery() > 0;
            using var reviewed = Command(connection, "DELETE FROM request_agent_upgrade_review WHERE request_id=$id AND EXISTS(SELECT 1 FROM agent_permissions p JOIN request_agent_epochs e ON e.request_id=p.request_id WHERE p.request_id=$id AND e.generation>0 AND p.consent_generation=e.generation)", ("$id", requestId)); reviewed.Transaction = transaction; reviewed.ExecuteNonQuery();
            transaction.Commit();
            if (granted) LogOwner(requestId, "request.allowed", "Let the agent work on this request.");
        }
    }
    public bool AgentAllowed(string requestId)
    {
        if (AgentDeclined(requestId)) return false;
        using var connection = Open();
        using var command = Command(connection, "SELECT 1 FROM agent_permissions p LEFT JOIN request_agent_epochs e ON e.request_id=p.request_id " +
            "WHERE p.request_id=$id AND e.generation>0 AND p.consent_generation=e.generation", ("$id", requestId));
        return command.ExecuteScalar() is not null;
    }

    public bool AgentDeclined(string requestId)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT json_extract(payload,'$.decision') FROM request_triage WHERE request_id=$id", ("$id", requestId));
        return command.ExecuteScalar() is "declined";
    }

    public long AgentConsentGeneration(string requestId)
    {
        using var connection = Open(); using var command = Command(connection,
            "SELECT generation FROM request_agent_epochs WHERE request_id=$id", ("$id", requestId));
        return command.ExecuteScalar() is long generation && generation > 0 ? generation : -1;
    }
    public bool AgentUpgradeReviewRequired(string requestId)
    {
        using var connection = Open(); using var command = Command(connection, "SELECT 1 FROM request_agent_upgrade_review WHERE request_id=$id", ("$id", requestId));
        return command.ExecuteScalar() is not null;
    }
    public static bool AgentRequestClosed(string status) => status is "archived" or "closed" or "canceled" or "completed" or "done";
    public bool CanUseAgent(WorkRequest item, WorkspaceOwner owner)
    {
        var current = FindRequest(item.Id);
        return current is not null && AgentConsentGeneration(current.Id)>0 && !AgentUpgradeReviewRequired(current.Id) && !AgentRequestClosed(current.Status) && !AgentDeclined(current.Id) &&
            (owner.Owns(current) || FindPerson(current.Requester) is { Revoked: false } && AgentAllowed(current.Id));
    }

    public string? PendingReplyKey(string requestId)
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT m.id,m.author FROM messages m LEFT JOIN message_keys k ON k.message_id=m.id " +
            "WHERE m.request_id=$id AND NOT (m.author='workspace-agent' AND COALESCE(k.client_key,'') IN ($ack,$decision)) " +
            "ORDER BY m.created_at DESC,m.id DESC LIMIT 1", ("$id", requestId), ("$ack", requestId + ":visitor-ack"), ("$decision", requestId + ":decision"));
        using var reader = command.ExecuteReader();
        return !reader.Read() ? "request" : reader.GetString(1) == "workspace-agent" ? null : reader.GetString(0);
    }

    internal string ReplyQueueKey(string requestId, string pending)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT granted_at FROM agent_permissions WHERE request_id=$id", ("$id", requestId));
        return command.ExecuteScalar() is string granted ? "allowed:" + granted + ":" + pending : pending;
    }

    public WorkRequest[] GrantedRequestsAfter(string cursor, int limit = 32)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT r.id FROM agent_permissions p JOIN requests r ON r.id=p.request_id WHERE r.id>$cursor ORDER BY r.id LIMIT $limit",
            ("$cursor", cursor), ("$limit", Math.Clamp(limit, 1, 32)));
        using var reader = command.ExecuteReader(); var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return [.. ids.Select(FindRequest).OfType<WorkRequest>()];
    }

    internal bool TryAddAgentReply(WorkRequest item, WorkspaceOwner owner, string pending, string answer) => Synchronize(() =>
    {
        if (!CanUseAgent(item, owner) || PendingReplyKey(item.Id) != pending) return false;
        AddMessage(item.Id, "workspace-agent", answer, item.Id + ":reply:" + pending);
        return true;
    });

    public Person SetPerson(string login, string role, bool revoked)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = Command(connection,
                "INSERT INTO people(login,role,revoked) VALUES($login,$role,$revoked) " +
                "ON CONFLICT(login) DO UPDATE SET role=excluded.role,revoked=excluded.revoked",
                ("$login", login), ("$role", role), ("$revoked", revoked ? 1 : 0));
            command.Transaction = transaction; command.ExecuteNonQuery();
            if (revoked)
            {
                using var drafts = Command(connection, "DELETE FROM message_drafts WHERE author=$login", ("$login", login));
                drafts.Transaction = transaction; drafts.ExecuteNonQuery();
                using var invalidate = Command(connection, "INSERT INTO request_agent_epochs(request_id,generation) SELECT id,1 FROM requests WHERE requester=$login " +
                    "ON CONFLICT(request_id) DO UPDATE SET generation=generation+1", ("$login", login));
                invalidate.Transaction = transaction; invalidate.ExecuteNonQuery();
            }
            transaction.Commit();
            if (revoked) foreach (var request in Requests(login)) RevokeAgentWork(request.Id);
        }
        return new(login, role, revoked);
    }

    public WorkRequest CreateRequest(string requester, string project, string title, string description, string clientKey)
    {
        var item = new WorkRequest(Guid.NewGuid().ToString("N"), requester, project, title, description,
            "queued", null, DateTimeOffset.UtcNow.ToString("O"));
        lock (_gate)
        {
            using var connection = Open();
            using var existing = Command(connection,
                "SELECT request_id FROM request_keys WHERE requester=$requester AND client_key=$key",
                ("$requester", requester), ("$key", clientKey));
            if (existing.ExecuteScalar() is string existingId) return FindRequest(existingId)!;
            using var transaction = connection.BeginTransaction();
            using var command = Command(connection,
                "INSERT INTO requests VALUES($id,$requester,$project,$title,$description,$status,$sema,$created)",
                ("$id", item.Id), ("$requester", item.Requester), ("$project", item.Project),
                ("$title", item.Title), ("$description", item.Description), ("$status", item.Status),
                ("$sema", item.SemaForgeId), ("$created", item.CreatedAt));
            command.Transaction = transaction;
            command.ExecuteNonQuery();
            using var key = Command(connection,
                "INSERT INTO request_keys VALUES($requester,$key,$id)",
                ("$requester", requester), ("$key", clientKey), ("$id", item.Id));
            key.Transaction = transaction;
            key.ExecuteNonQuery();
            using var epoch = Command(connection, "INSERT INTO request_agent_epochs VALUES($id,1)", ("$id", item.Id)); epoch.Transaction = transaction; epoch.ExecuteNonQuery();
            transaction.Commit();
            Activity.Record(item.Id, requester, ActorKind(requester), "request.created", item.Title);
        }
        return item;
    }

    public WorkRequest? FindRequest(string id)
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT r.id,r.requester,r.project,r.title,r.description,r.status,r.semaforge_id,r.created_at,t.title FROM requests r LEFT JOIN thread_titles t ON t.request_id=r.id WHERE r.id=$id", ("$id", id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRequest(reader) : null;
    }

    private static WorkRequest ReadRequest(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8));

    // A thread's short title. The model's (or the tidied fallback) never replaces one already saved; an owner's rename does.
    public void SaveShortTitle(string requestId, string title, string source, bool replace)
    {
        lock (_gate)
        {
            using var connection = Open(); using var command = Command(connection,
                (replace ? "INSERT OR REPLACE" : "INSERT OR IGNORE") + " INTO thread_titles VALUES($id,$title,$source,$at)",
                ("$id", requestId), ("$title", title), ("$source", source), ("$at", DateTimeOffset.UtcNow.ToString("O")));
            command.ExecuteNonQuery();
        }
    }

    public WorkRequest[] Requests(string? requester)
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT r.id,r.requester,r.project,r.title,r.description,r.status,r.semaforge_id,r.created_at,t.title FROM requests r LEFT JOIN thread_titles t ON t.request_id=r.id " +
            "WHERE $requester IS NULL OR r.requester=$requester ORDER BY r.created_at DESC", ("$requester", requester));
        using var reader = command.ExecuteReader();
        var results = new List<WorkRequest>();
        while (reader.Read()) results.Add(ReadRequest(reader));
        return [.. results];
    }

    // Conversations for the sidebar, latest activity first: when each last moved, who spoke last, and for a search,
    // where it matched. The search covers titles, projects, the opening request and conversation messages (never
    // owner-only notices), and only the conversations this requester can already read.
    public ConversationSummary[] Conversations(string? requester, string? query, int limit = 200)
    {
        var term = query?.Trim() is { Length: > 0 } text ? "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%" : null;
        using var connection = Open();
        using var command = Command(connection,
            "SELECT r.id,r.project,r.title,r.status,r.created_at,r.requester,t.title,EXISTS(SELECT 1 FROM home_threads h WHERE h.request_id=r.id)," +
            " (SELECT MAX(created_at) FROM messages m WHERE m.request_id=r.id)," +
            " (SELECT author FROM messages m WHERE m.request_id=r.id ORDER BY created_at DESC LIMIT 1)," +
            " (SELECT COUNT(*) FROM messages m WHERE m.request_id=r.id)," +
            " CASE WHEN $q IS NULL THEN NULL ELSE (SELECT body FROM messages m WHERE m.request_id=r.id AND m.body LIKE $q ESCAPE '\\' ORDER BY created_at DESC LIMIT 1) END," +
            " (SELECT substr(body, -400) FROM messages m WHERE m.request_id=r.id ORDER BY created_at DESC LIMIT 1) " +
            "FROM requests r LEFT JOIN thread_titles t ON t.request_id=r.id WHERE ($requester IS NULL OR r.requester=$requester) AND ($q IS NULL OR r.title LIKE $q ESCAPE '\\' OR t.title LIKE $q ESCAPE '\\' OR r.project LIKE $q ESCAPE '\\' " +
            " OR r.description LIKE $q ESCAPE '\\' OR EXISTS (SELECT 1 FROM messages m WHERE m.request_id=r.id AND m.body LIKE $q ESCAPE '\\')) " +
            "ORDER BY COALESCE((SELECT MAX(created_at) FROM messages m WHERE m.request_id=r.id), r.created_at) DESC LIMIT $limit",
            ("$requester", requester), ("$q", term), ("$limit", Math.Clamp(limit, 1, 500)));
        using var reader = command.ExecuteReader(); var results = new List<ConversationSummary>();
        while (reader.Read())
        {
            string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
            var match = Text(11);
            results.Add(new(reader.GetString(0), reader.GetString(1), Text(6) ?? ThreadTitles.Readable(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                Text(8) ?? reader.GetString(4), Text(9) == "workspace-agent", reader.GetInt32(10), match is null ? null : Snippet(match, query!), reader.GetString(2), reader.GetInt64(7) == 1,
                AgentAsked: Text(9) == "workspace-agent" && AsksOwner(Text(12))));
        }
        return [.. results];
    }

    // The agent's message ends by asking the owner something (a question in its closing lines), not just reporting.
    public static bool AsksOwner(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var tail = body.TrimEnd();
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.TakeLast(3).Any(line => line.EndsWith('?') || line.EndsWith("?*", StringComparison.Ordinal) || line.EndsWith("?)", StringComparison.Ordinal));
    }

    // A short window of the matched message around the search term.
    static string Snippet(string body, string query)
    {
        var flat = body.ReplaceLineEndings(" ");
        var at = flat.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, at - 50);
        var piece = flat.Substring(start, Math.Min(150, flat.Length - start)).Trim();
        return (start > 0 ? "…" : "") + piece + (start + 150 < flat.Length ? "…" : "");
    }

    public ConversationMessage AddMessage(string requestId, string author, string body, string clientKey)
    {
        var message = new ConversationMessage(Guid.NewGuid().ToString("N"), requestId, author, body,
            DateTimeOffset.UtcNow.ToString("O"));
        lock (_gate)
        {
            using var connection = Open();
            using var existing = Command(connection,
                "SELECT m.id,m.request_id,m.author,m.body,m.created_at FROM messages m " +
                "JOIN message_keys k ON k.message_id=m.id WHERE k.author=$author AND k.client_key=$key",
                ("$author", author), ("$key", clientKey));
            using (var reader = existing.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
                }
            }
            using var transaction = connection.BeginTransaction();
            using var command = Command(connection, "INSERT INTO messages VALUES($id,$request,$author,$body,$created)",
                ("$id", message.Id), ("$request", requestId), ("$author", author),
                ("$body", body), ("$created", message.CreatedAt));
            command.Transaction = transaction;
            command.ExecuteNonQuery();
            using var key = Command(connection,
                "INSERT INTO message_keys VALUES($author,$key,$id)",
                ("$author", author), ("$key", clientKey), ("$id", message.Id));
            key.Transaction = transaction;
            key.ExecuteNonQuery();
            using var draft = Command(connection,
                "DELETE FROM message_drafts WHERE request_id=$request AND author=$author AND client_key=$key",
                ("$request", requestId), ("$author", author), ("$key", clientKey));
            draft.Transaction = transaction;
            draft.ExecuteNonQuery();
            transaction.Commit();
            Activity.Record(requestId, author, ActorKind(author), "message.posted", body);
            if (author != "workspace-agent") RevokeAgentWork(requestId);
        }
        return message;
    }

    public ConversationMessage[] Messages(string requestId)
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT id,request_id,author,body,created_at FROM messages WHERE request_id=$id ORDER BY created_at,id", ("$id", requestId));
        using var reader = command.ExecuteReader();
        var results = new List<ConversationMessage>();
        while (reader.Read()) results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4)));
        return [.. results];
    }

    // Private notices never enter Messages(), which is the collaborator agent's transcript.
    public ConversationMessage[] OwnerMessages(string requestId)
    {
        var results = new List<ConversationMessage>(Messages(requestId));
        using var connection = Open();
        using var command = Command(connection,
            "SELECT revision_id,body,created_at FROM owner_notices WHERE request_id=$id", ("$id", requestId));
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(new("wiki:" + reader.GetString(0), requestId, "wiki-update",
            reader.GetString(1), reader.GetString(2), "owner"));
        reader.Close();
        using var agentNotices = Command(connection, "SELECT id,body,created_at FROM owner_agent_notices WHERE request_id=$id", ("$id", requestId));
        using var agentReader = agentNotices.ExecuteReader();
        while (agentReader.Read()) results.Add(new(agentReader.GetString(0), requestId, "workspace-agent", agentReader.GetString(1), agentReader.GetString(2), "owner"));
        return [.. results.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)];
    }

    // Raised once per new owner-only notice (requestId, body, noticeId); Home echoes them.
    public event Action<string, string, string>? NoticeAdded;
    public void AddOwnerAgentNotice(string requestId, string body, string id)
    {
        int added;
        lock (_gate)
        {
            using var connection = Open(); using var command = Command(connection,
                "INSERT OR IGNORE INTO owner_agent_notices VALUES($id,$request,$body,$created)", ("$id", id), ("$request", requestId),
                ("$body", body), ("$created", DateTimeOffset.UtcNow.ToString("O")));
            added = command.ExecuteNonQuery();
        }
        if (added == 1) NoticeAdded?.Invoke(requestId, body, id);
    }
    public string? LatestOwnerNotice(string requestId)
    {
        using var connection = Open(); using var command = Command(connection,
            "SELECT body FROM owner_agent_notices WHERE request_id=$request ORDER BY created_at DESC LIMIT 1", ("$request", requestId));
        return command.ExecuteScalar() as string;
    }

    // Each person's Home: one perpetual conversation with the agent, created on first use.
    public WorkRequest? FindHome(string login)
    {
        using var connection = Open(); using var find = Command(connection, "SELECT request_id FROM home_threads WHERE login=$login", ("$login", login));
        return find.ExecuteScalar() is string id ? FindRequest(id) : null;
    }
    public WorkRequest Home(string login)
    {
        using (var connection = Open())
        using (var find = Command(connection, "SELECT request_id FROM home_threads WHERE login=$login", ("$login", login)))
            if (find.ExecuteScalar() is string existing && FindRequest(existing) is { } found) return found;
        var home = CreateRequest(login, "Home", "Home", "The main conversation with the Workspace agent.", "home:" + login);
        lock (_gate)
        {
            using var connection = Open(); using var command = Command(connection,
                "INSERT OR REPLACE INTO home_threads VALUES($login,$id)", ("$login", login), ("$id", home.Id));
            command.ExecuteNonQuery();
            // Home is never a request awaiting review; it has its own status.
            using var status = Command(connection, "UPDATE requests SET status='home' WHERE id=$id", ("$id", home.Id));
            status.ExecuteNonQuery();
        }
        SaveShortTitle(home.Id, "Home", "owner", replace: true);
        return FindRequest(home.Id)!;
    }
    public bool IsHome(string requestId)
    {
        using var connection = Open(); using var command = Command(connection, "SELECT 1 FROM home_threads WHERE request_id=$id", ("$id", requestId));
        return command.ExecuteScalar() is not null;
    }
    public void MarkTextConversation(string requestId)
    {
        using var connection = Open(); using var command = Command(connection,
            "UPDATE requests SET status='chat' WHERE id=$id AND status='queued'", ("$id", requestId));
        command.ExecuteNonQuery();
    }
    // Task threads spun off from Home, keyed so the same ask reuses the open thread.
    public void LinkHomeTask(string requestId, string homeId, string? key)
    {
        lock (_gate)
        {
            using var connection = Open(); using var command = Command(connection,
                "INSERT OR REPLACE INTO home_tasks VALUES($id,$home,$key)", ("$id", requestId), ("$home", homeId), ("$key", key));
            command.ExecuteNonQuery();
        }
    }
    public string? HomeTask(string requestId)
    {
        using var connection = Open(); using var command = Command(connection, "SELECT home_id FROM home_tasks WHERE request_id=$id", ("$id", requestId));
        return command.ExecuteScalar() as string;
    }
    public WorkRequest? OpenHomeTask(string homeId, string key)
    {
        using var connection = Open(); using var command = Command(connection,
            "SELECT h.request_id FROM home_tasks h JOIN requests r ON r.id=h.request_id WHERE h.home_id=$home AND h.task_key=$key AND r.status!='archived' ORDER BY r.created_at DESC LIMIT 1",
            ("$home", homeId), ("$key", key));
        return command.ExecuteScalar() is string id ? FindRequest(id) : null;
    }
    public void LinkConfirmedWork(string requestId,string workItemId)
    {
        lock(_gate)
        {
            using var connection=Open();using var command=Command(connection,
                "UPDATE requests SET status='ready',semaforge_id=COALESCE(semaforge_id,$work) WHERE id=$id AND status='queued'",
                ("$id",requestId),("$work",workItemId));command.ExecuteNonQuery();
        }
    }

    public MessageDraft Draft(string requestId, string author)
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT body,client_key FROM message_drafts WHERE request_id=$request AND author=$author",
            ("$request", requestId), ("$author", author));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1)) : new("", "");
    }

    public void SaveDraft(string requestId, string author, string body, string clientKey)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var sent = Command(connection, "SELECT 1 FROM message_keys WHERE author=$author AND client_key=$key",
                ("$author", author), ("$key", clientKey));
            if (sent.ExecuteScalar() is not null) return; // A delayed autosave cannot restore a sent message.
            using var command = body.Length == 0
                ? Command(connection, "DELETE FROM message_drafts WHERE request_id=$request AND author=$author",
                    ("$request", requestId), ("$author", author))
                : Command(connection,
                    "INSERT INTO message_drafts VALUES($request,$author,$body,$key) " +
                    "ON CONFLICT(request_id,author) DO UPDATE SET body=excluded.body,client_key=excluded.client_key",
                    ("$request", requestId), ("$author", author), ("$body", body), ("$key", clientKey));
            command.ExecuteNonQuery();
        }
    }

    public string[] IntakeOrder()
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT r.id FROM requests r LEFT JOIN intake_order q ON q.request_id=r.id " +
            "WHERE r.status='queued' ORDER BY COALESCE(q.position,2147483647),r.created_at,r.id");
        using var reader = command.ExecuteReader();
        var results = new List<string>();
        while (reader.Read()) results.Add(reader.GetString(0));
        return [.. results];
    }

    public bool SetIntakeOrder(string[] ids)
    {
        lock (_gate)
        {
            var current = IntakeOrder();
            if (ids.Length != current.Length || ids.Distinct().Count() != ids.Length ||
                !ids.ToHashSet(StringComparer.Ordinal).SetEquals(current)) return false;
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var clear = Command(connection, "DELETE FROM intake_order");
            clear.Transaction = transaction;
            clear.ExecuteNonQuery();
            for (var index = 0; index < ids.Length; index++)
            {
                using var insert = Command(connection, "INSERT INTO intake_order VALUES($id,$position)",
                    ("$id", ids[index]), ("$position", index));
                insert.Transaction = transaction;
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
            return true;
        }
    }

    public SharedWork Publish(string requestId, string summary, string priority, string status, string? outcome)
    {
        var shared = new SharedWork(requestId, summary, priority, status, outcome, DateTimeOffset.UtcNow.ToString("O"));
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection,
                "INSERT INTO shared_work VALUES($id,$summary,$priority,$status,$outcome,$updated) " +
                "ON CONFLICT(request_id) DO UPDATE SET summary=excluded.summary,priority=excluded.priority," +
                "status=excluded.status,outcome=excluded.outcome,updated_at=excluded.updated_at",
                ("$id", requestId), ("$summary", summary), ("$priority", priority),
                ("$status", status), ("$outcome", outcome), ("$updated", shared.UpdatedAt));
            command.ExecuteNonQuery();
        }
        return shared;
    }

    public SharedWork[] Workload()
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT request_id,summary,priority,status,outcome,updated_at FROM shared_work ORDER BY updated_at DESC");
        using var reader = command.ExecuteReader();
        var results = new List<SharedWork>();
        while (reader.Read()) results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        return [.. results];
    }

    // Hides a conversation from intake; its messages stay readable from Projects.
    public bool ArchiveRequest(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = Command(connection, "UPDATE requests SET status='archived' WHERE id=$id AND status='queued'", ("$id", id));
            command.Transaction = transaction;
            if (command.ExecuteNonQuery() != 1) { transaction.Rollback(); return false; }
            using var invalidate = Command(connection, "INSERT INTO request_agent_epochs VALUES($id,1) ON CONFLICT(request_id) DO UPDATE SET generation=generation+1", ("$id", id));
            invalidate.Transaction = transaction; invalidate.ExecuteNonQuery();
            using var taskTable = Command(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='agent_tasks'");
            taskTable.Transaction = transaction;
            if (Convert.ToInt32(taskTable.ExecuteScalar()) == 1)
            {
                using var cancel = Command(connection, "UPDATE agent_tasks SET state='canceled',detail='Request archived.',updated_at=$now WHERE request_id=$id AND state IN ('queued','active')", ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$id", id));
                cancel.Transaction = transaction; cancel.ExecuteNonQuery();
            }
            transaction.Commit();
            RevokeAgentWork(id); return true;
        }
    }

    public bool ReservePromotion(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection,
                "UPDATE requests SET status='promotion-pending' WHERE id=$id AND status='queued'", ("$id", id));
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void FinishPromotion(string id, string semaForgeId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection,
                "UPDATE requests SET status='ready',semaforge_id=$sema WHERE id=$id AND status='promotion-pending'",
                ("$id", id), ("$sema", semaForgeId));
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Promotion state changed.");
        }
    }
}
