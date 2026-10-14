using HireZero.Core;

// One driver at a time, with a fake driver: no Codex or Claude process is started.
public static class SteeringChecks
{
    sealed class FakeDriver : ISessionDriver
    {
        public string App => "codex";
        public List<(string Kind, string Target, SteerRequest Request)> Calls = []; public Dictionary<string, CodingRun> Runs = new(); public List<string> Canceled = [];
        CodingRun Make(string root, string? thread) { var run = new CodingRun("run-" + (Runs.Count + 1), "running", root, "m", "", null, null, null, [], "", "", thread); Runs[run.Id] = run; return run; }
        public CodingRun Send(AgentSession session, SteerRequest request) { Calls.Add(("send", session.ExternalId, request)); return Make(session.Cwd!, session.ExternalId); }
        public CodingRun Start(string folder, string title, SteerRequest request) { Calls.Add(("start", folder + "|" + title, request)); return Make(folder, null); }
        public CodingRun? Find(string runId) => Runs.GetValueOrDefault(runId);
        public void Cancel(string runId) => Canceled.Add(runId);
    }
    static async Task Refused(Func<object> action, Action<bool, string> check, string label)
    { try { action(); check(false, label); } catch (WorkspaceException) { check(true, label); } await Task.CompletedTask; }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-steer-" + Guid.NewGuid().ToString("N")); var folder = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        try
        {
            var sessions = new SessionStore(Path.Combine(root, "steer.sqlite")); var driver = new FakeDriver(); var now = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
            var steering = new SessionSteering(sessions, [driver]) { Clock = () => now };
            AgentSession Session(string id, string status, DateTimeOffset updated) => new("codex:" + id, "codex", id, "Thread " + id, folder, "owner/app", "main", status, "vscode", null, null, updated.ToString("O"), []);
            sessions.Merge(Session("idle", "idle", now.AddMinutes(-5)));
            sessions.Merge(Session("busy", "idle", now.AddMinutes(-5))); sessions.Apply(new("codex", "prompt", "busy", null), now.AddMinutes(-5));
            sessions.Merge(Session("asking", "idle", now.AddMinutes(-5))); sessions.Apply(new("codex", "notification", "asking", null), now.AddMinutes(-5));
            sessions.Merge(Session("fresh", "idle", now.AddSeconds(-20)));
            sessions.Merge(new("claude:x", "claude", "x", "Claude", folder, null, null, "idle", null, null, null, now.AddMinutes(-5).ToString("O"), []));

            await Refused(() => steering.Send("codex:busy", new("go", false, false)), check, "A session working in its app is never driven at the same time.");
            await Refused(() => steering.Send("codex:asking", new("go", false, false)), check, "A session holding an approval in its app must be answered there first.");
            await Refused(() => steering.Send("codex:fresh", new("go", false, false)), check, "A session active in the last minute is left alone.");
            await Refused(() => steering.Send("claude:x", new("go", false, false)), check, "Apps without a driver can't be steered.");
            await Refused(() => steering.Send("codex:idle", new("  ", false, false)), check, "An empty message is refused.");
            await Refused(() => steering.Send("codex:idle", new("go", false, true)), check, "Network only applies with edits.");
            check(driver.Calls.Count == 0, "Refusals never reach the app.");

            var run = steering.Send("codex:idle", new("  please add tests  ", true, false));
            check(driver.Calls.Single() is ("send", "idle", { Message: "please add tests", AllowEdits: true, AllowNetwork: false }) && run.ThreadId == "idle",
                "An idle, quiet session receives exactly the owner's message and permissions.");
            check(sessions.Find("codex:idle") is { Status: "working", SteerRunId: "run-1", LastEvent: "hirezero-turn" }, "While HireZero's turn runs, the session shows as working.");
            now = now.AddMinutes(5);
            await Refused(() => steering.Send("codex:idle", new("again", false, false)), check, "A second turn waits for the first.");
            steering.Cancel("codex:idle");
            check(driver.Canceled.SequenceEqual(["run-1"]), "Stop interrupts HireZero's running turn.");
            driver.Runs["run-1"] = driver.Runs["run-1"] with { State = "completed" };
            steering.Reconcile();
            check(sessions.Find("codex:idle")!.Status == "waiting" && steering.Turn("codex:idle")!.State == "completed", "When the turn ends, the session is waiting on you again.");
            now = now.AddMinutes(2);
            steering.Send("codex:idle", new("follow up", false, false));
            check(driver.Calls.Count == 2, "After the turn ends and things are quiet, the owner can send again.");

            var started = steering.Start("codex", folder, "Write release notes​", new("draft the notes", false, false));
            check(driver.Calls[^1] is ("start", var target, _) && target == folder + "|Write release notes" && started.State == "running", "A hand-off starts in the chosen folder with a cleaned title.");
            await Refused(() => steering.Start("codex", Path.Combine(root, "missing"), "x", new("go", false, false)), check, "A hand-off needs an existing folder.");
            await Refused(() => steering.Start("codex", Path.GetPathRoot(folder)!, "x", new("go", false, false)), check, "A drive root is never a hand-off folder.");
        }
        finally { Directory.Delete(root, true); }
    }
}
