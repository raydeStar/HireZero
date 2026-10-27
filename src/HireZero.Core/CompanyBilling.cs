namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private CompanyBillingConfiguration? _billingConfiguration;
    public CompanyBillingConfiguration? BillingConfiguration
    {
        get => _billingConfiguration;
        init
        {
            _billingConfiguration = value;
            if (value is null)
                return;
            using var db = Open();
            Transaction(
                db,
                () =>
                {
                    var state = BillingState(db);
                    if (!state.Required)
                    {
                        SaveBilling(
                            db,
                            state with
                            {
                                Required = true,
                                Status = "subscription-required",
                                Version = state.Version + 1,
                            }
                        );
                        Audit(db, "billing.required", state.Id, new { value.ProviderIdentity });
                    }
                    return true;
                }
            );
        }
    }
    private const string BillingSchema =
        "CREATE TABLE co_billing_checkouts(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));"
        + "CREATE TABLE co_billing_state(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));"
        + "CREATE TABLE co_billing_objects(provider TEXT NOT NULL,kind TEXT NOT NULL,id TEXT NOT NULL,company_id TEXT NOT NULL,operation_id TEXT NOT NULL,PRIMARY KEY(provider,kind,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    private CompanyBillingState BillingState(StoreConnection db) =>
        Find<CompanyBillingState>(db, "subscription") ?? new();

    private void SaveBilling(StoreConnection db, CompanyBillingState state) =>
        Save(db, state, state.Id, state: state.Status, version: state.Version);

    private void SaveBilling(StoreConnection db, CompanyBillingCheckout checkout) =>
        Save(db, checkout, checkout.Id, state: checkout.State, version: checkout.Version);

    private bool BillingRequired(StoreConnection db) => BillingConfiguration is not null || BillingState(db).Required;

    private BillingAllowance? PaidAllowance(StoreConnection db)
    {
        var state = BillingState(db);
        var now = Clock();
        return
            state.Status == "active"
            && state.VerifiedAt <= now
            && state.ValidUntil > now
            && Find<CompanyBillingCheckout>(db, state.AttemptId) is { } checkout
            && BillingConfiguration?.Current(checkout) == true
            ? checkout.Plan.Allowance
            : null;
    }

    public CompanyBillingView Billing()
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var state = BillingState(db);
                return new CompanyBillingView(
                    BillingRequired(db),
                    BillingConfiguration is not null,
                    state,
                    BillingConfiguration?.Plans.Values.OrderBy(p => p.Id).ToArray() ?? [],
                    Find<CompanyBillingCheckout>(db, state.AttemptId),
                    PaidAllowance(db)
                );
            }
        );
    }

    public CompanyBillingCheckout BillingCheckout(string id)
    {
        using var db = Open();
        return Need<CompanyBillingCheckout>(db, id);
    }

    public bool IsBillingOperation(string id)
    {
        using var db = Open();
        return Scalar(
            db,
            "SELECT json_extract(result,'$.billingKind') FROM co_operations WHERE company_id=$0 AND id=$1",
            CompanyId,
            id
        )
            is not null;
    }

    private CompanyBillingCheckout PrepareBillingCheckout(StoreConnection db, CompanyCommand command)
    {
        var config = BillingConfiguration ?? throw new WorkspaceException("Billing plans are not configured.", 409);
        if (!config.Plans.TryGetValue(command.Scope ?? "", out var plan))
            throw new WorkspaceException("Choose a currently configured billing plan.", 422);
        var id = Guid.NewGuid().ToString("N");
        var expires = Clock().AddMinutes(30);
        var planDigest = config.Digest(plan);
        var digest = CompanyJson.Digest(
            new
            {
                CompanyId,
                Id = id,
                PlanDigest = planDigest,
                ExpiresAt = expires,
            }
        );
        var checkout = new CompanyBillingCheckout(
            id,
            config.ProviderIdentity,
            plan,
            planDigest,
            digest,
            "prepared",
            expires
        );
        SaveBilling(db, checkout);
        return checkout;
    }

    private CompanyBillingCheckout ApproveBillingCheckout(StoreConnection db, CompanyCommand command)
    {
        var checkout = Need<CompanyBillingCheckout>(db, command.Id);
        Version(checkout.Version, command.ExpectedVersion);
        if (
            checkout.State != "prepared"
            || command.Digest != checkout.Digest
            || command.Decision != "approve-subscription"
            || checkout.ExpiresAt <= Clock()
            || BillingConfiguration?.Current(checkout) != true
        )
            throw new WorkspaceException("Review and approve this exact subscription price and allowance again.", 409);
        var current = BillingState(db);
        // One unresolved subscription per company. A second checkout is not a recovery strategy, however tempting.
        if (
            current.AttemptId is not null
            && current.Status is not ("checkout-failed" or "expired" or "canceled" or "incomplete_expired")
        )
            throw new WorkspaceException("Resolve the current checkout or subscription before starting another.", 409);
        if (current.ProviderIdentity is not null && current.ProviderIdentity != checkout.ProviderIdentity)
            throw new WorkspaceException(
                "The original billing account must be reconciled before changing providers.",
                409
            );
        checkout = checkout with
        {
            State = "queued",
            ApprovedBy = CurrentActor.Value!.Subject,
            Version = checkout.Version + 1,
        };
        SaveBilling(db, checkout);
        SaveBilling(
            db,
            current with
            {
                Required = true,
                AttemptId = checkout.Id,
                ProviderIdentity = checkout.ProviderIdentity,
                SubscriptionId = null,
                Status = "queued",
                RefreshId = null,
                VerifiedAt = null,
                ValidUntil = null,
                PeriodEnd = null,
                CancelAtPeriodEnd = false,
                Version = current.Version + 1,
            }
        );
        Audit(
            db,
            "billing.approved",
            checkout.Id,
            new
            {
                checkout.PlanDigest,
                checkout.Digest,
                checkout.Version,
            }
        );
        return checkout;
    }

    private bool BillingAuthority(StoreConnection db, CompanyBillingCheckout checkout) =>
        BillingConfiguration?.Current(checkout) == true
        && checkout.ExpiresAt > Clock()
        && checkout.ApprovedBy is { } owner
        && Find<CompanyMember>(db, owner) is { State: "active", Role: CompanyRoles.Owner }
        && BillingState(db).AttemptId == checkout.Id;

    public CompanyBillingCheckout? TakeBillingDispatch(string id, string providerIdentity)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var checkout = Need<CompanyBillingCheckout>(db, id);
                if (checkout.State != "queued" || checkout.ProviderIdentity != providerIdentity)
                    return null;
                if (!BillingAuthority(db, checkout))
                {
                    BillingDispatchFailed(db, checkout, false);
                    return null;
                }
                checkout = checkout with
                {
                    State = "dispatching",
                    DispatchedAt = Clock(),
                    Version = checkout.Version + 1,
                };
                SaveBilling(db, checkout);
                var state = BillingState(db);
                SaveBilling(db, state with { Status = "dispatching", Version = state.Version + 1 });
                return checkout;
            }
        );
    }

    public bool BillingCheckoutAuthorized(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var checkout = Need<CompanyBillingCheckout>(db, id);
                return checkout.State == "dispatching" && BillingAuthority(db, checkout);
            }
        );
    }

    public void BillingDispatchFailed(string id, bool outcomeUnknown)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                BillingDispatchFailed(db, Need<CompanyBillingCheckout>(db, id), outcomeUnknown);
                return true;
            }
        );
    }

    private void BillingDispatchFailed(StoreConnection db, CompanyBillingCheckout checkout, bool unknown)
    {
        if (checkout.State is not ("queued" or "dispatching"))
            return;
        checkout = checkout with { State = unknown ? "uncertain" : "failed", Version = checkout.Version + 1 };
        SaveBilling(db, checkout);
        var state = BillingState(db);
        if (state.AttemptId == checkout.Id)
            SaveBilling(
                db,
                state with
                {
                    Status = unknown ? "unconfirmed" : "checkout-failed",
                    ValidUntil = null,
                    Version = state.Version + 1,
                }
            );
        Audit(db, "billing.dispatch-result", checkout.Id, new { OutcomeUnknown = unknown });
    }

    private void ClaimBillingObject(StoreConnection db, CompanyBillingCheckout checkout, string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new WorkspaceException("The billing object identifier is invalid.", 409);
        var operation = kind == "customer" ? "" : checkout.Id;
        Sql(
            db,
            "INSERT INTO co_billing_objects(provider,kind,id,company_id,operation_id) VALUES($0,$1,$2,$3,$4) ON CONFLICT(provider,kind,id) DO NOTHING",
            checkout.ProviderIdentity,
            kind,
            id,
            CompanyId,
            operation
        );
        if (
            Scalar(
                db,
                "SELECT company_id FROM co_billing_objects WHERE provider=$0 AND kind=$1 AND id=$2 AND operation_id=$3",
                checkout.ProviderIdentity,
                kind,
                id,
                operation
            ) != CompanyId
        )
            throw new WorkspaceException("This billing object belongs to another company or checkout.", 409);
    }

    private void ValidateBillingCheckout(
        CompanyBillingCheckout checkout,
        BillingCheckoutReceipt receipt,
        string? customer
    )
    {
        if (
            receipt.CompanyId != CompanyId
            || receipt.OperationId != checkout.Id
            || receipt.PlanDigest != checkout.PlanDigest
            || receipt.PriceId != checkout.Plan.Price.Id
            || customer is not null && customer != receipt.CustomerId
            || checkout.Receipt is { } original && original.Id != receipt.Id
            || receipt.Status is not ("open" or "complete" or "expired")
            || receipt.PaymentStatus is not ("paid" or "unpaid" or "no_payment_required")
        )
            throw new WorkspaceException("The checkout receipt does not match this company's approved plan.", 409);
    }

    // Trusted provider workflow only; never bind a browser-supplied receipt directly to these methods.
    public bool RecordBillingCheckout(string id, BillingCheckoutReceipt receipt, string? refreshId = null)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var checkout = Need<CompanyBillingCheckout>(db, id);
                var state = BillingState(db);
                if (
                    state.AttemptId != id
                    || checkout.DispatchedAt is null
                    || checkout.State == "failed"
                    || refreshId is null && checkout.State is not ("dispatching" or "uncertain")
                    || refreshId is not null && state.RefreshId != refreshId
                )
                    return false;
                ValidateBillingCheckout(checkout, receipt, state.CustomerId);
                if (state.SubscriptionId is not null && state.SubscriptionId != receipt.SubscriptionId)
                    throw new WorkspaceException("The original checkout's subscription binding changed.", 409);
                ClaimBillingObject(db, checkout, "checkout", receipt.Id);
                if (receipt.CustomerId is not null)
                    ClaimBillingObject(db, checkout, "customer", receipt.CustomerId);
                if (receipt.SubscriptionId is not null)
                    ClaimBillingObject(db, checkout, "subscription", receipt.SubscriptionId);
                SaveBilling(
                    db,
                    checkout with
                    {
                        State = receipt.Status,
                        Receipt = receipt,
                        Version = checkout.Version + 1,
                    }
                );
                SaveBilling(
                    db,
                    state with
                    {
                        CustomerId = receipt.CustomerId ?? state.CustomerId,
                        SubscriptionId = receipt.SubscriptionId ?? state.SubscriptionId,
                        RefreshId = refreshId is null ? null : state.RefreshId,
                        Status =
                            refreshId is not null ? "checking"
                            : receipt.Status == "expired" ? "expired"
                            : "unverified",
                        ValidUntil = null,
                        Version = state.Version + 1,
                    }
                );
                return true;
            }
        );
    }

    public CompanyBillingRefresh? BeginBillingRefresh(string id, string providerIdentity)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var checkout = Need<CompanyBillingCheckout>(db, id);
                var state = BillingState(db);
                if (
                    checkout.DispatchedAt is null
                    || checkout.State == "failed"
                    || checkout.ProviderIdentity != providerIdentity
                    || state.AttemptId != id
                )
                    return null;
                var refresh = new CompanyBillingRefresh(Guid.NewGuid().ToString("N"), checkout, state.CustomerId);
                // Latest refresh wins. Any failed read leaves admission closed; an older in-flight reply cannot reopen it.
                SaveBilling(
                    db,
                    state with
                    {
                        RefreshId = refresh.Id,
                        Status = "checking",
                        ValidUntil = null,
                        Version = state.Version + 1,
                    }
                );
                return refresh;
            }
        );
    }

    public void ValidateBillingSubscription(
        CompanyBillingCheckout checkout,
        BillingCheckoutReceipt receipt,
        BillingSubscriptionReceipt subscription
    )
    {
        if (
            receipt.SubscriptionId != subscription.Id
            || receipt.CustomerId != subscription.CustomerId
            || subscription.CompanyId != CompanyId
            || subscription.OperationId != checkout.Id
            || subscription.PlanDigest != checkout.PlanDigest
            || subscription.Price with { Active = true } != checkout.Plan.Price
            || subscription.PeriodEnd <= subscription.PeriodStart
        )
            throw new WorkspaceException("The subscription does not match this company's approved checkout.", 409);
    }

    public bool RecordBillingVerification(
        CompanyBillingRefresh refresh,
        BillingCheckoutReceipt receipt,
        BillingSubscriptionReceipt? subscription,
        BillingInvoiceReceipt? invoice
    )
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var checkout = Need<CompanyBillingCheckout>(db, refresh.Checkout.Id);
                var state = BillingState(db);
                if (state.AttemptId != checkout.Id || state.RefreshId != refresh.Id)
                    return false;
                ValidateBillingCheckout(checkout, receipt, state.CustomerId);
                if (checkout.Receipt != receipt)
                    throw new WorkspaceException(
                        "Verify the current canonical checkout before subscription admission.",
                        409
                    );
                if (subscription is not null)
                    ValidateBillingSubscription(checkout, receipt, subscription);
                if (receipt.SubscriptionId is not null && subscription is null)
                    throw new WorkspaceException("The subscription receipt is required.", 409);
                if (
                    invoice is not null
                    && (
                        subscription is null
                        || invoice.Id != subscription.LatestInvoiceId
                        || invoice.CustomerId != subscription.CustomerId
                        || invoice.SubscriptionId != subscription.Id
                        || invoice.Currency != checkout.Plan.Price.Currency
                        || invoice.AmountDue < 0
                        || invoice.AmountPaid < 0
                        || invoice.AmountRemaining < 0
                    )
                )
                    throw new WorkspaceException("The invoice does not match the current subscription.", 409);
                var now = Clock();
                var paid =
                    receipt.Status == "complete"
                    && subscription is { Status: "active", CollectionPaused: false }
                    && subscription.PeriodStart <= now
                    && subscription.PeriodEnd > now
                    && invoice is { Status: "paid", AmountRemaining: 0 }
                    && BillingConfiguration?.Current(checkout) == true;
                var status = subscription is not null
                    ? subscription.Status == "active" && !paid
                        ? "payment-unverified"
                        : subscription.Status
                    : receipt.Status == "expired"
                        ? "expired"
                        : "checkout-open";
                var until = paid
                    ? (subscription!.PeriodEnd < now.AddHours(1) ? subscription.PeriodEnd : now.AddHours(1))
                    : (DateTimeOffset?)null;
                state = state with
                {
                    Status = status,
                    RefreshId = null,
                    VerifiedAt = now,
                    ValidUntil = until,
                    PeriodEnd = subscription?.PeriodEnd,
                    CancelAtPeriodEnd = subscription?.CancelAtPeriodEnd ?? false,
                    Version = state.Version + 1,
                };
                SaveBilling(db, state);
                Audit(
                    db,
                    "billing.verified",
                    checkout.Id,
                    new
                    {
                        state.Status,
                        state.ValidUntil,
                        state.PeriodEnd,
                        state.CancelAtPeriodEnd,
                        checkout.PlanDigest,
                    }
                );
                return true;
            }
        );
    }

    private void DemandBillingUsage(StoreConnection db, CompanyUsageSummary usage, long tokens)
    {
        if (!BillingRequired(db))
            return;
        var allowance = PaidAllowance(db);
        if (
            allowance is null
            || usage.Jobs + usage.CarriedJobs >= allowance.Jobs
            || usage.Tokens + usage.HeldTokens + usage.CarriedTokens + tokens > allowance.Tokens
        )
            throw new WorkspaceException(
                "The subscription allowance is unavailable or exhausted. Open Billing to verify payment and included usage.",
                409
            );
    }

    private void DemandBillingCompute(StoreConnection db, CompanyComputeUsage usage, int seconds)
    {
        if (!BillingRequired(db))
            return;
        var allowance = PaidAllowance(db);
        if (
            allowance is null
            || usage.Jobs + usage.CarriedJobs >= allowance.ComputeJobs
            || usage.AllocatedSeconds + usage.HeldSeconds + usage.CarriedSeconds + seconds > allowance.ComputeSeconds
        )
            throw new WorkspaceException(
                "The subscription compute allowance is unavailable or exhausted. Open Billing to verify included compute.",
                409
            );
    }
}
