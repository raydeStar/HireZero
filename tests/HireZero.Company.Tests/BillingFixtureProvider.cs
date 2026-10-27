using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Stripe;

// Compiled test fixture only. It creates fictional receipts and never constructs a network client.
public sealed class BillingFixtureProvider : IBillingProvider
{
    public const string SigningSecret = "whsec_fictional_billing_signature";
    public static readonly BillingPlan Plan = new(
        "fictional",
        "Fictional Studio",
        new("price_fictional", "usd", 1900, "month", 1, true),
        new(3, 42_000, 2, 60)
    );
    public static readonly BillingPlan ExtendedPlan = new(
        "fictional-extended",
        "Fictional Extended",
        new("price_fictional_extended", "usd", 4900, "month", 1, true),
        new(3, 42_000, 3, 1800, 600)
    );
    public BillingPlan SelectedPlan { get; init; } = Plan;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
    public string Identity { get; } = CompanyJson.Digest(new { Provider = "fictional-billing", Mode = "test" });
    public int Creates,
        Portals,
        Reads;
    public bool LoseNextCheckoutReply,
        LoseNextPortalReply,
        RefuseReads;
    private readonly ConcurrentDictionary<
        string,
        (BillingCheckoutReceipt Checkout, BillingSubscriptionReceipt? Subscription, BillingInvoiceReceipt? Invoice)
    > _items = new();

    public CompanyBillingRuntime Runtime(bool enabled = true) =>
        new(
            this,
            new(Identity, [SelectedPlan]),
            (body, signature, now) => StripeWebhook.Verify(body, signature, [SigningSecret], false, now),
            enabled,
            false
        );

    public Task VerifyAccountAsync(CancellationToken token)
    {
        Read(token);
        return Task.CompletedTask;
    }

    public Task<BillingPrice> PriceAsync(string id, CancellationToken token)
    {
        Read(token);
        return Task.FromResult(SelectedPlan.Price);
    }

    private void Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref Reads);
        if (RefuseReads)
            throw new BillingProviderException("fictional-read-unavailable");
    }

    public Task<BillingCheckoutReceipt> CreateCheckoutAsync(
        BillingCheckoutRequest request,
        Func<bool> authorized,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        if (!authorized())
            throw new BillingProviderException("authority-withdrawn");
        Interlocked.Increment(ref Creates);
        var receipt = new BillingCheckoutReceipt(
            "cs_" + request.OperationId,
            request.OperationId,
            request.CompanyId,
            request.PlanDigest,
            request.Price.Id,
            "open",
            "unpaid",
            request.CustomerId,
            null,
            "https://checkout.stripe.com/c/pay/cs_" + request.OperationId,
            Clock().AddHours(24)
        );
        if (!_items.TryAdd(request.OperationId, (receipt, null, null)))
            throw new InvalidOperationException("The fictional butler caught a repeated checkout dispatch.");
        if (LoseNextCheckoutReply)
        {
            LoseNextCheckoutReply = false;
            throw new BillingProviderException("fictional-lost-checkout-reply", true);
        }
        return Task.FromResult(receipt);
    }

    public Task<BillingCheckoutReceipt> CheckoutAsync(string id, CancellationToken token)
    {
        Read(token);
        return Task.FromResult(_items.Values.Single(i => i.Checkout.Id == id).Checkout);
    }

    public Task<BillingSubscriptionReceipt> SubscriptionAsync(string id, CancellationToken token)
    {
        Read(token);
        return Task.FromResult(_items.Values.Single(i => i.Subscription?.Id == id).Subscription!);
    }

    public Task<BillingInvoiceReceipt> InvoiceAsync(string id, CancellationToken token)
    {
        Read(token);
        return Task.FromResult(_items.Values.Single(i => i.Invoice?.Id == id).Invoice!);
    }

    public Task<BillingPortalReceipt> CreatePortalAsync(
        BillingPortalRequest request,
        Func<bool> authorized,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        if (!authorized())
            throw new BillingProviderException("authority-withdrawn");
        var bound = _items.Values.Any(i =>
            i.Checkout.CustomerId == request.CustomerId && i.Checkout.CompanyId == request.CompanyId
        );
        if (!bound)
            throw new InvalidOperationException("The fictional billing portal belongs to another company.");
        Interlocked.Increment(ref Portals);
        if (LoseNextPortalReply)
        {
            LoseNextPortalReply = false;
            throw new BillingProviderException("fictional-lost-portal-reply", true);
        }
        return Task.FromResult(
            new BillingPortalReceipt(
                "bps_" + request.OperationId,
                request.CustomerId,
                "https://billing.stripe.com/p/session/" + request.OperationId
            )
        );
    }

    public void Change(string operation, string status)
    {
        var current = _items[operation];
        var customer = current.Checkout.CustomerId ?? "cus_" + current.Checkout.CompanyId.Replace('-', '_');
        var sub = "sub_" + operation;
        var checkout = current.Checkout with
        {
            CustomerId = customer,
            SubscriptionId = sub,
            Status = "complete",
            PaymentStatus = status == "paid" ? "paid" : "unpaid",
            Url = null,
        };
        var subscription = new BillingSubscriptionReceipt(
            sub,
            customer,
            status == "paid" ? "active" : status,
            SelectedPlan.Price,
            Clock().AddHours(-1),
            Clock().AddDays(29),
            false,
            false,
            "in_" + operation,
            checkout.CompanyId,
            operation,
            checkout.PlanDigest
        );
        var invoice = new BillingInvoiceReceipt(
            "in_" + operation,
            customer,
            sub,
            status == "paid" ? "paid" : "open",
            SelectedPlan.Price.Currency,
            SelectedPlan.Price.UnitAmount,
            status == "paid" ? SelectedPlan.Price.UnitAmount : 0,
            status == "paid" ? 0 : SelectedPlan.Price.UnitAmount
        );
        _items[operation] = (checkout, subscription, invoice);
    }

    public (string Body, string Signature) Event(
        string operation,
        string type = "checkout.session.completed",
        string? eventId = null
    )
    {
        var current = _items[operation];
        var checkout = type.StartsWith("checkout.", StringComparison.Ordinal);
        var invoice = type.StartsWith("invoice.", StringComparison.Ordinal);
        var now = Clock();
        var body = CompanyJson.Encode(
            new
            {
                id = eventId ?? "evt_" + Guid.NewGuid().ToString("N"),
                @object = "event",
                api_version = StripeBilling.ApiVersion,
                livemode = false,
                created = now.ToUnixTimeSeconds(),
                type,
                data = new
                {
                    @object = new
                    {
                        id = checkout ? current.Checkout.Id
                        : invoice ? current.Invoice!.Id
                        : current.Subscription!.Id,
                        @object = checkout ? "checkout.session"
                        : invoice ? "invoice"
                        : "subscription",
                        livemode = false,
                    },
                },
            }
        );
        return (body, Sign(body, now));
    }

    public static string Sign(string body, DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "t="
            + timestamp
            + ",v1="
            + Convert.ToHexStringLower(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(SigningSecret),
                    Encoding.UTF8.GetBytes(timestamp + "." + body)
                )
            );
    }
}
