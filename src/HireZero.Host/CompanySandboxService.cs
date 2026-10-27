using HireZero.Integrations.Sandbox;

// One bounded worker per host. Persistent identity and queues belong to the company database, never this loop.
public sealed class CompanySandboxService : BackgroundService
{
    private readonly Func<CompanyStore[]> _stores;
    private readonly ISandboxExecutor _executor;
    private readonly ILogger<CompanySandboxService> _logger;
    private readonly (CompanyStore Store, string Id)[] _recovery;
    private readonly SemaphoreSlim _worker = new(1, 1);

    public CompanySandboxService(
        Func<CompanyStore[]> stores,
        ISandboxExecutor executor,
        ILogger<CompanySandboxService> logger
    )
    {
        _stores = stores;
        _executor = executor;
        _logger = logger;
        // Constructed before the server accepts requests: only already-persisted jobs are startup recovery work.
        _recovery = stores().SelectMany(s => s.PendingSandboxRecovery().Select(j => (s, j.Id))).ToArray();
    }

    public async Task<CompanySandboxJob> RecoverAsync(CompanyStore store, string id, CancellationToken token)
    {
        if (store.SandboxJob(id).RuntimeIdentity != _executor.Identity)
            throw new WorkspaceException(
                "This job belongs to a different runtime. An operator must restore its original configuration before recovery.",
                409
            );
        await _worker.WaitAsync(token);
        try
        {
            return await new CompanySandboxRunner(store, _executor).RecoverAsync(id, token);
        }
        finally
        {
            _worker.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var token = lifetime.Token;
        var monitoring = MonitorAvailabilityAsync(lifetime);
        try
        {
            foreach (var (store, id) in _recovery)
                await Visit(store, id, recover: true, token);
            while (!token.IsCancellationRequested)
            {
                foreach (var store in _stores())
                foreach (var job in store.PendingSandboxRecovery().Where(j => j.State == "running" && !j.DispatchTaken))
                    await Visit(store, job.Id, recover: false, token);
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await lifetime.CancelAsync();
            await monitoring;
        }
    }

    private async Task MonitorAvailabilityAsync(CancellationTokenSource lifetime)
    {
        if (_executor is not ISandboxRuntimeAvailability qualified)
            return;
        var token = lifetime.Token;
        try
        {
            // A long errand must not leave the qualification inspector waiting outside its own door.
            while (!token.IsCancellationRequested)
            {
                await qualified.RefreshAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _logger.LogWarning("Sandbox qualification monitoring failed; the butler has stopped new errands.");
            await lifetime.CancelAsync();
            throw;
        }
    }

    private async Task Visit(CompanyStore store, string id, bool recover, CancellationToken token)
    {
        await _worker.WaitAsync(token);
        try
        {
            var runner = new CompanySandboxRunner(store, _executor);
            if (recover)
                await runner.RecoverAsync(id, token);
            else
                await runner.RunAsync(id, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            store.FenceSandboxRecovery(id);
            _logger.LogWarning("Sandbox job {Job} needs recovery; the butler will not replay an uncertain errand.", id);
        }
        finally
        {
            _worker.Release();
        }
    }
}
