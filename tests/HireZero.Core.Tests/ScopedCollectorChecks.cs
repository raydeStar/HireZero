using HireZero.Core;
using Microsoft.Extensions.Logging.Abstractions;

public static class ScopedCollectorChecks
{
    sealed class GateSensor : IShiftSensor
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<(ProactiveWork[], string)> SenseAsync(CancellationToken token)
        { Entered.SetResult(); await Release.Task; return ([new("probe", "expiry", "Observe", null)], "Checked."); }
    }
    sealed class Handler : IAgentTaskHandler
    {
        public int Calls; public string Kind => "probe";
        public Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token) { Calls++; return Task.FromResult(new AgentTaskResult("Observed")); }
    }
    public static async Task RunAsync(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "scoped-collector-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var owner = new WorkspaceOwner("owner@example.invalid"); var now = DateTimeOffset.UtcNow;
            IntakeRecord Item(string id) => new(id,"outlook",id,null,"sender@example.invalid","Fictional task", "review","rule","Review",null,null,"normal","rule",null,[],now.ToString("O"),"Never included in a proposal");
            var eligible = Item("eligible"); var high = Item("high") with { Priority = "high" };
            var records = new[] { eligible, high, eligible, Item("dismissed") with { Dismissed = true }, Item("ignore") with { Verdict = "ignore" },
                Item("guard") with { DecidedBy = "guard" }, Item("flagged") with { Flags = [ContentGuard.Injection] }, Item("linked") with { RequestId = "request" },
                Item("secret") with { Subject = "password=fictionalsecret" }, Item("stale") with { CreatedAt = now.AddDays(-15).ToString("O") },
                Item("future") with { CreatedAt = now.AddMinutes(1).ToString("O") }, Item("invalid") with { CreatedAt = "invalid" } };
            var found = InboxShiftSensor.Discover(records, owner, now);
            check(found.Length == 2 && found[0].Priority == "high", "Inbox proposals select recent unlinked review/action metadata, rank priority and exclude linked, dismissed, guarded, secret, ignored and invalid rows.");
            check(found.All(task => task.RequestId is null && task.Approval == "needs-owner" && !task.NextAction.Contains("Never included")), "Inbox metadata conveys no body text, request consent or execution authority.");
            check(InboxShiftSensor.Discover(Enumerable.Range(0, 50).Select(i => Item("id" + i)).ToArray(), owner, now).Length == 12, "Inbox task proposals are bounded.");
            var agents = new AgentStore(path); var proposals = new ShiftProposalStore(path); var intake = new IntakeStore(path); intake.Save(eligible);
            var sensor = new InboxShiftSensor(intake, proposals, agents, owner); await sensor.SenseAsync(default);
            check(proposals.Tasks().Length == 0, "Off-shift inbox collector publishes nothing.");
            var worker = new AgentWorker(agents, [sensor], [], NullLogger<AgentWorker>.Instance); worker.Start(TimeSpan.FromHours(1), "manual", now);
            await worker.TickAsync(now, default); check(proposals.Tasks().Length == 1 && agents.OpenTasks().Length == 0, "A shift produces an Inbox suggestion without creating a request or executable task.");
            intake.Save(eligible with { Dismissed = true }); await sensor.SenseAsync(default);
            check(proposals.Tasks().Length == 0, "Dismissed source metadata disappears on refresh.");
            worker.Stop(now.AddMinutes(1), true);

            var gate = new GateSensor(); var handler = new Handler(); var clock = now;
            var expiring = new AgentWorker(agents, [gate], [handler], NullLogger<AgentWorker>.Instance) { Clock = () => clock };
            var shift = expiring.Start(TimeSpan.FromMinutes(1), "manual", clock); var tick = expiring.TickAsync(clock, default);
            await gate.Entered.Task; clock = now.AddMinutes(2); gate.Release.SetResult(); await tick;
            check(!agents.Shift().On && agents.OpenTasks().Length == 0 && handler.Calls == 0, "A blocked scan released after the deadline cannot publish, enqueue or dispatch work.");
            check(!agents.WithCurrentShift(shift, () => throw new Exception(), () => clock), "Deadline fencing rejects expired authority even without an owner stop.");
            agents.SaveShift(shift); agents.EnqueueProactive(new("probe", "already-queued", "Observe", null));
            var queuedWorker = new AgentWorker(agents, [], [handler], NullLogger<AgentWorker>.Instance) { Clock = () => clock };
            await queuedWorker.TickAsync(now, default);
            check(handler.Calls == 0 && agents.OpenTasks().Single().State == "queued", "Already queued proactive work is not dispatched after expiry.");

            AgentSession Session(string status, string at) => new("stable-session", "codex", "external", "Fictional follow-up",null,null,null,status,null,null,null,at,[]);
            var old = RecentWorkShiftSensor.Discover([Session("idle", now.ToString("O"))],owner,now).Single();
            var unchanged = RecentWorkShiftSensor.Discover([Session("idle", now.ToString("O"))],owner,now).Single();
            var fresh = RecentWorkShiftSensor.Discover([Session("needs-you", now.AddMinutes(1).ToString("O"))],owner,now.AddMinutes(1)).Single();
            var views = new OwnerTaskStore(path); views.Save(old.Id,new("resolved","low"));
            check(views.Apply(unchanged).LocalState == "resolved" && unchanged.Id == old.Id, "Unchanged session evidence deduplicates and retains its local decision.");
            check(fresh.Id != old.Id && views.Apply(fresh) is { LocalState: "open", Priority: "high" }, "A new needs-you event gets a fresh evidence-bound suggestion and does not inherit an old resolved/low decision.");
        }
        finally { File.Delete(path); }
    }
}
