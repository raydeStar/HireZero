namespace HireZero.Core;

// Provider-neutral orchestration. Host composition owns scheduling and authorization of requests to this workflow.
public sealed class CompanyBillingWorkflow(CompanyStore store, IBillingProvider provider)
{
    public async Task<bool> DispatchAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var checkout = store.TakeBillingDispatch(id, provider.Identity);
        if (checkout is null)
            return false;
        try
        {
            var receipt = await provider.CreateCheckoutAsync(
                new(
                    checkout.Id,
                    store.CompanyId,
                    checkout.PlanDigest,
                    checkout.Plan.Price,
                    store.Billing().State.CustomerId
                ),
                () => store.BillingCheckoutAuthorized(id),
                token
            );
            return store.RecordBillingCheckout(id, receipt);
        }
        catch (BillingProviderException error)
        {
            store.BillingDispatchFailed(id, error.OutcomeUnknown);
            throw;
        }
        catch
        {
            // A process interruption after the durable claim is never proof that the provider did nothing.
            store.BillingDispatchFailed(id, true);
            throw;
        }
    }

    public async Task<bool> RefreshAsync(string id, CancellationToken token, string? recoveredSessionId = null)
    {
        token.ThrowIfCancellationRequested();
        var refresh = store.BeginBillingRefresh(id, provider.Identity);
        if (refresh is null)
            return false;
        var sessionId = refresh.Checkout.Receipt?.Id ?? recoveredSessionId;
        if (sessionId is null)
            throw new BillingProviderException("original-checkout-receipt-required");
        await provider.VerifyAccountAsync(token);
        var checkout = await provider.CheckoutAsync(sessionId, token);
        if (!store.RecordBillingCheckout(id, checkout, refresh.Id))
            return false;
        BillingSubscriptionReceipt? subscription = null;
        BillingInvoiceReceipt? invoice = null;
        if (checkout.SubscriptionId is not null)
        {
            subscription = await provider.SubscriptionAsync(checkout.SubscriptionId, token);
            store.ValidateBillingSubscription(refresh.Checkout, checkout, subscription);
            if (subscription.LatestInvoiceId is not null)
                invoice = await provider.InvoiceAsync(subscription.LatestInvoiceId, token);
            // Catch a renewal or cancellation racing the invoice read; a fresh read can safely retry later.
            if (await provider.SubscriptionAsync(subscription.Id, token) != subscription)
                throw new BillingProviderException("subscription-changed-during-verification");
        }
        return store.RecordBillingVerification(refresh, checkout, subscription, invoice);
    }
}
