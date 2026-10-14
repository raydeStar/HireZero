using HireZero.Core;

// Home: the owner's one running chat. Real work asked for there moves to a task thread, which reports back. Fake model; no network.
public static class HomeChecks
{
    sealed class FakeModel : IModelGateway
    {
        public string DefaultModel => "fake"; public string Reasoning => "none"; public List<string> Prompts = [];
        public void Prepare(string cell) { }
        public void Stop(string cell) { }
        public Task<string> CompleteAsync(string cell, string instructions, string prompt, CancellationToken token, string? model = null, int maxTokens = 900)
        { Prompts.Add(prompt); return Task.FromResult("Here's where things stand."); }
    }
    sealed class PrSignal : ITaskThreadSignal
    {
        public string? TaskKey(string text) => text.Contains("fix #216", StringComparison.OrdinalIgnoreCase) ? "pr#216" : null;
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            const string owner = "morgan@northwind.example";
            var db = Path.Combine(root, "home.sqlite");
            var requests = new RequestStore(db); var agents = new AgentStore(db); var model = new FakeModel();
            var feed = new HomeFeed(requests, new WorkspaceOwner(owner)); feed.Attach();
            var handler = new ConversationReplyHandler(requests, model, [], new WorkspaceOwner(owner), null, null, agents, [new PrSignal()], feed);
            AgentTask Reply(string requestId) => new("t-" + Guid.NewGuid().ToString("N"), "reply", requestId, "x", "ask", "active", null, null, null, "", "");

            var home = requests.Home(owner);
            check(requests.Home(owner).Id == home.Id && home.Status == "home" && home.DisplayTitle == "Home" && requests.IsHome(home.Id) &&
                requests.Conversations(null, null) is [{ Home: true }], "Home is created once, has its own status and is flagged for the sidebar.");

            requests.AddMessage(home.Id, owner, "Please fix #216, the P1 and P2 findings.", Guid.NewGuid().ToString());
            await handler.RunAsync(Reply(home.Id), () => false, default);
            var task = requests.Requests(null).Single(item => item.Id != home.Id);
            check(requests.HomeTask(task.Id) == home.Id && agents.OpenTasks().Any(open => open.RequestId == task.Id) && model.Prompts.Count == 0 &&
                requests.Messages(home.Id).Last().Body.Contains("(request:" + task.Id + ")"), "Work asked for in Home opens a task thread, queues its reply and links it from Home.");

            requests.AddMessage(home.Id, owner, "Also fix #216 test names while you're there.", Guid.NewGuid().ToString());
            await handler.RunAsync(Reply(home.Id), () => false, default);
            check(requests.Requests(null).Length == 2 && requests.Messages(task.Id).Any(message => message.Body.Contains("test names")) &&
                requests.Messages(home.Id).Last().Body.Contains("already open"), "Asking again reuses the open task thread.");

            await handler.RunAsync(Reply(task.Id), () => false, default);
            check(requests.OwnerMessages(home.Id).Any(message => message.Visibility == "owner" && message.Body.Contains("has an answer") && message.Body.Contains("(request:" + task.Id + ")")),
                "The task thread's first answer is echoed into Home.");

            requests.AddOwnerAgentNotice(task.Id, "The fix stopped: two lines need a look. More detail follows.", "pr-workflow:x:validation-attention");
            check(requests.OwnerMessages(home.Id).Count(message => message.Body.Contains("The fix stopped")) == 1 && requests.Messages(home.Id).All(message => !message.Body.Contains("The fix stopped")),
                "Owner notices on task threads become private follow-ups in Home.");
            requests.AddOwnerAgentNotice(task.Id, "The fix stopped: two lines need a look.", "pr-workflow:x:validation-attention");
            check(requests.OwnerMessages(home.Id).Count(message => message.Body.Contains("The fix stopped")) == 1, "A repeated notice isn't echoed twice.");

            requests.AddMessage(home.Id, owner, "How's it going?", Guid.NewGuid().ToString());
            await handler.RunAsync(Reply(home.Id), () => false, default);
            check(model.Prompts.Last().Contains("OPEN WORK") && model.Prompts.Last().Contains("request:" + task.Id) && model.Prompts.Last().Contains("<<untrusted open work"),
                "Questions in Home are answered with the open task threads as fenced context.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
