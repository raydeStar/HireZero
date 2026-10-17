using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// Stored coding runs are masked, and coding runs and the owner's workflow decisions reach the conversation's activity log. Fictional data.
public static class ActivityRecordChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-activity-dev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "activity.sqlite");
            var runs = new CodingRunStore(db);
            var configured = "jwt-" + Guid.NewGuid().ToString("N");
            ContentGuard.RegisterSecrets([KeyValuePair.Create("JWT_SECRET", configured)]);
            var token = "ghp_" + new string('c', 36);
            var stored = runs.Save(
                new CodingRun(
                    "run1",
                    "failed",
                    root,
                    "m",
                    "high",
                    null,
                    "{\"summary\":\"used " + configured + "\",\"tests\":[\"GH_TOKEN=" + token + " npm test\"]}",
                    "git push failed with " + token,
                    [
                        new(
                            "command-completed",
                            "Ran echo " + configured,
                            "{\"command\":\"echo " + configured + "\",\"exitCode\":0}",
                            "2026-10-09T00:00:00Z"
                        ),
                    ],
                    "2026-10-09T00:00:00Z",
                    "2026-10-09T00:00:00Z"
                )
            );
            var reread = runs.Find("run1")!;
            var text = System.Text.Json.JsonSerializer.Serialize(reread);
            check(
                !text.Contains(configured)
                    && !text.Contains(token)
                    && reread.Events[0].DataJson.Contains("\"exitCode\":0")
                    && reread.ResultJson!.Contains("[redacted]")
                    && stored.Error!.Contains("[redacted]"),
                "A stored coding run masks configured secrets and tokens in its events, result and error; the JSON shape is kept."
            );
            var home = Path.Combine(root, "codex-home");
            Directory.CreateDirectory(home);
            var refresh = "rt_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(
                Path.Combine(home, "auth.json"),
                "{\"tokens\":{\"refresh_token\":\"" + refresh + "\",\"account_id\":\"acct-visible-123\"}}"
            );
            CodexRunner.RegisterSignInSecrets(home);
            check(
                ContentGuard.Mask("refresh " + refresh + " for acct-visible-123")
                    == "refresh [redacted] for acct-visible-123",
                "The worker's sign-in tokens are masked; its non-secret fields are not."
            );

            var requests = new RequestStore(db) { OwnerLogin = "owner@example.invalid" };
            var store = new GitHubReviewStore(db);
            var request = requests.CreateRequest(
                "owner@example.invalid",
                "owner/example",
                "Fix #42",
                "Fix it.",
                Guid.NewGuid().ToString()
            );
            var workflow = new PullRequestWorkflow(
                "wf1",
                request.Id,
                1,
                "k",
                "owner/example",
                10,
                42,
                new string('a', 40),
                "snap",
                new FixStrategy("Fix", ["Step"], ["test"], [], [], false, "high", false, false, ""),
                "m",
                "proposed"
            );
            store.SaveWorkflow(workflow);
            store.SaveWorkflow(workflow = workflow with { State = "coding", CodingRunId = "run1", Round = 2 });
            store.SaveWorkflow(workflow with { RunState = "running" });
            store.SaveWorkflow(
                workflow = workflow with { State = "needs-attention", Error = "Tests failed with " + token }
            );
            var record = requests.Activity.ForRequest(request.Id);
            check(
                record.Count(entry => entry.Action == "coding.started") == 1
                    && record.Count(entry => entry.Action == "coding.finished") == 1
                    && record.All(entry =>
                        entry.Action == "request.created"
                        || entry
                            is { Actor: "developer", ActorKind: ActivityLog.Role, RunId: "run1", WorkflowId: "wf1" }
                    ),
                "A workflow's coding run is recorded once when it starts and once when it finishes, by the developer role, with run and workflow ids."
            );
            check(
                record[0].Summary.Contains("owner/example #42, round 2 ended: needs-attention")
                    && !record[0].Summary.Contains(token),
                "The finish names the outcome and masks the error."
            );

            var services = new ServiceCollection().AddSingleton(store).AddSingleton(requests).BuildServiceProvider();
            HttpContext Post(string path) =>
                new DefaultHttpContext { RequestServices = services, Request = { Method = "POST", Path = path } };
            GitHubReviewEndpoints.RecordDecision(Post("/api/github/workflows/wf1/confirm"));
            GitHubReviewEndpoints.RecordDecision(Post("/api/github/workflows/wf1/evidence"));
            GitHubReviewEndpoints.RecordDecision(Post("/api/github/workflows/missing/publish"));
            var decision = requests.Activity.ForRequest(request.Id)[0];
            check(
                decision
                    is {
                        Action: "workflow.confirm",
                        ActorKind: ActivityLog.Owner,
                        Actor: "owner@example.invalid",
                        WorkflowId: "wf1"
                    }
                    && decision.Summary == "Approved the fix strategy for owner/example #42."
                    && requests.Activity.ForRequest(request.Id).Length == record.Length + 1,
                "An owner's workflow approval is recorded on its conversation; unknown actions and workflows are not."
            );
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException) { }
        }
        return Task.CompletedTask;
    }
}
