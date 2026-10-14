using HireZero.Core;
using Microsoft.Extensions.Logging.Abstractions;

// Budget ledger with synthetic usage: thresholds, notices, per-task caps, resume, the month window and the shift hold.
public static class BudgetChecks
{
    sealed class Sensor : IShiftSensor
    {
        public int Calls;
        public Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token) { Calls++; return Task.FromResult<(ProactiveWork[], string)>(([new("probe", "probe:1", "Probe 1", "1")], "Checked.")); }
    }
    sealed class Reply : IAgentTaskHandler
    {
        public string Kind => "reply"; public string? Role, Task;
        public Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token)
        { Role = UsageMeter.Role; Task = UsageMeter.Task; UsageMeter.Model("openai/test", 1200, 3); return System.Threading.Tasks.Task.FromResult(new AgentTaskResult("Replied.")); }
    }
    static CodingRun Run(string id, int minutes, long? tokens = null, string state = "completed")
    {
        var start = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        CodingEvent[] events = tokens is { } used ? [new(BudgetLedger.CodingUsage, "Used.", "{\"tokens\":" + used + "}", start.ToString("O"))] : [];
        return new(id, state, "C:\\work", "sol", "high", "sol", null, null, events, start.ToString("O"), start.AddMinutes(minutes).ToString("O"));
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-budget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "budget.sqlite"); var requests = new RequestStore(db); var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
            var budgets = new BudgetLedger(db, requests, "owner@example.com") { Clock = () => now };
            var task = requests.CreateRequest("owner@example.com", "owner/example", "Fix #7", "Fix it.", Guid.NewGuid().ToString());
            var home = requests.Home("owner@example.com");

            check(budgets.Blocked("developer", task.Id) is null && budgets.RoleStatus("developer").State == "unlimited", "Without limits nothing is blocked.");
            budgets.SetLimit("role:developer", new(Minutes: 60, TaskRuns: 2));
            check(budgets.RecordCoding(Run("a", 30, 5000), "developer", task.Id) && !budgets.RecordCoding(Run("a", 30, 5000), "developer", task.Id),
                "A coding run counts once, however often it is seen.");
            check(!budgets.RecordCoding(Run("q", 5, state: "running"), "developer", task.Id), "A running run is not counted until it ends.");
            var used = budgets.RoleMonth("developer");
            check(used is { Runs: 1, Minutes: 30, Tokens: 5000 }, "Coding usage records the run, its minutes and the tokens Codex reported.");
            check(budgets.RoleStatus("developer") is { State: "ok", Percent: 50 } && !requests.OwnerMessages(home.Id).Any(), "Half the budget is quiet.");

            budgets.RecordCoding(Run("b", 20), "developer", task.Id);
            check(budgets.RoleStatus("developer").State == "warning" && budgets.Blocked("developer", null) is null, "Past 80% of the month warns but does not block.");
            check(requests.OwnerMessages(home.Id).Count(m => m.Body.StartsWith("Budget: the developer role has used 83%")) == 1, "The soft threshold posts one Home notice.");
            check(requests.OwnerMessages(task.Id).Any(m => m.Body.StartsWith("Budget reached for this task (2 of 2 coding runs)")), "The task's cap reached is said in its thread.");
            check(budgets.Blocked("developer", task.Id)!.StartsWith("this task's cap"), "A task at its cap blocks new work there.");
            var other = requests.CreateRequest("owner@example.com", "owner/example", "Fix #8", "Fix it.", Guid.NewGuid().ToString());
            check(budgets.Blocked("developer", other.Id) is null, "Another task is not blocked by this task's cap.");
            budgets.SetLimit("task:" + task.Id, new(Runs: 4));
            check(budgets.Blocked("developer", task.Id) is null, "Raising one task's cap lets it continue.");

            budgets.RecordCoding(Run("c", 15), "developer", other.Id);
            check(budgets.RoleStatus("developer") is { State: "reached", Blocks: true } && budgets.Blocked("developer", other.Id)!.Contains("October budget is used up (65 of 60 coding minutes)"),
                "At the monthly limit new developer work is blocked, naming the measure.");
            check(requests.OwnerMessages(home.Id).Count(m => m.Body.StartsWith("Budget reached: the developer role")) == 1, "The hard limit posts one Home notice.");
            check(budgets.ShiftBlocked() is not null && budgets.Blocked("workspace", null) is null, "A used-up role holds shift work; other roles are unaffected.");
            budgets.RecordCoding(Run("d", 1), "developer", null);
            check(requests.OwnerMessages(home.Id).Count(m => m.Body.StartsWith("Budget reached: the developer role")) == 1, "The notice isn't repeated for more usage.");

            budgets.Resume("role:developer");
            check(budgets.Blocked("developer", other.Id) is null && budgets.RoleStatus("developer") is { State: "reached", Resumed: true }, "Resume waives the stop for this month.");
            now = now.AddMonths(1);
            check(budgets.RoleMonth("developer").Runs == 0 && budgets.RoleStatus("developer") is { State: "ok", Resumed: false }, "A new month starts from zero.");
            now = now.AddMonths(-1);

            budgets.SetLimit("role:developer", new(Minutes: 120, TaskRuns: 2));
            check(budgets.Blocked("developer", other.Id) is null && budgets.RoleStatus("developer").State == "ok", "Raising the limit clears the block.");
            var failed = false; try { budgets.SetLimit("role:developer", new(Runs: -1)); } catch (WorkspaceException) { failed = true; }
            check(failed && !BudgetLedger.ValidScope("role:../x") && BudgetLedger.ValidScope("task:" + task.Id), "Negative limits and odd scopes are refused.");

            // Model calls report through the meter, attributed to the role and task running them.
            UsageMeter.Ledger = budgets;
            using (UsageMeter.For("developer", "t-model")) using (UsageMeter.As("reviewer")) UsageMeter.Model("openai/test", 700, 120);
            check(UsageMeter.Role == "workspace" && budgets.TaskTotal("t-model") is { Calls: 1, Tokens: 700, Minutes: 0 } && budgets.TaskRole("t-model") == "reviewer", "A model call counts tokens, not coding minutes, under the role and task in scope.");

            // On shift with a used-up budget: nothing proactive is sensed or queued; the owner's ask still runs, metered as the workspace.
            budgets.SetLimit("role:workspace", new(Tokens: 1000));
            var agents = new AgentStore(db); var sensor = new Sensor(); var reply = new Reply();
            var worker = new AgentWorker(agents, [sensor], [reply], NullLogger<AgentWorker>.Instance, budgets: budgets);
            agents.EnqueueReply(task.Id, "Ask", "m1");
            check(await worker.TickAsync(now, default) && reply.Role == "workspace" && reply.Task == task.Id, "An ask runs and is metered against its task.");
            check(budgets.RoleStatus("workspace").State == "reached", "The ask's model use reaches the workspace budget.");
            worker.Start(TimeSpan.FromHours(2), "manual", now);
            check(!await worker.TickAsync(now.AddMinutes(1), default) && sensor.Calls == 0 && agents.OpenTasks().Length == 0 &&
                agents.Shift().SenseNote!.StartsWith("Budget reached"), "At the limit the shift queues no proactive work and says why.");
            budgets.Resume("role:workspace");
            await worker.TickAsync(now.AddMinutes(40), default);
            check(sensor.Calls == 1, "After resume the shift checks its sources again.");
        }
        finally { UsageMeter.Ledger = null; try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
