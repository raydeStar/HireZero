namespace HireZero.Integrations.Sandbox;

// Durable authority belongs to Core. Infrastructure belongs here. Neither gets a second bite at an uncertain visit.
public sealed class CompanyBrowserRunner(CompanyStore store, IBrowserExecutor executor)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CompanyBrowserJob> RunAsync(string id, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var job = store.TakeBrowserDispatch(id, executor.Identity);
            if (job is null)
            {
                var existing = store.BrowserJob(id);
                return existing.StartedAt is not null && !existing.DispatchTaken
                    ? await RecoverUnderGateAsync(id, token)
                    : existing;
            }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var watcher = WatchAuthorityAsync(id, cancellation);
            BrowserExecution receipt;
            try
            {
                receipt = await executor.ExecuteAsync(
                    job.Request,
                    () => store.BrowserAuthorized(id),
                    cancellation.Token
                );
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                return await RecoverUnderGateAsync(id, CancellationToken.None);
            }
            finally
            {
                await cancellation.CancelAsync();
                await watcher;
            }
            // Persist captures before removing engine logs. A crash must leave one recoverable copy.
            job = store.RecordBrowserResult(id, executor.Identity, receipt);
            return await ReleaseSavedAsync(job);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WatchAuthorityAsync(string id, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(200, cancellation.Token);
                if (!store.BrowserAuthorized(id))
                    await cancellation.CancelAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await cancellation.CancelAsync();
        }
    }

    public async Task<CompanyBrowserJob> RecoverAsync(string id, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await RecoverUnderGateAsync(id, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CompanyBrowserJob> RecoverUnderGateAsync(string id, CancellationToken token)
    {
        var job = store.BrowserJob(id);
        if (job.RuntimeIdentity != executor.Identity || job.StartedAt is null || job.SettledAt is not null)
            return job;
        job = store.FenceBrowserRecovery(id);
        if (job.Execution is null || job.Execution.State is "unknown" or "not-sent" && !job.Execution.Release.Confirmed)
        {
            if (!job.DispatchTaken)
            {
                // The transaction fenced this job before a dispatch escaped. No external absence inference is needed.
                job = store.RecordBrowserResult(
                    id,
                    executor.Identity,
                    new("not-sent", null, null, 0, new(true, "not-created", "unsent:" + job.Id), "dispatch-not-taken")
                );
            }
            else
            {
                BrowserExecution receipt;
                try
                {
                    receipt = await executor.RecoverAsync(job.Request, token);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    return job;
                }
                job = store.RecordBrowserResult(id, executor.Identity, receipt);
            }
        }
        return await ReleaseSavedAsync(job);
    }

    private async Task<CompanyBrowserJob> ReleaseSavedAsync(CompanyBrowserJob job)
    {
        if (job.Execution is not { } receipt || job.SettledAt is not null)
            return job;
        if (receipt is { State: "not-sent", Release: { Confirmed: true, Status: "not-created" } })
            return store.RecordBrowserRelease(job.Id, executor.Identity, receipt.Release);
        if (receipt.Reason is "existing-resource" or "already-attempted" or "resource-state-unconfirmed")
            return store.RecordBrowserRelease(
                job.Id,
                executor.Identity,
                new(false, "scope-refused", receipt.Release.ResourceId)
            );
        SandboxRelease release;
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            release = await executor.ReleaseAsync(
                job.Request,
                receipt.Release.ResourceId,
                receipt.CreationConfirmed,
                cleanup.Token
            );
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            release = new(false, "release-unconfirmed", receipt.Release.ResourceId);
        }
        return store.RecordBrowserRelease(job.Id, executor.Identity, release);
    }
}
