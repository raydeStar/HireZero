using HireZero.Core;
using Microsoft.Data.Sqlite;

public static class ReplyRecoveryChecks
{
    sealed class Model : IModelGateway
    {
        public string DefaultModel => "fake"; public string Reasoning => "none";
        public void Prepare(string cell) { } public void Stop(string cell) { }
        public Task<string> CompleteAsync(string cell, string instructions, string prompt, CancellationToken token, string? model = null, int maxTokens = 900) => throw new InvalidOperationException("Recovery must not invoke a model.");
    }
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "reply-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var legacyDb = Path.Combine(root, "legacy.sqlite"); var legacy = new RequestStore(legacyDb);
            legacy.SetPerson("legacy@example.invalid", "visitor", false);
            var legacyRequest = legacy.CreateRequest("legacy@example.invalid", "Example", "Legacy", "Question", "legacy"); legacy.AllowAgent(legacyRequest.Id);
            using (var legacyConnection = new SqliteConnection("Data Source=" + legacyDb + ";Pooling=False"))
            {
                legacyConnection.Open(); using var legacyCommand = legacyConnection.CreateCommand();
                legacyCommand.CommandText = "ALTER TABLE agent_permissions DROP COLUMN consent_generation; DROP TABLE request_agent_epochs; DROP TABLE request_authority_versions; DROP TABLE request_agent_upgrade_review"; legacyCommand.ExecuteNonQuery();
            }
            legacy = new RequestStore(legacyDb);
            check(!legacy.AgentAllowed(legacyRequest.Id) && legacy.AgentConsentGeneration(legacyRequest.Id) == 1 && legacy.AgentUpgradeReviewRequired(legacyRequest.Id),
                "Existing two-column permission rows retain history but require fresh confirmation after migration.");
            legacy.SetPerson(legacyRequest.Requester, "visitor", true); legacy.SetPerson(legacyRequest.Requester, "visitor", false);
            legacy = new RequestStore(legacyDb);
            check(!legacy.AgentAllowed(legacyRequest.Id) && legacy.AgentConsentGeneration(legacyRequest.Id) == 2,
                "Migrated grants cannot survive a persisted revoke-and-restore cycle.");
            legacy.AllowAgent(legacyRequest.Id);
            check(legacy.AgentAllowed(legacyRequest.Id), "A new explicit grant binds to the migrated request's current generation.");
            var precedingGeneration = legacy.AgentConsentGeneration(legacyRequest.Id);
            using (var precedingConnection = new SqliteConnection("Data Source=" + legacyDb + ";Pooling=False"))
            {
                precedingConnection.Open(); using var precedingCommand = precedingConnection.CreateCommand();
                precedingCommand.CommandText = "DROP TABLE request_authority_versions"; precedingCommand.ExecuteNonQuery();
            }
            legacy = new RequestStore(legacyDb);
            check(!legacy.AgentAllowed(legacyRequest.Id) && legacy.AgentUpgradeReviewRequired(legacyRequest.Id) && legacy.AgentConsentGeneration(legacyRequest.Id) == precedingGeneration + 1,
                "The preceding generation-bearing implementation also requires fresh authority at the upgrade boundary.");
            check(legacy.AgentConsentGeneration("missing-request") < 0, "Unknown request generations cannot become valid zero-generation authority.");
            var db = Path.Combine(root, "test.sqlite"); var owner = new WorkspaceOwner(WorkspaceOwner.LocalIdentity);
            var requests = new RequestStore(db); var agents = new AgentStore(db); var model = new Model();
            var triage = new RequestTriageService(requests, new TriageStore(db), model, owner, agents);
            requests.SetPerson("visitor@example.invalid", "visitor", false);
            using var connection = new SqliteConnection("Data Source=" + db + ";Pooling=False"); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fake_queue_failure BEFORE INSERT ON agent_tasks BEGIN SELECT RAISE(ABORT,'synthetic queue failure'); END";
            command.ExecuteNonQuery();
            var accepted = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "accept");
            try { triage.Decide(accepted.Id, true, null); } catch (SqliteException) { }
            var allowed = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "allow");
            try { RequestAgentWork.Allow(requests, agents, null, allowed); } catch (SqliteException) { }
            check(triage.Find(accepted.Id)?.Decision == "accepted" && requests.AgentAllowed(accepted.Id) && requests.AgentAllowed(allowed.Id) && agents.OpenTasks().Length == 0,
                "Injected queue failures preserve durable acceptance/allow grants without pretending work was enqueued.");
            command.CommandText = "DROP TRIGGER fake_queue_failure"; command.ExecuteNonQuery();
            requests = new RequestStore(db); agents = new AgentStore(db);
            var recovery = new RequestReplyRecovery(requests, agents, owner);
            check(recovery.Reconcile() == 2 && agents.OpenTasks().Length == 2, "Restart reconciliation repairs both missing accepted and allowed replies.");
            check(recovery.Reconcile() == 0 && agents.OpenTasks().Length == 2, "Recovery is idempotent and cannot duplicate a reply.");
            agents.Cancel(agents.OpenTasks()[0].Id);
            var failed = agents.OpenTasks().Single(); agents.Transition(failed.Id, "queued", "failed");
            check(recovery.Reconcile() == 0 && agents.OpenTasks().Length == 0, "Recovery never retries canceled or failed model work automatically.");
            var archived = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "archived"); requests.AllowAgent(archived.Id);
            check(requests.ArchiveRequest(archived.Id), "The fixture archives an accepted request before missing-row recovery.");
            recovery = new RequestReplyRecovery(new RequestStore(db), new AgentStore(db), owner);
            check(recovery.Reconcile() == 0 && agents.ForRequest(archived.Id, 10).Length == 0 && !requests.CanUseAgent(archived, owner),
                "Archived requests cannot recover model work, including through a stale WorkRequest object.");
            // Cross-store archive between a caller's metadata read and the SQLite conditional insert must win.
            var racing = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "archive-race"); requests.AllowAgent(racing.Id);
            command.CommandText = "SELECT granted_at FROM agent_permissions WHERE request_id=$id"; command.Parameters.AddWithValue("$id", racing.Id);
            var queueKey = "allowed:" + (string)command.ExecuteScalar()! + ":request";
            var epoch = requests.AgentConsentGeneration(racing.Id); new RequestStore(db).ArchiveRequest(racing.Id);
            check(agents.RecoverMissingReply(racing.Id, racing.DisplayTitle, queueKey, "request", owner.Login, epoch) is null,
                "The queue insert itself rejects archive and generation drift after the caller's check.");
            var messageRace = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "message-race"); requests.AllowAgent(messageRace.Id);
            command.Parameters["$id"].Value = messageRace.Id;
            var staleKey = "allowed:" + (string)command.ExecuteScalar()! + ":request";
            requests.AddMessage(messageRace.Id, messageRace.Requester, "Newer requirements", "newer");
            check(agents.RecoverMissingReply(messageRace.Id, messageRace.DisplayTitle, staleKey, "request", owner.Login, requests.AgentConsentGeneration(messageRace.Id)) is null,
                "Recovery's conditional insert rejects a stale message binding.");
            var inserted = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "insert-before-archive"); requests.AllowAgent(inserted.Id);
            command.Parameters["$id"].Value = inserted.Id;
            var insertedKey = "allowed:" + (string)command.ExecuteScalar()! + ":request";
            var insertedTask = agents.RecoverMissingReply(inserted.Id, inserted.DisplayTitle, insertedKey, "request", owner.Login, requests.AgentConsentGeneration(inserted.Id));
            check(insertedTask is not null && new RequestStore(db).ArchiveRequest(inserted.Id) && agents.Find(insertedTask.Id)?.State == "canceled",
                "If queue recovery inserts first, archive atomically cancels that recovered task.");
            command.Parameters.Clear();
            var declined = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "decline"); requests.AllowAgent(declined.Id);
            new RequestTriageService(requests, new TriageStore(db), model).Decide(declined.Id, false, null);
            var revoked = requests.CreateRequest("visitor@example.invalid", "Example", "Ask", "Question", "revoked"); requests.AllowAgent(revoked.Id);
            requests.SetPerson("visitor@example.invalid", "visitor", true);
            check(recovery.Reconcile() == 0 && agents.OpenTasks().Length == 0, "Durable recovery cannot revive declined work or revoked requesters.");
        }
        finally { Directory.Delete(root, true); }
    }
}
