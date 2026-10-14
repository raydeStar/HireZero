using HireZero.Roles.Developer;

public static class PipelineWatchChecks
{
    sealed class Source : IPipelineRunSource
    {
        public int Reads; public bool Fail; public bool Drift; public string Status = "in_progress";
        public TaskCompletionSource<PipelineObservation>? Pending;
        public Task<PipelineObservation> ReadAsync(PipelineScope scope, CancellationToken token)
        {
            Reads++;
            if (Fail) throw new ReviewAccessException("A simulated error; never stored as raw output.");
            return Pending?.Task ?? Task.FromResult(new PipelineObservation(scope, Drift ? new string('b', 40) : scope.HeadSha,
                Status, Status == "completed" ? "failure" : null, "revision-" + Reads));
        }
    }
    public static async Task RunAsync(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "pipeline-check-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var scope = new PipelineScope(1, 10, "owner/example", "main", new string('a', 40), 100, 1);
        try
        {
            var store = new PipelineWatchStore(path); var source = new Source(); var worker = new PipelineWatchWorker(store, source);
            var watch = store.Create(scope, now);
            check(store.Create(scope, now).Id == watch.Id, "Repeated exact watch creation deduplicates.");
            await worker.PollAsync(now, default);
            check(source.Reads == 1 && store.All()[0].Cursor == "revision-1", "Observation persists its run cursor.");
            await new PipelineWatchWorker(new PipelineWatchStore(path), source).PollAsync(now.AddSeconds(15), default);
            check(source.Reads == 1, "Restart retains the durable polling deadline.");
            source.Status = "completed";
            await worker.PollAsync(now.AddSeconds(30), default);
            check(store.All()[0] is { State: "completed", Conclusion: "failure", Cursor: "revision-2" }, "The exact terminal result is durable.");
            await worker.PollAsync(now.AddHours(1), default);
            check(source.Reads == 2, "Terminal watches never poll or execute again.");
            source.Drift = true; store.Create(scope with { RunId = 101 }, now);
            await worker.PollAsync(now, default);
            check(store.All().First(item => item.Scope.RunId == 101).State == "stale", "Branch head drift stops the watch.");
            source.Drift = false; source.Fail = true; store.Create(scope with { RunId = 102 }, now);
            await worker.PollAsync(now, default);
            var failed = store.All().First(item => item.Scope.RunId == 102);
            check(failed.Failures == 1 && failed.NextPoll == now.AddSeconds(60) && !failed.Note!.Contains("simulated"), "Transient failures back off and store a curated note.");
            await worker.PollAsync(now.AddSeconds(59), default);
            check(store.All().First(item => item.Scope.RunId == 102).Failures == 1, "Backoff prevents tight retry loops.");
            var expired = store.Create(scope with { RunId = 103 }, now.AddHours(-3));
            await worker.PollAsync(now, default);
            check(store.All().First(item => item.Id == expired.Id).State == "timed-out", "A watch deadline stops without an external action.");
            source.Fail = false; source.Status = "in_progress"; source.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = store.Create(scope with { RunId = 104 }, now);
            var polling = worker.PollAsync(now, default);
            store.Cancel(canceled.Id, now);
            source.Pending.SetResult(new(scope with { RunId = 104 }, scope.HeadSha, "completed", "success", "late-result"));
            await polling;
            check(store.All().First(item => item.Id == canceled.Id) is { State: "canceled", Cursor: null }, "Cancel wins over a late in-flight terminal result.");
            check(store.Cancel(canceled.Id, now).State == "canceled", "Cancel is idempotent and changes no remote run.");
        }
        finally { File.Delete(path); }
    }
}
