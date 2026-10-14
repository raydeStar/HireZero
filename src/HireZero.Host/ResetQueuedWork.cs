using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Roles.Developer;
using Microsoft.Data.Sqlite;

public sealed record QueueResetReceipt(string Backup, int ArchivedRequests, int CanceledAgentTasks, int CanceledWorkflows,
    int StoppedLoops, int CanceledCodingRuns, int CanceledLocalFixes, int ExpiredApprovals, int ConsentHeldRequests);

public static class ResetQueuedWork
{
    public static QueueResetReceipt Run(string dataRoot)
    {
        var path = Path.Combine(dataRoot, "workspace.sqlite");
        var backup = Path.Combine(dataRoot, "Backups", "fresh-start-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".sqlite");
        using (var original = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            original.Open();
            using var active = original.CreateCommand();
            active.CommandText = "SELECT (SELECT count(*) FROM agent_tasks WHERE state='active')+(SELECT count(*) FROM coding_runs WHERE state='running')";
            if (Convert.ToInt32(active.ExecuteScalar()) != 0) throw new InvalidOperationException("Inspect live run receipts before resetting active work.");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString());
            destination.Open(); original.BackupDatabase(destination);
        }
        var requests = new RequestStore(path);
        var agents = new AgentStore(path);
        var reviews = new GitHubReviewStore(path);
        var coding = new CodingRunStore(path);
        var archived = requests.Requests(null).Where(item => item.Status == "queued").Count(item => requests.ArchiveRequest(item.Id));
        var canceledTasks = agents.OpenTasks().Count(item => agents.Cancel(item.Id));
        var shift = agents.Shift(); agents.SaveShift(shift with { On = false });
        agents.SaveSchedule(agents.Schedule() with { Enabled = false });
        var canceledWorkflows = 0;
        foreach (var workflow in reviews.Workflows().Where(item => item.State is "proposed" or "needs-evidence" or "handoff-attention" or
            "preparing" or "scoping" or "coding" or "validating" or "needs-input" or "needs-attention" or "validation-attention" or
            "business-rule-attention" or "publication-proposed"))
        {
            reviews.SaveWorkflow(workflow with { State = "canceled", Error = "Canceled for the owner's fresh start; historical evidence is retained." });
            canceledWorkflows++;
        }
        var stoppedLoops = 0;
        foreach (var loop in reviews.Loops(null).Where(item => item.State is not ("healthy" or "stopped")))
        { reviews.SaveLoop(loop with { State = "stopped", Note = "Stopped for the owner's fresh start.", UpdatedAt = DateTimeOffset.UtcNow.ToString("O") }); stoppedLoops++; }
        var canceledCoding = 0;
        foreach (var run in coding.Unfinished())
        { coding.Save(run with { State = "canceled", Error = "Canceled for the owner's fresh start.", UpdatedAt = DateTimeOffset.UtcNow.ToString("O") }); canceledCoding++; }
        var canceledFixes = 0;
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); database.Open();
        using (var accounts = database.CreateCommand())
        {
            accounts.CommandText = "SELECT DISTINCT account_id FROM github_review_repositories";
            var ids = new List<long>(); using (var reader = accounts.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetInt64(0));
            foreach (var repository in ids.SelectMany(reviews.Repositories))
            {
                reviews.SetAutomatic(repository.Id, false);
                foreach (var fix in reviews.Fixes(repository.Id).Where(item => item.Status is "scout-queued" or "builder-queued" or "proposed"))
                { reviews.Save(fix with { Status = "canceled" }); canceledFixes++; }
            }
        }
        using var transaction = database.BeginTransaction();
        using var invalidate = database.CreateCommand(); invalidate.Transaction = transaction;
        invalidate.CommandText = "UPDATE request_agent_epochs SET generation=MAX(generation,0)+1; INSERT OR IGNORE INTO request_agent_epochs SELECT id,1 FROM requests; INSERT OR IGNORE INTO request_agent_upgrade_review SELECT id FROM requests";
        invalidate.ExecuteNonQuery();
        invalidate.CommandText = "SELECT count(*) FROM request_agent_upgrade_review";
        var consentHeld = Convert.ToInt32(invalidate.ExecuteScalar());
        invalidate.CommandText = "UPDATE github_workflow_approvals SET expires_at=$now WHERE consumed_at IS NULL";
        invalidate.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        var approvals = invalidate.ExecuteNonQuery(); transaction.Commit();
        requests.LogOwner(null, "queue.reset", "Archived queued requests, canceled local pending work, stopped shifts and invalidated historical consent. The pre-reset database is retained.");
        return new(backup, archived, canceledTasks, canceledWorkflows, stoppedLoops, canceledCoding, canceledFixes, approvals, consentHeld);
    }
}
