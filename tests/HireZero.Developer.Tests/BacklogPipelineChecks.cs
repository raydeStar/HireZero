using HireZero.Core;
using HireZero.Roles.Developer;
using Microsoft.Data.Sqlite;

public static class BacklogPipelineChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "local-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "test.sqlite");
            var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
            var store = new GitHubReviewStore(db) { Clock = () => now.AddMinutes(-1) };
            for (var index = 0; index < 3; index++)
                store.SaveWorkflow(
                    new PullRequestWorkflow(
                        "workflow" + index,
                        "request" + index,
                        1,
                        "opening",
                        "owner/example",
                        10,
                        42 + index,
                        new string('a', 40),
                        "evidence",
                        PullRequestWorkflowChecks.Strategy(),
                        "fake"
                    )
                    {
                        State = index == 2 ? "merged" : "proposed",
                        ConsentGeneration = 1,
                    }
                );
            var source = new LocalPipelineBacklog(db);
            var first = await source.ReadAsync(new(null, null, now, 2), default);
            check(
                first.Items.Length == 2
                    && !first.Complete
                    && first.NextCursor == "workflow1"
                    && first.Items.All(item => item.NeedsReview),
                "Local workflow metadata is paginated and proposed work becomes review intent without a GitHub client or model."
            );
            var second = await source.ReadAsync(new(first.NextCursor, null, now, 2), default);
            check(
                second.Complete && second.Items.Single().Status == "merged" && !second.Items.Single().NeedsReview,
                "Completed source observations resolve rather than create repair proposals."
            );
            using var connection = new SqliteConnection("Data Source=" + db + ";Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM github_workflow_stamps WHERE workflow_id='workflow0'";
            command.ExecuteNonQuery();
            var incomplete = await source.ReadAsync(new(null, null, now, 2), default);
            check(
                incomplete.Gaps.Length == 1 && incomplete.Items[0].SourceUpdatedAt == DateTimeOffset.MinValue,
                "Missing legacy observation time remains an explicit freshness gap."
            );
            // Restore the stamped fixture so the attention-state regression is independent of legacy gaps.
            store.SaveWorkflow(store.Workflows().Single(item => item.Id == "workflow0"));
            var backlog = new BacklogStore(db);
            var coordinator = new BacklogCoordinator(backlog, [source]) { Clock = () => now };
            coordinator.Select(source.Info.Id, true);
            var attention = store.Workflows().Single(item => item.Id == "workflow0") with
            {
                Id = "attention",
                State = "proposed",
            };
            store.SaveWorkflow(attention);
            var initial = await coordinator.CatchUpAsync(default);
            var original = initial.Proposals.Single(item => item.ItemId == attention.Id);
            var planned = coordinator.Plan(original.Id, new(original.Snapshot, "manual-review", "none"));
            foreach (
                var state in new[]
                {
                    "needs-attention",
                    "validation-attention",
                    "business-rule-attention",
                    "needs-input",
                    "needs-evidence",
                    "handoff-attention",
                    "publication-proposed",
                    "published",
                    "merge-attention",
                    "consent-revoked",
                    "scope-changed",
                    "read-only",
                    "unknown-future-state",
                }
            )
            {
                attention = attention with { State = state };
                store.SaveWorkflow(attention);
                var observed = await coordinator.CatchUpAsync(default);
                var proposal = observed.Proposals.Single(item => item.ItemId == attention.Id);
                check(
                    proposal is { Status: "proposed", Plan: null, ApprovedBy: null }
                        && observed
                            .Inbox.Single(item => item.Event.ItemId == attention.Id && item.Current)
                            .Event.NeedsReview,
                    "Persisted "
                        + state
                        + " remains actionable owner attention, never resolved-source or an inherited approval."
                );
            }
            check(
                PullRequestWorkflowStates.Parse("needs-attention") == PullRequestWorkflowState.NeedsAttention
                    && PullRequestWorkflowStates.Parse("validation-attention")
                        == PullRequestWorkflowState.ValidationAttention,
                "The real paused workflow wire states parse into the typed state contract."
            );
            attention = attention with { State = "merged" };
            store.SaveWorkflow(attention);
            check(
                (await coordinator.CatchUpAsync(default)).Proposals.Single(item => item.ItemId == attention.Id).Status
                    == "resolved-source",
                "A real terminal state still resolves the observation."
            );
            command.CommandText = "DELETE FROM github_pr_workflows WHERE id='attention'";
            command.ExecuteNonQuery();
            var missing = await coordinator.CatchUpAsync(default);
            check(
                missing.Proposals.Single(item => item.ItemId == attention.Id).Status == "source-unavailable"
                    && missing
                        .Inbox.Where(item => item.Event.ItemId == attention.Id)
                        .All(item => !item.Current && item.UnavailableReason is not null),
                "Deleted workflow source rows invalidate current authority while retaining every historical revision."
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
