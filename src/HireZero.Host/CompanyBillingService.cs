using HireZero.Core;

public sealed class CompanyBillingService(
    Func<CompanyStore[]> stores,
    CompanyBillingRuntime runtime,
    ILogger<CompanyBillingService> logger
) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _nextRead = new(StringComparer.Ordinal);

    public async Task<bool> RefreshAsync(CompanyStore store, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await Refresh(store, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> Refresh(CompanyStore store, CancellationToken token)
    {
        var billing = store.Billing();
        if (billing.Checkout is not { DispatchedAt: not null } checkout)
            return false;
        _nextRead[store.CompanyId] = DateTimeOffset.UtcNow.AddMinutes(10);
        return await new CompanyBillingWorkflow(store, runtime.Provider).RefreshAsync(checkout.Id, token);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var store in stores())
            store.RecoverBillingPortals();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _gate.WaitAsync(stoppingToken);
                try
                {
                    await Sweep(stoppingToken);
                }
                finally
                {
                    _gate.Release();
                }
                await Task.Delay(750, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task Sweep(CancellationToken token)
    {
        var all = stores();
        foreach (var store in all)
        {
            foreach (var checkout in store.QueuedBillingCheckouts())
            {
                if (!runtime.PaymentsEnabled)
                    continue;
                await Attempt(
                    async () =>
                    {
                        await new CompanyBillingWorkflow(store, runtime.Provider).DispatchAsync(checkout.Id, token);
                        _nextRead[store.CompanyId] = DateTimeOffset.UtcNow.AddMinutes(10);
                    },
                    token
                );
            }
            foreach (var portal in store.QueuedBillingPortals())
                await Attempt(() => Portal(store, portal.Id, token), token);
            var view = store.Billing();
            if (
                view.Checkout?.Receipt is not null
                && view.State.Status is not ("expired" or "canceled" or "incomplete_expired")
                && (!_nextRead.TryGetValue(store.CompanyId, out var next) || next <= DateTimeOffset.UtcNow)
            )
                await Attempt(
                    async () =>
                    {
                        await Refresh(store, token);
                    },
                    token
                );
        }
        if (all.Length == 0)
            return;
        var inbox = all[0];
        for (var count = 0; count < 20 && !token.IsCancellationRequested; count++)
        {
            var delivery = inbox.TakeBillingEvent(runtime.Provider.Identity);
            if (delivery is null)
                break;
            await Event(inbox, all, delivery, token);
        }
    }

    private async Task Attempt(Func<Task> action, CancellationToken token)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            logger.LogWarning(
                "Billing needs reconciliation; the butler keeps the original receipt and does not replay a charge."
            );
        }
    }

    private async Task Portal(CompanyStore store, string id, CancellationToken token)
    {
        var portal = store.TakeBillingPortal(id, runtime.Provider.Identity);
        if (portal is null)
            return;
        try
        {
            var receipt = await runtime.Provider.CreatePortalAsync(
                new(id, portal.CustomerId, store.CompanyId),
                () => store.BillingPortalAuthorized(id),
                token
            );
            store.RecordBillingPortal(id, receipt);
        }
        catch (BillingProviderException error)
        {
            store.RecordBillingPortal(id, null, error.OutcomeUnknown);
            throw;
        }
        catch
        {
            store.RecordBillingPortal(id, null, true);
            throw;
        }
    }

    private async Task Event(
        CompanyStore inbox,
        CompanyStore[] all,
        CompanyBillingEvent delivery,
        CancellationToken token
    )
    {
        string? company = null,
            operation = null,
            session = null;
        try
        {
            var provider = runtime.Provider;
            await provider.VerifyAccountAsync(token);
            if (delivery.Notice.Type.StartsWith("checkout.session.", StringComparison.Ordinal))
            {
                var checkout = await provider.CheckoutAsync(delivery.Notice.ObjectId, token);
                company = checkout.CompanyId;
                operation = checkout.OperationId;
                session = checkout.Id;
            }
            else
            {
                var subscriptionId = delivery.Notice.Type.StartsWith("invoice.", StringComparison.Ordinal)
                    ? (await provider.InvoiceAsync(delivery.Notice.ObjectId, token)).SubscriptionId
                    : delivery.Notice.ObjectId;
                var subscription = await provider.SubscriptionAsync(subscriptionId, token);
                company = subscription.CompanyId;
                operation = subscription.OperationId;
            }
            var store = all.SingleOrDefault(s => s.CompanyId == company);
            if (store is null)
            {
                inbox.FinishBillingEvent(provider.Identity, delivery, "ignored");
                return;
            }
            CompanyBillingCheckout original;
            try
            {
                original = store.BillingCheckout(operation!);
            }
            catch (WorkspaceException e) when (e.Status == 404)
            {
                inbox.FinishBillingEvent(provider.Identity, delivery, "ignored");
                return;
            }
            if (original.ProviderIdentity != provider.Identity || original.DispatchedAt is null)
                throw new WorkspaceException("Billing event does not match an original dispatch.", 409);
            // A superseded subscription event cannot undo a newer purchase. Reconcile only the active binding.
            if (store.Billing().State.AttemptId != original.Id)
            {
                inbox.FinishBillingEvent(provider.Identity, delivery, "ignored", company, operation);
                return;
            }
            await new CompanyBillingWorkflow(store, provider).RefreshAsync(original.Id, token, session);
            _nextRead[store.CompanyId] = DateTimeOffset.UtcNow.AddMinutes(10);
            inbox.FinishBillingEvent(provider.Identity, delivery, "complete", company, operation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            inbox.FinishBillingEvent(runtime.Provider.Identity, delivery, "retry", company, operation);
            logger.LogWarning(
                "A billing notification awaits a verified receipt; the butler has kept it in the durable inbox."
            );
        }
    }
}
