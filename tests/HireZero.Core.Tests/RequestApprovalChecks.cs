using HireZero.Core;
using Microsoft.Extensions.Logging.Abstractions;

public static class RequestApprovalChecks
{
    sealed class FakeModel : IModelGateway
    {
        public int Calls;
        public string DefaultModel => "fake";
        public string Reasoning => "none";

        public void Prepare(string cell) { }

        public void Stop(string cell) { }

        public Task<string> CompleteAsync(
            string cell,
            string instructions,
            string prompt,
            CancellationToken token,
            string? model = null,
            int maxTokens = 900
        )
        {
            Calls++;
            return Task.FromResult("A synthetic answer");
        }
    }

    sealed class BlockingModel : IModelGateway
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public string DefaultModel => "fake";
        public string Reasoning => "none";

        public void Prepare(string cell) { }

        public void Stop(string cell) { }

        public Task<string> CompleteAsync(
            string cell,
            string instructions,
            string prompt,
            CancellationToken token,
            string? model = null,
            int maxTokens = 900
        )
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            return Result.Task.WaitAsync(token);
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "hirezero-approval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "test.sqlite");
            var requests = new RequestStore(db);
            var model = new FakeModel();
            var owner = new WorkspaceOwner("owner@example.invalid");
            var handler = new ConversationReplyHandler(requests, model, [], owner);
            var triage = new RequestTriageService(requests, new TriageStore(db), model);
            AgentTask Reply(string id) =>
                new("test", "reply", id, "Example", "ask", "queued", null, null, null, "now", "now");
            foreach (var role in PersonRoles.All)
            {
                var login = role + "@example.invalid";
                requests.SetPerson(login, role, false);
                var item = requests.CreateRequest(login, "Example", "Question", "Explain this", role);
                var before = model.Calls;
                await handler.RunAsync(Reply(item.Id), () => false, default);
                check(model.Calls == before, role + " requests wait for per-request owner permission.");
                triage.Decide(item.Id, true, null);
                await handler.RunAsync(Reply(item.Id), () => false, default);
                check(
                    model.Calls == before + 1,
                    "Acceptance resumes an acknowledged " + role + " request without a new message."
                );
                await handler.RunAsync(Reply(item.Id), () => false, default);
                check(model.Calls == before + 1, "Repeated handler runs do not answer the same opening twice.");
            }
            requests.SetPerson("declined@example.invalid", "trusted", false);
            var declined = requests.CreateRequest(
                "declined@example.invalid",
                "Example",
                "Question",
                "Explain this",
                "declined"
            );
            triage.Decide(declined.Id, false, null);
            requests.AddMessage(declined.Id, declined.Requester, "Following up", "followup");
            var prior = model.Calls;
            await handler.RunAsync(Reply(declined.Id), () => false, default);
            check(model.Calls == prior, "Decline blocks model work after a trusted requester follows up.");

            var pending = requests.CreateRequest(
                "declined@example.invalid",
                "Example",
                "https://example.invalid/question",
                "Explain this",
                "pending"
            );
            try
            {
                await triage.SummarizeAsync(pending.Id, default);
            }
            catch (WorkspaceException) { }
            check(model.Calls == prior, "A pending request cannot automatically invoke the owner summary model.");
            await new ThreadTitler(requests, model, NullLogger<ThreadTitler>.Instance).TitleAsync(pending.Id, default);
            check(model.Calls == prior, "Title backfill cannot invoke the model for a pending coworker request.");
            await RacesAsync(requests, db, owner, check);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static async Task RacesAsync(RequestStore requests, string db, WorkspaceOwner owner, Action<bool, string> check)
    {
        var agents = new AgentStore(db);
        var immediate = new FakeModel();
        var triage = new RequestTriageService(requests, new TriageStore(db), immediate, owner, agents);
        WorkRequest New(string key) =>
            requests.CreateRequest(
                "visitor@example.invalid",
                "Example",
                "https://example.invalid/" + key,
                "Question",
                key
            );
        AgentTask Reply(string id) =>
            new("test", "reply", id, "Example", "ask", "queued", null, null, null, "now", "now");
        var item = New("accept-race");
        var old = RequestAgentWork.Queue(requests, agents, null, item)!;
        var handler = new ConversationReplyHandler(requests, immediate, [], owner);
        await handler.RunAsync(old, () => false, default);
        agents.Transition(old.Id, "queued", "done");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => triage.Decide(item.Id, true, null))));
        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => RequestAgentWork.Allow(requests, agents, null, item)))
        );
        check(
            agents.OpenTasks().Count(task => task.RequestId == item.Id) == 1,
            "Concurrent accept/allow clicks queue exactly one continuation after the acknowledgement task completed."
        );
        var continuation = agents.OpenTasks().Single(task => task.RequestId == item.Id);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => handler.RunAsync(continuation, () => false, default)));
        check(immediate.Calls == 1, "Concurrent reply executions produce one model call and one answer.");
        check(
            requests.Messages(item.Id).Count(message => message.Body == "The owner accepted this request.") == 1,
            "Repeated accept writes only one decision message."
        );
        check(
            triage.Open(item, "ai").Decision == "accepted",
            "Reopening triage cannot overwrite the owner's decision."
        );

        var declined = New("allow-decline");
        RequestAgentWork.Allow(requests, agents, null, declined);
        triage.Decide(declined.Id, false, null);
        check(
            !requests.AgentAllowed(declined.Id) && agents.OpenTasks().All(task => task.RequestId != declined.Id),
            "Declining an allowed request revokes effective permission and cancels queued replies."
        );
        try
        {
            RequestAgentWork.Allow(requests, agents, null, declined);
            check(false, "Decline cannot be bypassed with allow-agent.");
        }
        catch (WorkspaceException)
        {
            check(true, "Decline cannot be bypassed with allow-agent.");
        }

        foreach (var operation in new[] { "reply", "summary", "title" })
        {
            var blocked = new BlockingModel();
            var target = New("decline-during-" + operation);
            requests.AllowAgent(target.Id);
            var service = new RequestTriageService(requests, new TriageStore(db), blocked, owner);
            Task action = operation switch
            {
                "summary" => service.SummarizeAsync(target.Id, default),
                "title" => new ThreadTitler(requests, blocked, NullLogger<ThreadTitler>.Instance, owner).TitleAsync(
                    target.Id,
                    default
                ),
                _ => new ConversationReplyHandler(requests, blocked, [], owner).RunAsync(
                    Reply(target.Id),
                    () => false,
                    default
                ),
            };
            await blocked.Entered.Task;
            service.Decide(target.Id, false, null);
            blocked.Result.SetResult("Synthetic model result");
            try
            {
                await action;
            }
            catch (WorkspaceException) { }
            check(
                requests.Messages(target.Id).All(message => message.Body != "Synthetic model result")
                    && service.Find(target.Id) is { Decision: "declined", Summary: null }
                    && requests.FindRequest(target.Id)?.ShortTitle is null,
                "Decline during " + operation + " discards the in-flight result and preserves the decision."
            );
        }

        var changed = New("message-during-reply");
        requests.AllowAgent(changed.Id);
        var delayed = new BlockingModel();
        var stale = new ConversationReplyHandler(requests, delayed, [], owner).RunAsync(
            Reply(changed.Id),
            () => false,
            default
        );
        await delayed.Entered.Task;
        requests.AddMessage(changed.Id, changed.Requester, "A newer instruction", "newer");
        delayed.Result.SetResult("Obsolete answer");
        await stale;
        check(
            requests.Messages(changed.Id).All(message => message.Body != "Obsolete answer"),
            "A new message during a reply discards the obsolete answer."
        );
        await handler.RunAsync(Reply(changed.Id), () => false, default);
        check(
            requests.PendingReplyKey(changed.Id) is null,
            "The newer message is answered once on the next continuation."
        );

        var summaryItem = New("summary-accept-race");
        requests.AllowAgent(summaryItem.Id);
        var summaryModel = new BlockingModel();
        var summaries = new RequestTriageService(requests, new TriageStore(db), summaryModel, owner);
        var first = summaries.SummarizeAsync(summaryItem.Id, default);
        await summaryModel.Entered.Task;
        var second = summaries.SummarizeAsync(summaryItem.Id, default);
        summaries.Decide(summaryItem.Id, true, null);
        summaryModel.Result.SetResult("Synthetic summary");
        await Task.WhenAll(first, second);
        check(
            summaryModel.Calls == 1 && summaries.Find(summaryItem.Id)?.Decision == "accepted",
            "Concurrent summaries spend one model turn and cannot overwrite acceptance."
        );

        var memoryItem = New("decline-during-memory");
        requests.AllowAgent(memoryItem.Id);
        requests.Publish(memoryItem.Id, "Synthetic finished work", "normal", "done", "Finished");
        var suggestions = new MemorySuggestionStore(db);
        var memoryModel = new BlockingModel();
        var memory = new MemorySuggestionHandler(
            new MemorySources(new IntakeStore(db), requests, suggestions, owner),
            suggestions,
            memoryModel
        );
        var memoryRun = memory.RunAsync(Reply(memoryItem.Id), () => false, default);
        await memoryModel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        triage.Decide(memoryItem.Id, false, null);
        memoryModel.Result.SetResult(
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    suggestions = new[]
                    {
                        new
                        {
                            source = "request:" + memoryItem.Id,
                            text = "The synthetic project has a documented owner.",
                            reason = "A durable synthetic fact.",
                        },
                    },
                }
            )
        );
        await memoryRun;
        check(
            suggestions.Pending().Length == 0 && !suggestions.Seen().Contains("request:" + memoryItem.Id),
            "Decline during memory generation discards the fact and its seen marker."
        );

        requests.SetPerson("other@example.invalid", "visitor", false);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Items["login"] = "other@example.invalid";
        context.Items["role"] = "visitor";
        check(
            !WorkspaceAccess.CanRead(context, item) && !WorkspaceAccess.IsOwner(context),
            "A visitor cannot read someone else's request or gain the owner execution role."
        );
        context.Items["login"] = item.Requester;
        check(WorkspaceAccess.CanRead(context, item), "A visitor can read their own request.");
    }
}
