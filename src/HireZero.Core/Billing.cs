namespace HireZero.Core;

// Provider receipts are evidence for a durable billing workflow, never an owner's permission or a paid entitlement.
public sealed record BillingPrice(
    string Id,
    string Currency,
    long UnitAmount,
    string Interval,
    int IntervalCount,
    bool Active
);

public sealed record BillingCheckoutRequest(
    string OperationId,
    string CompanyId,
    string PlanDigest,
    BillingPrice Price,
    string? CustomerId = null
);

public sealed record BillingCheckoutReceipt(
    string Id,
    string OperationId,
    string CompanyId,
    string PlanDigest,
    string PriceId,
    string Status,
    string PaymentStatus,
    string? CustomerId,
    string? SubscriptionId,
    string? Url,
    DateTimeOffset ExpiresAt
);

public sealed record BillingSubscriptionReceipt(
    string Id,
    string CustomerId,
    string Status,
    BillingPrice Price,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    bool CancelAtPeriodEnd,
    bool CollectionPaused,
    string? LatestInvoiceId,
    string CompanyId,
    string OperationId,
    string PlanDigest
);

public sealed record BillingInvoiceReceipt(
    string Id,
    string CustomerId,
    string SubscriptionId,
    string Status,
    string Currency,
    long AmountDue,
    long AmountPaid,
    long AmountRemaining
);

public sealed record BillingPortalRequest(string OperationId, string CustomerId, string? CompanyId = null);

public sealed record BillingPortalReceipt(string Id, string CustomerId, string Url);

public sealed record BillingEventNotice(
    string Id,
    string Type,
    string ObjectId,
    DateTimeOffset CreatedAt,
    string PayloadDigest
);

public sealed class BillingProviderException(string code, bool outcomeUnknown = false)
    : Exception(
        outcomeUnknown
            ? "The billing provider outcome is unconfirmed. Reconcile the original operation before trying again."
            : "The billing provider could not verify this request. No new billing operation was confirmed."
    )
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

public interface IBillingProvider
{
    string Identity { get; }
    Task VerifyAccountAsync(CancellationToken token);
    Task<BillingPrice> PriceAsync(string id, CancellationToken token);
    Task<BillingCheckoutReceipt> CreateCheckoutAsync(
        BillingCheckoutRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    );
    Task<BillingCheckoutReceipt> CheckoutAsync(string id, CancellationToken token);
    Task<BillingSubscriptionReceipt> SubscriptionAsync(string id, CancellationToken token);
    Task<BillingInvoiceReceipt> InvoiceAsync(string id, CancellationToken token);
    Task<BillingPortalReceipt> CreatePortalAsync(
        BillingPortalRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    );
}
