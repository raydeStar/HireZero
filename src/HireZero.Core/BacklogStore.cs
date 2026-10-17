using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Local metadata, checkpoints and review plans. This store has no provider, connector or worker dependency.
public sealed class BacklogStore(string path)
{
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS backlog_sources(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_inbox(id TEXT PRIMARY KEY,entity TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_latest(entity TEXT PRIMARY KEY,inbox_id TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_unavailable(entity TEXT PRIMARY KEY,reason TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_proposals(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_audit(id INTEGER PRIMARY KEY AUTOINCREMENT,proposal_id TEXT NOT NULL,payload TEXT NOT NULL,at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_runtime(id TEXT PRIMARY KEY,value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS backlog_selection(id TEXT PRIMARY KEY,enabled INTEGER NOT NULL,generation INTEGER NOT NULL,scope_revision TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string, object?)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    public BacklogSourceInfo Selected(BacklogSourceInfo source)
    {
        BacklogSourceInfo selected;
        string? savedScope;
        using (var connection = Open())
        {
            using var command = Command(
                connection,
                "SELECT enabled,generation,scope_revision FROM backlog_selection WHERE id=$id",
                ("$id", source.Id)
            );
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return source with { SelectedScope = null };
            selected =
                reader.GetInt32(0) == 1
                    ? source with
                    {
                        SelectedScope = source.SelectedScope + ":selection:" + reader.GetInt64(1),
                    }
                    : source with
                    {
                        SelectedScope = null,
                    };
            savedScope = reader.GetString(2);
        }
        if (selected.ScopeRevision == savedScope)
            return selected;
        Select(source, false, DateTimeOffset.UtcNow);
        return source with
        {
            SelectedScope = null,
            MissingReason = "This local source scope changed. Select it again before collecting metadata.",
        };
    }

    public void Select(BacklogSourceInfo source, bool enabled, DateTimeOffset now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var read = Command(
            connection,
            "SELECT generation FROM backlog_selection WHERE id=$id",
            ("$id", source.Id)
        );
        read.Transaction = transaction;
        var generation = (read.ExecuteScalar() is long previous ? previous : 0) + 1;
        var selected = source with
        {
            SelectedScope = enabled ? source.SelectedScope + ":selection:" + generation : null,
        };
        using var save = Command(
            connection,
            "INSERT INTO backlog_selection VALUES($id,$enabled,$generation,$scope) ON CONFLICT(id) DO UPDATE SET enabled=$enabled,generation=$generation,scope_revision=$scope",
            ("$id", source.Id),
            ("$enabled", enabled ? 1 : 0),
            ("$generation", generation),
            ("$scope", selected.ScopeRevision)
        );
        save.Transaction = transaction;
        save.ExecuteNonQuery();
        using var reset = Command(
            connection,
            "INSERT INTO backlog_sources VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload",
            ("$id", source.Id),
            ("$payload", Json(new BacklogCheckpoint(source.Id, selected.ScopeRevision)))
        );
        reset.Transaction = transaction;
        reset.ExecuteNonQuery();
        foreach (var proposal in Proposals(connection).Where(item => item.SourceId == source.Id))
            SaveProposal(
                connection,
                transaction,
                proposal with
                {
                    Status = "scope-changed",
                    Plan = null,
                    ApprovedBy = null,
                    ApprovedAt = null,
                    ExpiresAt = null,
                },
                now
            );
        transaction.Commit();
    }

    private static bool Selected(SqliteConnection connection, string id, string revision)
    {
        using var command = Command(
            connection,
            "SELECT 1 FROM backlog_selection WHERE id=$id AND enabled=1 AND scope_revision=$revision",
            ("$id", id),
            ("$revision", revision)
        );
        return command.ExecuteScalar() is not null;
    }

    private static T? Find<T>(SqliteConnection connection, string table, string id)
    {
        using var command = Command(connection, "SELECT payload FROM " + table + " WHERE id=$id", ("$id", id));
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json) : default;
    }

    public BacklogCheckpoint Checkpoint(BacklogSourceInfo source)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var selected = Command(
            connection,
            "SELECT scope_revision FROM backlog_selection WHERE id=$id",
            ("$id", source.Id)
        );
        if (selected.ExecuteScalar() is string scope && scope != source.ScopeRevision)
            throw new WorkspaceException("The selected source changed. Refresh its scope.", 409);
        var existing = Find<BacklogCheckpoint>(connection, "backlog_sources", source.Id);
        if (existing is not null && existing.ScopeRevision == source.ScopeRevision)
            return existing;
        var fresh = new BacklogCheckpoint(source.Id, source.ScopeRevision);
        using var command = Command(
            connection,
            "INSERT INTO backlog_sources VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload",
            ("$id", source.Id),
            ("$payload", Json(fresh))
        );
        command.Transaction = transaction;
        command.ExecuteNonQuery();
        if (existing is not null)
        {
            foreach (
                var proposal in Proposals(connection)
                    .Where(item => item.SourceId == source.Id && item.ScopeRevision != source.ScopeRevision)
            )
                SaveProposal(
                    connection,
                    transaction,
                    proposal with
                    {
                        Status = "scope-changed",
                        Plan = null,
                        ApprovedBy = null,
                        ApprovedAt = null,
                        ExpiresAt = null,
                    },
                    DateTimeOffset.UtcNow
                );
        }
        transaction.Commit();
        return fresh;
    }

    public bool Replace(BacklogCheckpoint before, BacklogCheckpoint after)
    {
        if (before.SourceId != after.SourceId || before.ScopeRevision != after.ScopeRevision)
            throw new WorkspaceException("A checkpoint cannot change its selected scope.");
        using var connection = Open();
        using var command = Command(
            connection,
            "UPDATE backlog_sources SET payload=$after WHERE id=$id AND payload=$before",
            ("$after", Json(after)),
            ("$id", before.SourceId),
            ("$before", Json(before))
        );
        return command.ExecuteNonQuery() == 1;
    }

    public bool CommitPage(BacklogCheckpoint lease, BacklogPage page, DateTimeOffset now, string? membershipSql = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (!Selected(connection, lease.SourceId, lease.ScopeRevision))
        {
            transaction.Rollback();
            return false;
        }
        var next = lease with
        {
            State = page.Complete ? "caught-up" : "catching-up",
            Cursor = page.NextCursor,
            Through = page.Complete ? null : lease.Through,
            Since = page.Complete ? null : lease.Since,
            LastPage = now,
            LastComplete = page.Complete ? lease.Through : lease.LastComplete,
            LeaseUntil = null,
            Failures = 0,
            Received = lease.Received + page.Items.Length,
            Note = null,
            AuthoritativeSnapshot = page.Complete && membershipSql is not null,
            Gaps =
            [
                .. (lease.Gaps ?? [])
                    .Concat(page.Gaps.Select(gap => ContentGuard.Clean(gap, 500).Text))
                    .Distinct()
                    .Take(8),
            ],
        };
        using var update = Command(
            connection,
            "UPDATE backlog_sources SET payload=$next WHERE id=$id AND payload=$lease",
            ("$next", Json(next)),
            ("$id", lease.SourceId),
            ("$lease", Json(lease))
        );
        update.Transaction = transaction;
        if (update.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            return false;
        }
        foreach (var original in page.Items)
        {
            var item = original with
            {
                Title = ContentGuard.Clean(original.Title, 240).Text,
                Status = ContentGuard.Clean(original.Status, 80).Text,
                References = [.. original.References.Select(value => ContentGuard.Clean(value, 500).Text)],
            };
            var entity = BacklogPolicy.Hash(
                Json(
                    new
                    {
                        lease.SourceId,
                        lease.ScopeRevision,
                        item.ItemId,
                    }
                )
            );
            var id = BacklogPolicy.Hash(entity + ":" + item.Revision);
            var inbox = new BacklogInboxItem(id, lease.SourceId, lease.ScopeRevision, item, now);
            using var insert = Command(
                connection,
                "INSERT OR IGNORE INTO backlog_inbox VALUES($id,$entity,$payload)",
                ("$id", id),
                ("$entity", entity),
                ("$payload", Json(inbox))
            );
            insert.Transaction = transaction;
            insert.ExecuteNonQuery();
            using var previous = Command(
                connection,
                "SELECT i.payload FROM backlog_latest l JOIN backlog_inbox i ON i.id=l.inbox_id WHERE l.entity=$entity",
                ("$entity", entity)
            );
            previous.Transaction = transaction;
            var older = previous.ExecuteScalar() is string saved
                ? JsonSerializer.Deserialize<BacklogInboxItem>(saved)
                : null;
            if (older is not null && (older.Id == id || older.Event.SourceUpdatedAt > item.SourceUpdatedAt))
                continue;
            using var latest = Command(
                connection,
                "INSERT INTO backlog_latest VALUES($entity,$id) ON CONFLICT(entity) DO UPDATE SET inbox_id=$id",
                ("$entity", entity),
                ("$id", id)
            );
            latest.Transaction = transaction;
            latest.ExecuteNonQuery();
            using var available = Command(
                connection,
                "DELETE FROM backlog_unavailable WHERE entity=$entity",
                ("$entity", entity)
            );
            available.Transaction = transaction;
            available.ExecuteNonQuery();
            var proposalId = BacklogPolicy.Hash("proposal:" + entity);
            var existing = Find<BacklogProposal>(connection, "backlog_proposals", proposalId);
            if (item.NeedsReview || existing is not null)
                SaveProposal(
                    connection,
                    transaction,
                    new(
                        proposalId,
                        id,
                        lease.SourceId,
                        lease.ScopeRevision,
                        item.ItemId,
                        item.Revision,
                        item.Title,
                        item.NeedsReview ? "proposed" : "resolved-source",
                        now
                    ),
                    now
                );
        }
        if (page.Complete && membershipSql is not null)
            ReconcileMissing(connection, transaction, lease, membershipSql, now);
        transaction.Commit();
        return true;
    }

    private static void ReconcileMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BacklogCheckpoint lease,
        string membershipSql,
        DateTimeOffset now
    )
    {
        // This is a current local membership query inside the same SQLite transaction, never an inference
        // from rows not seen in a time-filtered page. Partial/failing pages do not invoke reconciliation.
        using var reconcile = Command(
            connection,
            $"""
            CREATE TEMP TABLE backlog_missing(entity TEXT PRIMARY KEY,inbox_id TEXT NOT NULL);
            INSERT INTO backlog_missing SELECT l.entity,l.inbox_id FROM backlog_latest l JOIN backlog_inbox i ON i.id=l.inbox_id
                WHERE json_extract(i.payload,'$.SourceId')=$source AND json_extract(i.payload,'$.ScopeRevision')=$scope
                AND NOT EXISTS(SELECT 1 FROM ({membershipSql}) membership WHERE membership.item_id=json_extract(i.payload,'$.Event.ItemId'));
            INSERT OR REPLACE INTO backlog_unavailable SELECT entity,'The source record is unavailable after a completed local membership check.' FROM backlog_missing;
            INSERT INTO backlog_audit(proposal_id,payload,at)
                SELECT p.id,json_set(p.payload,'$.Status','source-unavailable','$.Plan',NULL,'$.ApprovedBy',NULL,'$.ApprovedAt',NULL,'$.ExpiresAt',NULL,'$.UpdatedAt',$now),$now
                FROM backlog_proposals p WHERE EXISTS(SELECT 1 FROM backlog_missing m WHERE m.inbox_id=json_extract(p.payload,'$.InboxId'));
            UPDATE backlog_proposals SET payload=json_set(payload,'$.Status','source-unavailable','$.Plan',NULL,'$.ApprovedBy',NULL,'$.ApprovedAt',NULL,'$.ExpiresAt',NULL,'$.UpdatedAt',$now)
                WHERE EXISTS(SELECT 1 FROM backlog_missing m WHERE m.inbox_id=json_extract(backlog_proposals.payload,'$.InboxId'));
            DELETE FROM backlog_latest WHERE entity IN (SELECT entity FROM backlog_missing);
            DROP TABLE backlog_missing;
            """,
            ("$source", lease.SourceId),
            ("$scope", lease.ScopeRevision),
            ("$now", now.ToString("O"))
        );
        reconcile.Transaction = transaction;
        reconcile.ExecuteNonQuery();
    }

    private static void SaveProposal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BacklogProposal proposal,
        DateTimeOffset now
    )
    {
        using var save = Command(
            connection,
            "INSERT INTO backlog_proposals VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload",
            ("$id", proposal.Id),
            ("$payload", Json(proposal))
        );
        save.Transaction = transaction;
        save.ExecuteNonQuery();
        using var audit = Command(
            connection,
            "INSERT INTO backlog_audit(proposal_id,payload,at) VALUES($id,$payload,$at)",
            ("$id", proposal.Id),
            ("$payload", Json(proposal)),
            ("$at", now.ToString("O"))
        );
        audit.Transaction = transaction;
        audit.ExecuteNonQuery();
    }

    private static BacklogProposal[] Proposals(SqliteConnection connection, bool selectedOnly = false)
    {
        using var command = Command(
            connection,
            "SELECT p.payload FROM backlog_proposals p "
                + (
                    selectedOnly
                        ? "WHERE EXISTS(SELECT 1 FROM backlog_selection s WHERE s.id=json_extract(p.payload,'$.SourceId') AND s.enabled=1 AND s.scope_revision=json_extract(p.payload,'$.ScopeRevision')) "
                        : ""
                )
                + "ORDER BY p.rowid DESC LIMIT 200"
        );
        using var reader = command.ExecuteReader();
        var result = new List<BacklogProposal>();
        while (reader.Read())
            result.Add(JsonSerializer.Deserialize<BacklogProposal>(reader.GetString(0))!);
        return [.. result];
    }

    public BacklogProposal[] Proposals()
    {
        using var connection = Open();
        return Proposals(connection, true);
    }

    public BacklogInboxItem[] Inbox()
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT i.payload,l.inbox_id,u.reason FROM backlog_inbox i LEFT JOIN backlog_latest l ON l.entity=i.entity LEFT JOIN backlog_unavailable u ON u.entity=i.entity "
                + "JOIN backlog_selection s ON s.id=json_extract(i.payload,'$.SourceId') AND s.enabled=1 AND s.scope_revision=json_extract(i.payload,'$.ScopeRevision') ORDER BY i.rowid DESC LIMIT 200"
        );
        using var reader = command.ExecuteReader();
        var result = new List<BacklogInboxItem>();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<BacklogInboxItem>(reader.GetString(0))!;
            result.Add(
                item with
                {
                    Current = !reader.IsDBNull(1) && reader.GetString(1) == item.Id,
                    UnavailableReason = reader.IsDBNull(2) ? null : reader.GetString(2),
                }
            );
        }
        return [.. result];
    }

    public BacklogProposal Plan(string id, BacklogPlanInput input, DateTimeOffset now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item =
            Find<BacklogProposal>(connection, "backlog_proposals", id)
            ?? throw new WorkspaceException("That proposal no longer exists.", 404);
        if (!Selected(connection, item.SourceId, item.ScopeRevision))
            throw new WorkspaceException("The source is no longer selected.");
        if (item.Status is not ("proposed" or "planned") || item.Snapshot != input.Snapshot)
            throw new WorkspaceException("The proposal changed. Review its current evidence before planning.");
        var planned = item with
        {
            Status = "planned",
            Plan = BacklogPolicy.Plan(input.Action, input.Provider),
            UpdatedAt = now,
            ApprovedBy = null,
            ApprovedAt = null,
            ExpiresAt = now.AddMinutes(30),
        };
        SaveProposal(connection, transaction, planned, now);
        transaction.Commit();
        return planned;
    }

    public BacklogProposal Approve(string id, string snapshot, string owner, DateTimeOffset now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item =
            Find<BacklogProposal>(connection, "backlog_proposals", id)
            ?? throw new WorkspaceException("That proposal no longer exists.", 404);
        if (!Selected(connection, item.SourceId, item.ScopeRevision))
            throw new WorkspaceException("The source is no longer selected.");
        if (item.Status != "planned" || item.Snapshot != snapshot || item.ExpiresAt is not { } until || until <= now)
            throw new WorkspaceException("Review a fresh, current plan before approval.");
        if (item.Plan is not { LocalReview: true })
            throw new WorkspaceException(
                "Provider transmission and execution are blocked. No approved backlog adapter is configured."
            );
        var checkpoint = Find<BacklogCheckpoint>(connection, "backlog_sources", item.SourceId);
        if (
            checkpoint?.ScopeRevision != item.ScopeRevision
            || checkpoint.State != "caught-up"
            || !checkpoint.AuthoritativeSnapshot
            || checkpoint.Gaps is { Length: > 0 }
            || checkpoint.LastComplete is not { } complete
            || now - complete > TimeSpan.FromMinutes(5)
        )
            throw new WorkspaceException("Catch up this source before approving a stale or partial plan.");
        var approved = item with
        {
            Status = "approved-local-review",
            ApprovedBy = owner,
            ApprovedAt = now,
            UpdatedAt = now,
        };
        SaveProposal(connection, transaction, approved, now);
        transaction.Commit();
        return approved;
    }

    public void Startup(DateTimeOffset now)
    {
        using var connection = Open();
        using var previous = Command(
            connection,
            "INSERT INTO backlog_runtime SELECT 'previous-seen',value FROM backlog_runtime WHERE id='seen' ON CONFLICT(id) DO UPDATE SET value=excluded.value"
        );
        previous.ExecuteNonQuery();
        Touch(now);
    }

    public void Touch(DateTimeOffset now)
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "INSERT INTO backlog_runtime VALUES('seen',$now) ON CONFLICT(id) DO UPDATE SET value=$now",
            ("$now", now.ToString("O"))
        );
        command.ExecuteNonQuery();
    }

    public DateTimeOffset? PreviousSeen()
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT value FROM backlog_runtime WHERE id='previous-seen'");
        return command.ExecuteScalar() is string value && DateTimeOffset.TryParse(value, out var time) ? time : null;
    }
}
