using System.Diagnostics;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Roles.Developer;

public static class ProviderLifecycleChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "provider-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prior = Environment.GetEnvironmentVariable("HIREZERO_CODEX_HOME");
        Environment.SetEnvironmentVariable("HIREZERO_CODEX_HOME", Path.Combine(root, "empty-profile"));
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var fake = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../HireZero.FakeCodex/bin",
                output.Parent!.Name,
                output.Name,
                "HireZero.FakeCodex" + (OperatingSystem.IsWindows() ? ".exe" : "")
            )
        );
        try
        {
            var store = new CodingRunStore(Path.Combine(root, "fixture.sqlite"));
            async Task<CodingRun> Terminal(CodexRunner runner, CodingRun run)
            {
                for (int i = 0; i < 200; i++)
                {
                    var current = runner.Find(run.Id)!;
                    if (current.State is "completed" or "failed" or "canceled")
                        return current;
                    await Task.Delay(25);
                }
                throw new Exception("Synthetic worker did not reach a terminal state");
            }
            CodingRunRequest Request(string mode) =>
                new(
                    root,
                    "Fictional test",
                    null,
                    null,
                    "low",
                    false,
                    AllowWrite: false,
                    Environment: new Dictionary<string, string> { { "HIREZERO_FIXTURE_MODE", mode } }
                );
            using (var missing = new CodexRunner(store, Path.Combine(root, "missing-cli")))
            {
                var result = await Terminal(missing, missing.Start(Request("success")));
                check(
                    result.State == "failed" && result.Error is { Length: > 0 },
                    "A launch failure is persisted as terminal failure instead of a dead queued run."
                );
            }
            using (var runner = new CodexRunner(store, fake))
            {
                for (int i = 0; i < 5; i++)
                {
                    var result = await Terminal(runner, runner.Start(Request("success")));
                    check(
                        result.State == "completed" && result.ResultJson!.Contains("Fictional answer"),
                        "A fast-exiting fixture's buffered response and completion are drained before exit is classified."
                    );
                }
                var named = await Terminal(
                    runner,
                    runner.Start(Request("model-name") with { Model = "openai/gpt-6.1-sol" })
                );
                check(
                    named.State == "completed"
                        && named.RequestedModel == "openai/gpt-6.1-sol"
                        && named.ResolvedModel == "gpt-6.1-sol",
                    "Provider-qualified requests retain their audit identity while both thread and turn use Codex's advertised native model ID."
                );
                foreach (var mode in new[] { "early-exit", "bad-status", "failed", "missing-id", "missing-thread" })
                {
                    var result = await Terminal(runner, runner.Start(Request(mode)));
                    check(
                        result.State == "failed" && result.Error is { Length: > 0 },
                        "Missing or invalid terminal evidence cannot be reported as successful coding."
                    );
                }
                foreach (var mode in new[] { "stale-turn", "stale-events", "unscoped-events" })
                {
                    var stale = await Terminal(runner, runner.Start(Request(mode)));
                    check(
                        stale.State == "completed"
                            && stale.ResultJson!.Contains("Fictional answer")
                            && !stale.ResultJson.Contains("Stale")
                            && !stale.Events.Any(e =>
                                e.Type
                                    is "command-started"
                                        or "command-completed"
                                        or "file-changed"
                                        or BudgetLedger.CodingUsage
                            ),
                        "Only the expected turn and thread contribute result, command, file and usage evidence."
                    );
                }
                var approval = await Terminal(runner, runner.Start(Request("approval")));
                check(
                    approval.State == "completed"
                        && approval.Events.Any(e => e.Type == "warning" && e.Message.Contains("Declined")),
                    "The worker still declines requests for extra execution authority."
                );
                var pidPath = Path.Combine(root, "fixture-hang.pid");
                var running = runner.Start(Request("hang"));
                for (int i = 0; i < 100 && !File.Exists(pidPath); i++)
                    await Task.Delay(25);
                check(File.Exists(pidPath), "The cancellation fixture was actually launched.");
                var queued = runner.Start(Request("success"));
                runner.Cancel(queued.Id);
                check(
                    (await Terminal(runner, queued)).State == "canceled",
                    "Canceling a waiting run does not execute it or strand its queue state."
                );
                runner.Cancel(running.Id);
                check(
                    (await Terminal(runner, running)).State == "canceled",
                    "Canceling an active fixture preserves canceled status and does not retry it."
                );
                var pid = int.Parse(File.ReadAllText(pidPath));
                bool Exited()
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        return process.HasExited;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                }
                for (int i = 0; i < 100 && !Exited(); i++)
                    await Task.Delay(25);
                check(Exited(), "Canceling the active fixture also ends its owned child process.");
            }
            var status = new CodexStream();
            status.Read("{\"method\":\"turn/completed\",\"params\":{\"turn\":{\"status\":\"inProgress\"}}}");
            check(status.Completed && status.Failure is not null, "Unknown terminal status is a protocol failure.");
            var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var path = Path.Combine(root, "approval.sqlite");
            var requests = new RequestStore(path);
            var reviews = new GitHubReviewStore(path) { Clock = () => now };
            var watches = new PipelineWatchStore(path);
            var request = requests.CreateRequest(
                WorkspaceOwner.LocalIdentity,
                "fictional",
                "Repair",
                "Fix fictional PR 42",
                "expiry"
            );
            requests.AllowAgent(request.Id);
            var workflow = new PullRequestWorkflow(
                "expiry",
                request.Id,
                1,
                GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id)),
                "owner/example",
                10,
                42,
                new string('a', 40),
                "evidence",
                PullRequestWorkflowChecks.Strategy(),
                "fake"
            )
            {
                ConsentGeneration = requests.AgentConsentGeneration(request.Id),
            };
            reviews.SaveWorkflow(workflow);
            var board = new DeveloperOwnerTasks(reviews, watches, requests);
            check(
                board.Tasks().Single().Start!.StartsWith("loop:"),
                "A current unused approval offers the scoped start action."
            );
            now = now.AddMinutes(31);
            var expired = board.Tasks().Single();
            check(
                expired.Start == "refresh:" + workflow.Id && expired.NextAction.Contains("expired"),
                "An expired PR90-style strategy offers refresh and explains the block before launch."
            );
            check(
                !reviews.ApprovalAvailable(workflow),
                "Reading an expired task card does not renew execution authority."
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("HIREZERO_CODEX_HOME", prior);
        }
    }
}
