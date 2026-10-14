using HireZero.Roles.Developer;

public static class WorkflowApprovalChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "approval-check-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        try
        {
            var store = new GitHubReviewStore(path) { Clock = () => now };
            var scope = new PullRequestWorkflow("approved", "request", 1, "message-1", "owner/example", 10, 42,
                new string('a', 40), "evidence-1", PullRequestWorkflowChecks.Strategy(), "fake-model");
            store.SaveWorkflow(scope);
            var wins = 0;
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try { store.ClaimApproval(scope, "repair", scope.ApprovalSnapshot); Interlocked.Increment(ref wins); }
                catch (ReviewAccessException) { }
            })));
            check(wins == 1, "Concurrent repair approval is single-use.");
            store.SaveWorkflow(scope);
            check(Rejected(() => store.ClaimApproval(scope, "repair", scope.ApprovalSnapshot)), "Saving a proposal never renews consumed authority.");
            var expiring = scope with { Id = "expiry" }; store.SaveWorkflow(expiring); now = now.AddMinutes(31);
            check(Rejected(() => store.ClaimApproval(expiring, "repair", expiring.ApprovalSnapshot)), "Approval expires after thirty minutes.");
            var drifting = scope with { Id = "drift" }; store.SaveWorkflow(drifting);
            store.SaveWorkflow(drifting with { HeadSha = new string('b', 40) });
            check(Rejected(() => store.ClaimApproval(drifting, "repair", drifting.ApprovalSnapshot)), "A changed exact head invalidates stale approval.");
            var publication = scope with { Id = "publish", State = "publication-proposed", ReviewedDiff = "diff-1" };
            store.SaveWorkflow(publication); store.RegisterPublication(publication);
            check(Rejected(() => store.ClaimApproval(publication, "publish", publication.ApprovalSnapshot)), "Repair approval cannot authorize publication.");
            store.ClaimApproval(publication, "publish", publication.PublicationSnapshot);
            check(Rejected(() => store.ClaimApproval(publication, "publish", publication.PublicationSnapshot)), "Publication approval cannot replay.");
            check((scope with { Strategy = scope.Strategy with { Tests = ["a changed check"] } }).ApprovalSnapshot != scope.ApprovalSnapshot,
                "Approved scope includes the exact test plan.");
            check((scope with { ConsentGeneration = 1 }).ApprovalSnapshot != scope.ApprovalSnapshot,
                "A fresh consent generation changes the required workflow approval snapshot.");
            var github = new SyntheticGitHub { CiEvidence = true };
            var evidence = await github.EvidenceAsync("owner/example", 42, true, default);
            check(evidence.Snapshot == (await github.EvidenceAsync("owner/example", 42, false, default)).Snapshot,
                "CI identity is stable across detailed-log and metadata-only retrieval.");
            var originalCheck = evidence.Checks.Single();
            foreach (var changed in new[] { originalCheck with { EventId = "replacement-check" }, originalCheck with { RunId = 101 },
                originalCheck with { RunAttempt = 2 }, originalCheck with { JobUrl = "https://github.com/owner/example/actions/runs/100/job/23" },
                originalCheck with { EvidenceRevision = PullRequestPolicy.Hash("changed failure output") }, originalCheck with { UpdatedAt = "later event" } })
                check((evidence with { Checks = [changed] }).Snapshot != evidence.Snapshot,
                    "Same-name/same-state CI changes invalidate approval when event, attempt, job or evidence revision changes.");
            foreach (var head in new string?[] { null, "", "not-a-sha", new string('b', 40) })
            {
                var incomplete = await new InvalidRunHeadGitHub(head) { CiEvidence = true }.EvidenceAsync("owner/example", 42, false, default);
                check(!incomplete.Complete && !PullRequestPolicy.Merge(incomplete, new(1, "owner")).Ready,
                    "Missing, malformed or mismatched CI run heads cannot supply complete approval evidence.");
            }
        }
        finally { File.Delete(path); }
    }
    static bool Rejected(Action action) { try { action(); return false; } catch (ReviewAccessException) { return true; } }
    sealed class InvalidRunHeadGitHub(string? head) : SyntheticGitHub
    {
        protected override Task<string> CommandAsync(string executable, string? root, string[] args, string? input, CancellationToken token) =>
            args.Contains("repos/owner/example/actions/runs/100")
                ? Task.FromResult(System.Text.Json.JsonSerializer.Serialize(head is null ? (object)new { run_attempt = 1 } : new { run_attempt = 1, head_sha = head }))
                : base.CommandAsync(executable, root, args, input, token);
    }
}
