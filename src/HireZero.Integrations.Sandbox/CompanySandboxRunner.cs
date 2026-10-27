namespace HireZero.Integrations.Sandbox;

// Composition belongs to the integration: Core owns authority and durable receipts; only the executor touches
// infrastructure. Call recovery before accepting work at startup, never by replaying an earlier run command.
public sealed class CompanySandboxRunner(CompanyStore store, ISandboxExecutor executor)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CompanySandboxJob> RunAsync(string id, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var job = store.TakeSandboxDispatch(id, executor.Identity);
            if (job is null)
            {
                var existing = store.SandboxJob(id);
                return existing.StartedAt is not null && !existing.DispatchTaken
                    ? await RecoverUnderGateAsync(id, token)
                    : existing;
            }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var watcher = WatchAuthorityAsync(id, cancellation);
            SandboxResult result;
            try
            {
                result = await executor.ExecuteAsync(
                    job.Request,
                    () => store.SandboxAuthorized(id),
                    cancellation.Token
                );
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // The adapter might have provisioned a resource before throwing. Absence is not a receipt.
                // RecoverAsync supplies the adapter's canonical resource reference before any release is attempted.
                return await RecoverUnderGateAsync(id, CancellationToken.None);
            }
            finally
            {
                await cancellation.CancelAsync();
                await watcher;
            }
            // Never move release above this durable write: a crash must leave either saved output or engine logs.
            job = store.RecordSandboxResult(id, executor.Identity, result);
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
                if (!store.SandboxAuthorized(id))
                    await cancellation.CancelAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Losing the authority store is a reason to stop, never permission to continue.
            await cancellation.CancelAsync();
        }
    }

    public async Task<CompanySandboxJob> RecoverAsync(string id, CancellationToken token)
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

    private async Task<CompanySandboxJob> RecoverUnderGateAsync(string id, CancellationToken token)
    {
        var job = store.SandboxJob(id);
        if (job.RuntimeIdentity != executor.Identity || job.StartedAt is null || job.SettledAt is not null)
            return job;
        job = store.FenceSandboxRecovery(id);
        if (job.Result is null || job.Result.State == "unknown")
        {
            if (!job.DispatchTaken)
            {
                // A dispatch never left the database. Fence won the transaction before a worker could claim it.
                var noResource = new SandboxRelease(true, "not-created", "unsent:" + job.Id);
                job = store.RecordSandboxResult(
                    id,
                    executor.Identity,
                    new("not-sent", null, "", "", [], 0, noResource, "dispatch-not-taken")
                );
            }
            else
            {
                SandboxResult receipt;
                try
                {
                    receipt = await executor.RecoverAsync(job.Request, token);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    return job;
                }
                job = store.RecordSandboxResult(id, executor.Identity, receipt);
            }
        }
        return await ReleaseSavedAsync(job);
    }

    private async Task<CompanySandboxJob> ReleaseSavedAsync(CompanySandboxJob job)
    {
        if (job.Result is null || job.SettledAt is not null)
            return job;
        var result = job.Result;
        if (result is { State: "not-sent", Release: { Confirmed: true, Status: "not-created" } })
            return store.RecordSandboxRelease(job.Id, executor.Identity, result.Release);
        if (result.Reason is "existing-resource" or "already-attempted" or "resource-state-unconfirmed")
            return store.RecordSandboxRelease(
                job.Id,
                executor.Identity,
                new(false, "scope-refused", result.Release.ResourceId)
            );
        SandboxRelease release;
        try
        {
            // Host cancellation cannot erase the duty to release. Give this bounded cleanup its own time window.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            release = await executor.ReleaseAsync(store.CompanyId, job.Id, result.CreationConfirmed, cleanup.Token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            release = new(false, "release-unconfirmed", result.Release.ResourceId);
        }
        return store.RecordSandboxRelease(job.Id, executor.Identity, release);
    }
}
