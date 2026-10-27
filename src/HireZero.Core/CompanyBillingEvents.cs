namespace HireZero.Core;

public sealed record CompanyBillingEvent(
    BillingEventNotice Notice,
    string State,
    int Attempts,
    DateTimeOffset NextAt,
    string? Lease = null,
    string? CompanyId = null,
    string? CheckoutId = null
);

public sealed partial class CompanyStore
{
    private const string BillingDeliverySchema =
        "CREATE TABLE co_billing_events(provider TEXT NOT NULL,id TEXT NOT NULL,state TEXT NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(provider,id));"
        + "CREATE TABLE co_billing_portals(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    // An authenticated merchant event enters this global inbox before tenant resolution. No customer body is stored.
    public bool QueueBillingEvent(string provider, BillingEventNotice notice)
    {
        if (
            provider.Length != 64
            || !provider.All(Uri.IsHexDigit)
            || notice.Id.Length is < 1 or > 200
            || notice.ObjectId.Length is < 1 or > 200
            || notice.Type.Length is < 1 or > 120
            || notice.PayloadDigest.Length != 64
            || !notice.PayloadDigest.All(Uri.IsHexDigit)
        )
            throw new WorkspaceException("Invalid billing event identity.", 422);
        using var db = Open();
        return db.Transaction(
            "billing-events:" + provider,
            () =>
            {
                var previous = BillingEvent(db, provider, notice.Id);
                if (previous is not null)
                {
                    if (
                        previous.Notice.Type != notice.Type
                        || previous.Notice.ObjectId != notice.ObjectId
                        || previous.Notice.CreatedAt != notice.CreatedAt
                    )
                        throw new WorkspaceException("A billing event identity changed.", 409);
                    // Delivery counters and serialization may change. Identity, not byte-for-byte delivery, is deduplicated.
                    return false;
                }
                if (
                    int.Parse(
                        Scalar(
                            db,
                            "SELECT count(*) FROM co_billing_events WHERE provider=$0 AND state NOT IN ('complete','ignored')",
                            provider
                        ) ?? "0"
                    ) >= 10_000
                )
                    throw new WorkspaceException("Billing event delivery is temporarily full.", 503);
                PutBillingEvent(db, provider, new(notice, "pending", 0, Clock()));
                return true;
            }
        );
    }

    private static CompanyBillingEvent? BillingEvent(StoreConnection db, string provider, string id) =>
        Rows(
                db,
                "SELECT payload FROM co_billing_events WHERE provider=$0 AND id=$1",
                r => CompanyJson.Decode<CompanyBillingEvent>(r.GetString(0)),
                provider,
                id
            )
            .SingleOrDefault();

    private static void PutBillingEvent(StoreConnection db, string provider, CompanyBillingEvent value) =>
        Sql(
            db,
            "INSERT INTO co_billing_events(provider,id,state,payload) VALUES($0,$1,$2,$3) ON CONFLICT(provider,id) DO UPDATE SET state=excluded.state,payload=excluded.payload",
            provider,
            value.Notice.Id,
            value.State,
            CompanyJson.Encode(value)
        );

    public CompanyBillingEvent? TakeBillingEvent(string provider)
    {
        using var db = Open();
        return db.Transaction(
            "billing-events:" + provider,
            () =>
            {
                var due = Rows(
                        db,
                        "SELECT payload FROM co_billing_events WHERE provider=$0 AND state IN ('pending','retry','processing')",
                        r => CompanyJson.Decode<CompanyBillingEvent>(r.GetString(0)),
                        provider
                    )
                    .Where(e => e.NextAt <= Clock())
                    .OrderBy(e => e.NextAt)
                    .ThenBy(e => e.Notice.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (due is null)
                    return null;
                var taken = due with
                {
                    State = "processing",
                    Lease = Guid.NewGuid().ToString("N"),
                    Attempts = due.Attempts + 1,
                    NextAt = Clock().AddMinutes(5),
                };
                PutBillingEvent(db, provider, taken);
                return taken;
            }
        );
    }

    public bool FinishBillingEvent(
        string provider,
        CompanyBillingEvent taken,
        string outcome,
        string? company = null,
        string? checkout = null
    )
    {
        if (outcome is not ("complete" or "ignored" or "retry"))
            throw new ArgumentException("Choose a billing event outcome.");
        using var db = Open();
        return db.Transaction(
            "billing-events:" + provider,
            () =>
            {
                var current = BillingEvent(db, provider, taken.Notice.Id);
                if (current is not { State: "processing" } || current.Lease != taken.Lease)
                    return false;
                PutBillingEvent(
                    db,
                    provider,
                    current with
                    {
                        State = outcome == "retry" && current.Attempts >= 12 ? "needs-review" : outcome,
                        NextAt = Clock().AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(current.Attempts - 1, 6)))),
                        Lease = null,
                        CompanyId = company ?? current.CompanyId,
                        CheckoutId = checkout ?? current.CheckoutId,
                    }
                );
                return true;
            }
        );
    }

    public CompanyBillingEvent[] BillingEventStatus(string provider)
    {
        using var db = Open();
        return Rows(
            db,
            "SELECT payload FROM co_billing_events WHERE provider=$0",
            r => CompanyJson.Decode<CompanyBillingEvent>(r.GetString(0)),
            provider
        );
    }

    public CompanyBillingCheckout[] QueuedBillingCheckouts()
    {
        using var db = Open();
        return Rows(
            db,
            "SELECT payload FROM co_billing_checkouts WHERE company_id=$0 AND state='queued'",
            r => CompanyJson.Decode<CompanyBillingCheckout>(r.GetString(0)),
            CompanyId
        );
    }
}
