using HireZero.Core;

// The conversations sidebar and the per-thread step log, with a real store in a temp folder.
public static class ConversationListChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-threads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new RequestStore(Path.Combine(root, "threads.sqlite"));
            var quote = store.CreateRequest(
                "morgan@northwind.example",
                "quotedesk",
                "Pricing rounding",
                "Totals are off by a cent.",
                Guid.NewGuid().ToString()
            );
            var other = store.CreateRequest(
                "riley@northwind.example",
                "wiki",
                "Fix the sidebar",
                "Links 404.",
                Guid.NewGuid().ToString()
            );
            store.AddMessage(
                quote.Id,
                "workspace-agent",
                "The rounding happens in QuoteTotals before tax is applied, so 100% discounts break.",
                Guid.NewGuid().ToString()
            );

            var all = store.Conversations(null, null);
            check(
                all.Length == 2
                    && all[0].Id == quote.Id
                    && all[0].LastFromAgent
                    && all[0].Messages == 1
                    && all[0].Match is null,
                "Latest activity first; the agent spoke last."
            );
            check(
                store.Conversations("riley@northwind.example", null).Select(item => item.Id).SequenceEqual([other.Id]),
                "Everyone else sees only their own conversations."
            );
            check(
                store.Conversations(null, "quotetotals") is [{ Match: { } snippet }] && snippet.Contains("QuoteTotals"),
                "Search reaches message bodies and shows where it matched."
            );
            check(
                store.Conversations(null, "sidebar") is [{ Title: "Fix the sidebar", Match: null }],
                "Search covers titles."
            );
            check(
                store.Conversations(null, "100%") is [{ Id: var id }]
                    && id == quote.Id
                    && store.Conversations(null, "_").Length == 0,
                "Wildcards are searched literally."
            );
            check(
                store.Conversations("riley@northwind.example", "rounding").Length == 0,
                "Search never reaches someone else's conversations."
            );

            var task = "task-" + Guid.NewGuid().ToString("N");
            using (AgentActivity.Begin(task))
            {
                AgentActivity.Note("Gathered context.");
                AgentActivity.Note("Asking the model…");
            }
            for (var index = 0; index < 60; index++)
                AgentActivity.Note("Unrelated step " + index);
            check(
                AgentActivity
                    .ForTask(task, 10)
                    .Select(step => step.Text)
                    .SequenceEqual(["Asking the model…", "Gathered context."])
                    && AgentActivity.LatestFor(task)?.Text == "Asking the model…",
                "A task keeps its own steps even when other work fills the shared feed."
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
