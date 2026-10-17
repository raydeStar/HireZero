using HireZero.Core;

// What a task card says it is, from the conversation's own words.
public static class OwnerTaskKindChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        WorkRequest Item(string title, string description = "") =>
            new("id", "owner@example.com", "p", title, description, "queued", null, "2026-10-07T00:00:00Z");
        check(
            OwnerTaskText.RequestKind(
                Item("I have a build failure here -- https://github.com/o/r/pull/213 can you fix it")
            ) == "Build fix"
                && OwnerTaskText.RequestKind(Item("Fix P1 and P2 bugs #216")) == "PR fix"
                && OwnerTaskText.RequestKind(Item("Take a look at PR 89")) == "PR review",
            "PR asks read as build fix, PR fix or PR review."
        );
        check(
            OwnerTaskText.RequestKind(Item("What's going on with QuoteDesk?")) == "Question"
                && OwnerTaskText.RequestKind(Item("Connect a GitHub repository from chat")) == "Setup"
                && OwnerTaskText.RequestKind(Item("Draft the release notes")) == "Request",
            "Questions, setup and everything else are labelled plainly."
        );
        check(
            OwnerTaskText.Brief("  line one\n\n line   two ") == "line one line two"
                && OwnerTaskText.Brief(new string('a', 400))!.Length == 280
                && OwnerTaskText.Brief("  ") is null,
            "Hover detail is one tidy, bounded line."
        );
        // Go-ahead runs only when a message is waiting, and the notice says which happened.
        var root = Path.Combine(Path.GetTempPath(), "workspace-allow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "allow.sqlite");
            var requests = new RequestStore(db);
            var agents = new AgentStore(db);
            var owner = new WorkspaceOwner("mark@example.com");
            var answered = requests.CreateRequest(
                WorkspaceOwner.LocalIdentity,
                "p",
                "Fix PR 213 build failure",
                "Please fix it.",
                Guid.NewGuid().ToString()
            );
            requests.AddMessage(answered.Id, "workspace-agent", "Old answer.", Guid.NewGuid().ToString());
            RequestAgentWork.Allow(requests, agents, null, answered, owner);
            check(
                !agents.OpenTasks().Any(task => task.RequestId == answered.Id)
                    && requests.OwnerMessages(answered.Id).Any(m => m.Body.Contains("nothing to run yet"))
                    && !requests.OwnerMessages(answered.Id).Any(m => m.Body.Contains("requester")),
                "Go-ahead on an answered conversation says there's nothing to run, without calling the owner a requester."
            );
            var waiting = requests.CreateRequest(
                owner.Login,
                "p",
                "What changed today?",
                "What changed today?",
                Guid.NewGuid().ToString()
            );
            RequestAgentWork.Allow(requests, agents, null, waiting, owner);
            check(
                requests.OwnerMessages(waiting.Id).Any(m => m.Body.Contains("working on your message now"))
                    == agents.OpenTasks().Any(task => task.RequestId == waiting.Id),
                "Go-ahead says it's working only when a reply was actually queued."
            );
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    }
}
