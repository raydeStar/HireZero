using HireZero.Core;
using Microsoft.Extensions.Logging.Abstractions;

// Synthetic queue and shift checks: a fake role (sensor + two task kinds), no GitHub, model or SemaForge.
public static class AgentShiftChecks
{
    sealed class Sensor : IShiftSensor
    {
        public ProactiveWork[] Signals = [];
        public int Calls;

        public Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token)
        {
            Calls++;
            return Task.FromResult((Signals, "Checked."));
        }
    }

    sealed class Runner
    {
        public List<string> Ran = [];
        public Func<bool>? Canceled;
        public Action? During;
        public bool Fail;
    }

    sealed class Reply(Runner runner) : IAgentTaskHandler
    {
        public string Kind => "reply";

        public Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token)
        {
            runner.Ran.Add("reply:" + task.RequestId);
            runner.Canceled = canceled;
            runner.During?.Invoke();
            if (runner.Fail)
                throw new ModelUnavailableException("OpenClaw could not answer. The cell may need OpenAI sign-in.");
            return Task.FromResult(new AgentTaskResult(canceled() ? "Canceled" : "Replied."));
        }
    }

    sealed class Probe(Runner runner) : IAgentTaskHandler
    {
        public string Kind => "probe";

        public Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token)
        {
            runner.Ran.Add("probe:" + task.Target);
            return Task.FromResult(new AgentTaskResult("Proposed.", "diag-" + task.Target));
        }
    }

    sealed class FakeRole(string id) : IRole
    {
        public RoleInfo Info { get; } = new(id, id, id);

        public void AddServices(IServiceCollection services, RoleContext context) { }

        public void MapEndpoints(WebApplication app) { }
    }

    static ProactiveWork Pr(int number, string head = "a") =>
        new("probe", "probe:" + number + "@" + head, "Probe " + number, number.ToString());

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new AgentStore(Path.Combine(root, "agent.sqlite"));
            var sensor = new Sensor();
            var runner = new Runner();
            var worker = new AgentWorker(
                store,
                [sensor],
                [new Reply(runner), new Probe(runner)],
                NullLogger<AgentWorker>.Instance
            );
            var monday9 = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

            // Off shift: asks still run; nothing is sensed or proposed.
            store.EnqueueReply("r1", "First ask", "m1");
            sensor.Signals = [Pr(42)];
            check(
                await worker.TickAsync(monday9, default) && runner.Ran.SequenceEqual(["reply:r1"]),
                "An ask must be answered off shift."
            );
            check(
                sensor.Calls == 0 && store.OpenTasks().Length == 0,
                "Off shift, the agent must not sense or queue proactive work."
            );

            // Two messages before the agent gets to them fold into one reply that reads the whole thread.
            var first = store.EnqueueReply("r2", "Folded", "m1");
            var second = store.EnqueueReply("r2", "Folded", "m2");
            check(
                first.Id == second.Id && store.OpenTasks().Length == 1 && second.DedupeKey!.EndsWith(":m2"),
                "Back-to-back messages must fold into one queued reply."
            );
            store.Cancel(second.Id);
            check(
                !await worker.TickAsync(monday9, default) && runner.Ran.Count == 1,
                "A canceled queued task must never run."
            );
            check(
                store.EnqueueReply("r2", "Folded", "m2").State == "queued",
                "Asking again for the same message must revive a canceled reply."
            );
            await worker.TickAsync(monday9, default);

            // On shift: sensing queues diagnoses; asks still go first.
            worker.Start(TimeSpan.FromHours(8), "manual", monday9);
            store.EnqueueReply("r3", "Ask during shift", "m1");
            check(await worker.TickAsync(monday9, default) && sensor.Calls == 1, "A shift must sense open PRs.");
            check(
                runner.Ran[^1] == "reply:r3" && store.OpenTasks().Single().Kind == "probe",
                "An ask must run before queued proactive work."
            );
            await worker.TickAsync(monday9.AddMinutes(1), default);
            check(
                runner.Ran[^1] == "probe:42" && store.Shift().ProactiveDone == 1,
                "On shift, the agent proposes a diagnosis for a sensed PR."
            );
            check(store.Recent(1).Single().RequestId == "diag-42", "A diagnosis must link its conversation.");
            check(sensor.Calls == 1, "Sensing must wait for its interval, not run every tick.");

            // The same revision is never re-diagnosed, even after a cancel; a new push is.
            check(store.EnqueueProactive(Pr(42)) is null, "A diagnosed revision must not be queued again.");
            var canceled = store.EnqueueProactive(Pr(43))!;
            store.Cancel(canceled.Id);
            check(
                store.EnqueueProactive(Pr(43)) is null,
                "A canceled proposal must not come back for the same revision."
            );
            check(store.EnqueueProactive(Pr(43, "b")) is not null, "A new revision may be diagnosed again.");
            check(
                store.CancelProactive() == 1 && store.OpenTasks().Length == 0,
                "Stopping a shift clears its queued proactive work."
            );

            // The shift's diagnosis budget caps proactive work.
            sensor.Signals = [.. Enumerable.Range(100, 10).Select(n => Pr(n))];
            await worker.TickAsync(monday9.Add(AgentWorker.SenseInterval), default);
            check(
                store.OpenTasks().Count(t => t.Kind == "probe") + store.Shift().ProactiveDone
                    == AgentStore.DefaultProactiveBudget,
                "Sensing must not queue more diagnoses than the shift budget allows."
            );
            store.CancelProactive();

            // Cancel during a running ask: its turn finishes, but the result is discarded.
            store.EnqueueReply("r4", "Cancel mid-turn", "m1");
            runner.During = () => store.Cancel(store.Active().Single().Id);
            await worker.TickAsync(monday9.AddMinutes(20), default);
            check(
                runner.Canceled!()
                    && store.Find(store.Recent(5).Concat(store.OpenTasks()).FirstOrDefault()?.Id ?? "")?.State
                        != "active",
                "Canceling a running task must signal the runner and never mark it done."
            );
            runner.During = null;

            // A failed turn is reported, not hidden, and can be retried.
            runner.Fail = true;
            var failing = store.EnqueueReply("r5", "Fails", "m1");
            await worker.TickAsync(monday9.AddMinutes(21), default);
            check(
                store.Find(failing.Id) is { State: "failed", Detail: { } detail } && detail.Contains("sign-in"),
                "A failed reply must keep the provider's reason."
            );
            runner.Fail = false;
            check(store.EnqueueReply("r5", "Fails", "m1").State == "queued", "Try again must requeue a failed reply.");

            // A restart puts an interrupted turn back in the queue.
            var interrupted = store.EnqueueReply("r6", "Interrupted", "m1");
            store.Transition(interrupted.Id, "queued", "active");
            store.RecoverInterrupted();
            check(store.Find(interrupted.Id)!.State == "queued", "A restart must requeue an interrupted turn.");

            // The shift ends at its window.
            await worker.TickAsync(monday9.AddHours(9), default);
            check(!store.Shift().On, "A shift must end when its window closes.");

            // Working hours.
            var hours = new ShiftSchedule(true, [1, 2, 3, 4, 5], "08:00", "17:00", "UTC");
            var off = store.Shift() with { On = false, StoppedDay = null, LastScheduledDay = null };
            check(
                ShiftClock.ScheduledStart(monday9, hours, off) is { Hour: 17 },
                "A working day inside its hours starts a shift that ends at the end time."
            );
            check(
                ShiftClock.ScheduledStart(monday9.AddHours(-2), hours, off) is null,
                "Nothing starts before working hours."
            );
            check(
                ShiftClock.ScheduledStart(new DateTimeOffset(2026, 10, 5, 16, 50, 0, TimeSpan.Zero), hours, off)
                    is null,
                "Nothing starts in the last 15 minutes."
            );
            check(
                ShiftClock.ScheduledStart(monday9.AddDays(-1), hours, off) is null,
                "A non-working day must not start a shift."
            );
            check(
                ShiftClock.ScheduledStart(monday9, hours, off with { StoppedDay = "2026-10-05" }) is null,
                "A day the owner stopped must not restart."
            );
            check(
                ShiftClock.ScheduledStart(monday9, hours, off with { LastScheduledDay = "2026-10-05" }) is null,
                "A day can't start twice."
            );
            check(
                ShiftClock.ScheduledStart(monday9, hours with { Enabled = false }, off) is null,
                "Disabled hours never start a shift."
            );
            store.SaveSchedule(hours);
            store.SaveShift(off);
            var tuesday9 = monday9.AddDays(1);
            await worker.TickAsync(tuesday9, default);
            check(
                store.Shift() is { On: true, Origin: "schedule-2026-10-06" },
                "The pump must start the scheduled shift."
            );
            worker.Stop(tuesday9.AddHours(1), byOwner: true);
            await worker.TickAsync(tuesday9.AddHours(2), default);
            check(!store.Shift().On, "An owner-stopped scheduled shift must not restart that day.");

            // Firing a role leaves its queued work visible as failed, not silently dropped or run by someone else.
            var orphan = store.EnqueueReply("r7", "Orphan", "m1");
            var fired = new AgentWorker(store, [], [], NullLogger<AgentWorker>.Instance);
            await fired.TickAsync(tuesday9.AddHours(3), default);
            check(
                store.Find(orphan.Id) is { State: "failed", Detail: "No hired role handles this kind of work." },
                "A task without a hired handler must fail visibly."
            );

            // Hiring and firing persist in roles.json and only accept roles this build knows.
            var catalog = new RoleCatalog(root, [new FakeRole("developer"), new FakeRole("marketer")], ["developer"]);
            check(catalog.HiredIds().SequenceEqual(["developer"]), "The default hires apply before roles.json exists.");
            catalog.SetHired("marketer", true);
            catalog.SetHired("developer", false);
            check(
                new RoleCatalog(root, catalog.Available, ["developer"])
                    .Hired()
                    .Select(role => role.Info.Id)
                    .SequenceEqual(["marketer"]),
                "Hiring and firing must survive a restart."
            );
            try
            {
                catalog.SetHired("ceo", true);
                check(false, "An unknown role must be refused.");
            }
            catch (WorkspaceException)
            {
                check(true, "An unknown role must be refused.");
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
