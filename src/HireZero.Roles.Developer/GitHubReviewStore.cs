using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Roles.Developer;

public sealed class GitHubReviewStore
{
    private readonly string _path;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
    private readonly ActivityLog _activity;

    public GitHubReviewStore(string path)
    {
        _path = path;
        _activity = new ActivityLog(path);
        using var database = Open();
        using var command = database.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS github_review_repositories(id TEXT PRIMARY KEY, account_id INTEGER NOT NULL,
              repository_id INTEGER NOT NULL, name TEXT NOT NULL, root TEXT NOT NULL, project TEXT NOT NULL,
              UNIQUE(account_id,repository_id,root));
            CREATE TABLE IF NOT EXISTS github_review_fixes(id TEXT PRIMARY KEY, repository_id TEXT NOT NULL,
              number INTEGER NOT NULL, head TEXT NOT NULL, snapshot TEXT NOT NULL, threads TEXT NOT NULL,
              status TEXT NOT NULL, work_item_id TEXT, scout_id TEXT, builder_id TEXT,
              UNIQUE(repository_id,number,head,snapshot));
            CREATE TABLE IF NOT EXISTS github_repository_proposals(id TEXT PRIMARY KEY, request_id TEXT NOT NULL,
              account_id INTEGER NOT NULL, choices TEXT NOT NULL, confirmed INTEGER NOT NULL, UNIQUE(request_id,id));
            CREATE TABLE IF NOT EXISTS github_review_policies(repository_id TEXT PRIMARY KEY, automatic INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS github_pr_workflows(id TEXT PRIMARY KEY,request_id TEXT NOT NULL,account_id INTEGER NOT NULL,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS github_fix_loops(id TEXT PRIMARY KEY,request_id TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS github_postgres_profiles(repository TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS github_workflow_approvals(workflow_id TEXT NOT NULL,action TEXT NOT NULL,snapshot TEXT NOT NULL,
              expires_at TEXT NOT NULL,consumed_at TEXT,PRIMARY KEY(workflow_id,action));
            CREATE TABLE IF NOT EXISTS github_workflow_stamps(workflow_id TEXT PRIMARY KEY,updated_at TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        command.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('github_repository_proposals') WHERE name='action'";
        if ((long)command.ExecuteScalar()! == 0)
        {
            command.CommandText =
                "ALTER TABLE github_repository_proposals ADD COLUMN action TEXT NOT NULL DEFAULT 'connect'";
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var database = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        database.Open();
        return database;
    }

    private static SqliteCommand Command(
        SqliteConnection database,
        string sql,
        params (string Key, object? Value)[] values
    )
    {
        var command = database.CreateCommand();
        command.CommandText = sql;
        foreach (var value in values)
            command.Parameters.AddWithValue(value.Key, value.Value ?? DBNull.Value);
        return command;
    }

    public ReviewRepository[] Repositories(long account)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT id,account_id,repository_id,name,root,project FROM github_review_repositories WHERE account_id=$account ORDER BY name",
            ("$account", account)
        );
        using var reader = command.ExecuteReader();
        var result = new List<ReviewRepository>();
        while (reader.Read())
            result.Add(
                new(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)
                )
            );
        return [.. result];
    }

    public string[] RepositoryNames()
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT DISTINCT name FROM github_review_repositories ORDER BY name LIMIT 20"
        );
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return [.. names];
    }

    public ReviewRepository Add(ReviewRepository repository)
    {
        using var database = Open();
        using var command = Command(
            database,
            "INSERT OR IGNORE INTO github_review_repositories VALUES($id,$account,$repo,$name,$root,$project)",
            ("$id", repository.Id),
            ("$account", repository.AccountId),
            ("$repo", repository.GitHubId),
            ("$name", repository.Name),
            ("$root", repository.RootPath),
            ("$project", repository.Project)
        );
        command.ExecuteNonQuery();
        return Repositories(repository.AccountId)
            .Single(item => item.GitHubId == repository.GitHubId && item.RootPath == repository.RootPath);
    }

    public void Remove(string id)
    {
        using var database = Open();
        using var command = Command(database, "DELETE FROM github_review_repositories WHERE id=$id", ("$id", id));
        command.ExecuteNonQuery();
    }

    public void SetRoot(string id, string root)
    {
        using var database = Open();
        using var command = Command(
            database,
            "UPDATE github_review_repositories SET root=$root WHERE id=$id",
            ("$root", root),
            ("$id", id)
        );
        command.ExecuteNonQuery();
    }

    public void SetAutomatic(string id, bool enabled)
    {
        using var database = Open();
        using var command = Command(
            database,
            "INSERT INTO github_review_policies VALUES($id,$enabled) ON CONFLICT(repository_id) DO UPDATE SET automatic=excluded.automatic",
            ("$id", id),
            ("$enabled", enabled ? 1 : 0)
        );
        command.ExecuteNonQuery();
    }

    public bool Automatic(string id)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT automatic FROM github_review_policies WHERE repository_id=$id",
            ("$id", id)
        );
        return command.ExecuteScalar() is long value && value == 1;
    }

    public bool HasAutomatic()
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT COUNT(*) FROM github_review_policies p JOIN github_review_repositories r ON r.id=p.repository_id WHERE p.automatic=1"
        );
        return (long)command.ExecuteScalar()! > 0;
    }

    public RepositoryConnectionProposal[] Proposals(string requestId, long accountId)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT id,request_id,account_id,choices,confirmed,action FROM github_repository_proposals WHERE request_id=$request AND account_id=$account",
            ("$request", requestId),
            ("$account", accountId)
        );
        using var reader = command.ExecuteReader();
        var result = new List<RepositoryConnectionProposal>();
        while (reader.Read())
            result.Add(
                new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    JsonSerializer.Deserialize<RepositoryChoice[]>(reader.GetString(3))!,
                    reader.GetInt64(4) == 1,
                    reader.GetString(5)
                )
            );
        return [.. result];
    }

    public void SaveProposal(RepositoryConnectionProposal proposal)
    {
        using var database = Open();
        using var command = Command(
            database,
            "INSERT INTO github_repository_proposals(id,request_id,account_id,choices,confirmed,action) VALUES($id,$request,$account,$choices,$confirmed,$action) ON CONFLICT(id) DO UPDATE SET confirmed=excluded.confirmed",
            ("$id", proposal.Id),
            ("$request", proposal.RequestId),
            ("$account", proposal.AccountId),
            ("$choices", JsonSerializer.Serialize(proposal.Choices)),
            ("$confirmed", proposal.Confirmed ? 1 : 0),
            ("$action", proposal.Action)
        );
        command.ExecuteNonQuery();
    }

    public ReviewFix[] Fixes(string repositoryId)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT id,repository_id,number,head,snapshot,threads,status,work_item_id,scout_id,builder_id FROM github_review_fixes WHERE repository_id=$repo ORDER BY rowid DESC",
            ("$repo", repositoryId)
        );
        using var reader = command.ExecuteReader();
        var result = new List<ReviewFix>();
        while (reader.Read())
            result.Add(
                new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(5))!,
                    reader.GetString(6),
                    Nullable(reader, 7),
                    Nullable(reader, 8),
                    Nullable(reader, 9)
                )
            );
        return [.. result];
    }

    public ReviewFix Add(ReviewFix fix)
    {
        using var database = Open();
        using var command = Command(
            database,
            "INSERT OR IGNORE INTO github_review_fixes VALUES($id,$repo,$number,$head,$snapshot,$threads,$status,NULL,NULL,NULL)",
            ("$id", fix.Id),
            ("$repo", fix.RepositoryId),
            ("$number", fix.Number),
            ("$head", fix.HeadSha),
            ("$snapshot", fix.Snapshot),
            ("$threads", JsonSerializer.Serialize(fix.ThreadIds)),
            ("$status", fix.Status)
        );
        command.ExecuteNonQuery();
        return Fixes(fix.RepositoryId)
            .Single(item => item.Number == fix.Number && item.HeadSha == fix.HeadSha && item.Snapshot == fix.Snapshot);
    }

    public void Save(ReviewFix fix)
    {
        using var database = Open();
        using var command = Command(
            database,
            "UPDATE github_review_fixes SET status=$status,work_item_id=$work,scout_id=$scout,builder_id=$builder,head=$head,snapshot=$snapshot WHERE id=$id",
            ("$status", fix.Status),
            ("$work", fix.WorkItemId),
            ("$scout", fix.ScoutId),
            ("$builder", fix.BuilderId),
            ("$head", fix.HeadSha),
            ("$snapshot", fix.Snapshot),
            ("$id", fix.Id)
        );
        command.ExecuteNonQuery();
    }

    private static string? Nullable(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);

    public bool ApprovalAvailable(PullRequestWorkflow workflow)
    {
        var publish = workflow.State == "publication-proposed";
        using var database = Open();
        using var command = Command(
            database,
            "SELECT COUNT(*) FROM github_workflow_approvals WHERE workflow_id=$id AND action=$action AND snapshot=$snapshot AND consumed_at IS NULL AND expires_at>$now",
            ("$id", workflow.Id),
            ("$action", publish ? "publish" : "repair"),
            ("$snapshot", publish ? workflow.PublicationSnapshot : workflow.ApprovalSnapshot),
            ("$now", Clock().ToUniversalTime().ToString("O"))
        );
        return (long)command.ExecuteScalar()! == 1;
    }

    public PullRequestWorkflow[] Workflows(long? accountId = null, string? requestId = null)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT payload FROM github_pr_workflows WHERE ($account IS NULL OR account_id=$account) AND ($request IS NULL OR request_id=$request) ORDER BY rowid DESC",
            ("$account", accountId),
            ("$request", requestId)
        );
        using var reader = command.ExecuteReader();
        var items = new List<PullRequestWorkflow>();
        while (reader.Read())
            items.Add(JsonSerializer.Deserialize<PullRequestWorkflow>(reader.GetString(0))!);
        return [.. items];
    }

    public PostgresProfile? PostgresProfile(string repository)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT payload FROM github_postgres_profiles WHERE repository=$repository",
            ("$repository", repository.ToLowerInvariant())
        );
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<PostgresProfile>(payload) : null;
    }

    public void SavePostgresProfile(PostgresProfile? profile, string repository)
    {
        using var database = Open();
        using var command = profile is null
            ? Command(
                database,
                "DELETE FROM github_postgres_profiles WHERE repository=$repository",
                ("$repository", repository.ToLowerInvariant())
            )
            : Command(
                database,
                "INSERT INTO github_postgres_profiles VALUES($repository,$payload) ON CONFLICT(repository) DO UPDATE SET payload=excluded.payload",
                ("$repository", repository.ToLowerInvariant()),
                ("$payload", JsonSerializer.Serialize(profile))
            );
        command.ExecuteNonQuery();
    }

    public void RegisterPublication(PullRequestWorkflow workflow) =>
        RegisterApproval(workflow.Id, "publish", workflow.PublicationSnapshot);

    private void RegisterApproval(string id, string action, string snapshot)
    {
        using var database = Open();
        using var command = Command(
            database,
            "INSERT INTO github_workflow_approvals VALUES($id,$action,$snapshot,$expires,NULL) "
                + "ON CONFLICT(workflow_id,action) DO UPDATE SET snapshot=excluded.snapshot,expires_at=excluded.expires_at "
                + "WHERE github_workflow_approvals.consumed_at IS NULL AND github_workflow_approvals.snapshot<>excluded.snapshot",
            ("$id", id),
            ("$action", action),
            ("$snapshot", snapshot),
            ("$expires", Clock().ToUniversalTime().AddMinutes(30).ToString("O"))
        );
        command.ExecuteNonQuery();
    }

    // Whether the owner's approval for this workflow action has been claimed (consumed) by the work it authorizes.
    public bool ApprovalClaimed(string workflowId, string action)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT COUNT(*) FROM github_workflow_approvals WHERE workflow_id=$id AND action=$action AND consumed_at IS NOT NULL",
            ("$id", workflowId),
            ("$action", action)
        );
        return (long)command.ExecuteScalar()! == 1;
    }

    public void ClaimApproval(PullRequestWorkflow workflow, string action, string snapshot)
    {
        if (action is not ("repair" or "publish"))
            throw new ReviewAccessException("This action has no execution approval.");
        var current = Workflows(workflow.AccountId).SingleOrDefault(item => item.Id == workflow.Id);
        var expected = action == "repair" ? workflow.ApprovalSnapshot : workflow.PublicationSnapshot;
        if (
            current is null
            || current.ApprovalSnapshot != workflow.ApprovalSnapshot
            || expected != snapshot
            || action == "repair" && current.State is not ("proposed" or "needs-evidence" or "handoff-attention")
            || action == "publish" && current.State != "publication-proposed"
            || action == "publish" && current.PublicationSnapshot != snapshot
        )
            throw new ReviewAccessException("The proposal scope changed. Refresh and approve the current proposal.");
        using var database = Open();
        using var command = Command(
            database,
            "UPDATE github_workflow_approvals SET consumed_at=$now WHERE workflow_id=$id AND action=$action "
                + "AND snapshot=$snapshot AND consumed_at IS NULL AND expires_at>$now "
                + "AND EXISTS(SELECT 1 FROM github_pr_workflows WHERE id=$id AND payload=$current)",
            ("$id", workflow.Id),
            ("$action", action),
            ("$snapshot", snapshot),
            ("$now", Clock().ToUniversalTime().ToString("O")),
            ("$current", JsonSerializer.Serialize(current))
        );
        if (command.ExecuteNonQuery() != 1)
            throw new ReviewAccessException("This approval expired or was already used. Ask for a fresh proposal.");
    }

    public FixLoop[] Loops(string? requestId)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT payload FROM github_fix_loops WHERE ($request IS NULL OR request_id=$request) ORDER BY rowid DESC",
            ("$request", requestId)
        );
        using var reader = command.ExecuteReader();
        var items = new List<FixLoop>();
        while (reader.Read())
            items.Add(JsonSerializer.Deserialize<FixLoop>(reader.GetString(0))!);
        return [.. items];
    }

    public void SaveLoop(FixLoop loop)
    {
        using var database = Open();
        using var command = Command(
            database,
            "INSERT INTO github_fix_loops VALUES($id,$request,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
            ("$id", loop.Id),
            ("$request", loop.RequestId),
            ("$payload", JsonSerializer.Serialize(loop))
        );
        command.ExecuteNonQuery();
    }

    public void SaveWorkflow(PullRequestWorkflow workflow)
    {
        using var database = Open();
        using (
            var before = Command(database, "SELECT payload FROM github_pr_workflows WHERE id=$id", ("$id", workflow.Id))
        )
            RecordCoding(
                before.ExecuteScalar() is string payload
                    ? JsonSerializer.Deserialize<PullRequestWorkflow>(payload)
                    : null,
                workflow
            );
        using var command = Command(
            database,
            "INSERT INTO github_pr_workflows VALUES($id,$request,$account,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
            ("$id", workflow.Id),
            ("$request", workflow.RequestId),
            ("$account", workflow.AccountId),
            ("$payload", JsonSerializer.Serialize(workflow))
        );
        command.ExecuteNonQuery();
        using var stamp = Command(
            database,
            "INSERT INTO github_workflow_stamps VALUES($id,$at) ON CONFLICT(workflow_id) DO UPDATE SET updated_at=$at",
            ("$id", workflow.Id),
            ("$at", Clock().ToUniversalTime().ToString("O"))
        );
        stamp.ExecuteNonQuery();
        if (workflow.State is "proposed" or "needs-evidence" or "handoff-attention")
            RegisterApproval(workflow.Id, "repair", workflow.ApprovalSnapshot);
    }

    // Coding runs start and finish inside the workflow service; the activity log reads both off the saved transition.
    void RecordCoding(PullRequestWorkflow? before, PullRequestWorkflow after)
    {
        var target = after.Repository + " #" + after.Number + (after.Round is { } round ? ", round " + round : "");
        if (after.CodingRunId is { } run && run != before?.CodingRunId)
            _activity.Record(
                after.RequestId,
                "developer",
                ActivityLog.Role,
                "coding.started",
                "Started a coding run on " + target + ".",
                run,
                after.Id
            );
        else if (before is { State: "coding", CodingRunId: { } finished } && after.State != "coding")
            _activity.Record(
                after.RequestId,
                "developer",
                ActivityLog.Role,
                "coding.finished",
                "Coding run on "
                    + target
                    + " ended: "
                    + after.State
                    + (after.Error is { Length: > 0 } why ? ". " + why : "."),
                finished,
                after.Id
            );
    }

    public string WorkflowUpdatedAt(string id)
    {
        using var database = Open();
        using var command = Command(
            database,
            "SELECT updated_at FROM github_workflow_stamps WHERE workflow_id=$id",
            ("$id", id)
        );
        return command.ExecuteScalar() as string ?? "";
    }
}
