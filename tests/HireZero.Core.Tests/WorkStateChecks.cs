using HireZero.Core;

public static class WorkStateChecks
{
    sealed class BlockingSensor : IShiftSensor
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token)
        {
            Entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ([], "Unreachable.");
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var at = "2026-01-01T00:00:00Z";
        var conversation = new ConversationSummary(
            "request",
            "example",
            "Example",
            "ready",
            at,
            "owner@example.invalid",
            at,
            true,
            1,
            null,
            "Example"
        );
        var task = new AgentTask(
            "lease-check",
            "reply",
            conversation.Id,
            "Reply",
            "ask",
            "active",
            null,
            null,
            null,
            at,
            at
        );
        check(
            ConversationWorkStates.For(conversation, null, null, true).State == "Answered",
            "An open recent reply is answered, not running and not waiting on the owner."
        );
        check(
            ConversationWorkStates.For(conversation with { AgentAsked = true }, null, null, true).State
                == "Waiting for you",
            "A reply that asks the owner something waits for them."
        );
        check(
            ConversationWorkStates
                .For(conversation with { LastFromAgent = false }, null, null, true, needsGoAhead: true)
                .State == "Waiting for you",
            "A message waiting on the owner's go-ahead waits for them."
        );
        check(
            RequestStore.AsksOwner("Here is the plan.\n\nShould I go ahead?")
                && !RequestStore.AsksOwner("Done. Tests pass.")
                && !RequestStore.AsksOwner("Why did it fail? Because the lock moved.\nFixed it."),
            "Only a question in the closing lines counts as asking the owner."
        );
        check(
            ConversationWorkStates.For(conversation, task, null, true) is { State: "Paused", Freshness: "unknown" },
            "Persisted active rows without current execution are unknown."
        );
        var lease = AgentActivity.Begin(task.Id);
        check(
            ConversationWorkStates.For(conversation, task, null, true) is { State: "Running", Freshness: "live" },
            "Current host task lease establishes running."
        );
        lease.Dispose();
        lease.Dispose();
        check(!AgentActivity.IsRunning(task.Id), "Disposed execution leases cannot remain live or dispose twice.");
        check(
            ConversationWorkStates.For(conversation, task with { State = "queued" }, null, true).State == "Queued",
            "Queued replies remain queued without an execution lease."
        );
        check(
            ConversationWorkStates
                .For(conversation, task with { State = "queued", Source = "proactive" }, null, false)
                .State == "Paused",
            "Stopped shift pauses pending proactive work."
        );
        foreach (var pair in new[] { ("done", "Completed"), ("failed", "Failed"), ("canceled", "Cancelled") })
            check(
                ConversationWorkStates
                    .For(conversation, task with { Kind = "diagnose-pr", State = pair.Item1 }, null, true)
                    .State == pair.Item2,
                "Terminal task states remain truthful."
            );
        check(
            ConversationWorkStates.For(conversation, task with { State = "done" }, null, true).State == "Answered",
            "A finished reply reads as answered, not as completed work."
        );
        check(
            ConversationWorkStates
                .For(
                    conversation,
                    null,
                    new OwnerTask(
                        "w",
                        "Fix",
                        "example",
                        null,
                        conversation.Id,
                        "validation-attention",
                        at,
                        "owner",
                        "normal",
                        ["Missing proof."],
                        "Review",
                        "separate-actions-required"
                    ),
                    true
                )
                .State == "Waiting for you",
            "A paused fix waits for the owner's decision."
        );
        var workflow = new OwnerTask(
            "workflow",
            "Fix",
            "example",
            null,
            conversation.Id,
            "publication-proposed",
            "2026-01-01T01:00:00Z",
            "owner",
            "normal",
            [],
            "Review",
            "needs-owner"
        );
        check(
            ConversationWorkStates.For(conversation, task with { State = "done" }, workflow, true).State
                == "Waiting for you",
            "Completed coding does not hide later publication approval."
        );
        check(
            ConversationWorkStates
                .For(
                    conversation,
                    null,
                    workflow with
                    {
                        Status = "coding",
                        Approval = "separate-actions-required",
                    },
                    true
                )
                .Freshness == "unknown",
            "Workflow durable coding state does not impersonate current execution."
        );
        check(
            ConversationWorkStates
                .For(
                    conversation,
                    null,
                    workflow with
                    {
                        Status = "coding",
                        Approval = "separate-actions-required",
                        ExecutionState = "live",
                    },
                    true
                )
                .State == "Running",
            "Workflow live execution proof reaches conversation state."
        );
        check(
            ConversationWorkStates
                .For(
                    conversation,
                    null,
                    workflow with
                    {
                        Status = "consent-revoked",
                        Approval = "separate-actions-required",
                    },
                    true
                )
                .State == "Blocked",
            "Revoked workflows remain blocked."
        );
        var path = Path.Combine(Path.GetTempPath(), "work-state-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var store = new AgentStore(path);
            var now = DateTimeOffset.Parse(at);
            store.SaveShift(
                store.Shift() with
                {
                    On = true,
                    StartedAt = at,
                    EndsAt = now.AddHours(1).ToString("O"),
                    LastSenseAt = at,
                    SenseNote = "No new evidence.",
                }
            );
            check(
                ShiftProgressView.For(store, false, now.AddMinutes(1)).NextCheckAt == now.AddMinutes(15).ToString("O"),
                "Empty queues retain a bounded next source check."
            );
            check(
                ShiftProgressView.For(store, false, now.AddMinutes(18)).Freshness == "stale",
                "Missed checks cannot be reported as fresh."
            );
            check(
                ShiftProgressView.For(store, true, now.AddMinutes(1)).State == "Scanning",
                "Actual source scan has a distinct live phase."
            );
            check(
                ShiftProgressView.For(store, true, now.AddHours(2)).State == "Paused",
                "Expired shifts never display live scanning."
            );
            var sensor = new BlockingSensor();
            var worker = new AgentWorker(
                store,
                [sensor],
                [],
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentWorker>.Instance
            )
            {
                Clock = () => now,
            };
            store.SaveShift(store.Shift() with { LastSenseAt = null });
            var scanning = worker.TickAsync(now, default);
            await sensor.Entered.Task;
            check(worker.Scanning, "Scanning is shown only while a bounded check is underway.");
            worker.Stop(now, true);
            await scanning;
            check(
                !worker.Scanning
                    && !store.Shift().On
                    && store.Shift().LastSenseAt is null
                    && store.OpenTasks().Length == 0,
                "Stopping cancels the source scan and suppresses late progress/work publication."
            );
            var requests = new RequestStore(path);
            var owner = new WorkspaceOwner("owner@example.invalid");
            var feed = new HomeFeed(requests, owner);
            feed.Attach();
            var realHome = requests.Home(owner.Login);
            var localHome = requests.Home(WorkspaceOwner.LocalIdentity);
            var request = requests.CreateRequest(owner.Login, "example", "Task", "Task", "outcome-check");
            var completed = task with { RequestId = request.Id, State = "done" };
            feed.TaskFinished(completed);
            feed.TaskFinished(completed);
            check(
                requests.OwnerMessages(realHome.Id).Length == 1 && requests.OwnerMessages(localHome.Id).Length == 1,
                "A completed task mirrors once into both established owner Homes."
            );
        }
        finally
        {
            File.Delete(path);
        }
    }
}
