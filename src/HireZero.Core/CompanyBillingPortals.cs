namespace HireZero.Core;

public sealed record CompanyBillingPortal(
    string Id,
    string ProviderIdentity,
    string CustomerId,
    string ApprovedBy,
    string State,
    DateTimeOffset ExpiresAt,
    int Version = 1,
    BillingPortalReceipt? Receipt = null
)
{
    public string BillingKind => "portal";
}

public sealed partial class CompanyStore
{
    public CompanyBillingPortal? BillingPortal()
    {
        using var db = Open();
        return All<CompanyBillingPortal>(db).LastOrDefault();
    }

    private CompanyBillingPortal PrepareBillingPortal(StoreConnection db, CompanyCommand command)
    {
        var state = BillingState(db);
        Version(state.Version, command.ExpectedVersion);
        if (
            state.CustomerId is null
            || state.ProviderIdentity != BillingConfiguration?.ProviderIdentity
            || command.Decision is not ("open-billing" or "create-new-link")
        )
            throw new WorkspaceException("Confirm opening the billing portal for this company's current account.", 409);
        var latest = All<CompanyBillingPortal>(db).LastOrDefault();
        if (latest is { } prior && prior.ExpiresAt > Clock())
        {
            if (prior.State is "queued" or "dispatching")
                throw new WorkspaceException(
                    "The current billing link is still being prepared. Check its status.",
                    409
                );
            if (prior.State == "uncertain" && command.Decision != "create-new-link")
                throw new WorkspaceException(
                    "The earlier link could not be confirmed. Explicitly request a new link.",
                    409
                );
            if (prior.State == "open")
                return prior;
        }
        var portal = new CompanyBillingPortal(
            Guid.NewGuid().ToString("N"),
            state.ProviderIdentity!,
            state.CustomerId,
            CurrentActor.Value!.Subject,
            "queued",
            Clock().AddMinutes(5)
        );
        Save(db, portal, portal.Id, state: portal.State, version: portal.Version);
        Audit(db, "billing.portal-approved", portal.Id, new { portal.ExpiresAt });
        return portal;
    }

    public CompanyBillingPortal[] QueuedBillingPortals()
    {
        using var db = Open();
        return Rows(
            db,
            "SELECT payload FROM co_billing_portals WHERE company_id=$0 AND state='queued'",
            r => CompanyJson.Decode<CompanyBillingPortal>(r.GetString(0)),
            CompanyId
        );
    }

    private bool PortalAuthority(StoreConnection db, CompanyBillingPortal portal) =>
        portal.ProviderIdentity == BillingConfiguration?.ProviderIdentity
        && portal.CustomerId == BillingState(db).CustomerId
        && portal.ExpiresAt > Clock()
        && Find<CompanyMember>(db, portal.ApprovedBy) is { State: "active", Role: CompanyRoles.Owner };

    public CompanyBillingPortal? TakeBillingPortal(string id, string provider)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var portal = Need<CompanyBillingPortal>(db, id);
                if (portal.State != "queued" || portal.ProviderIdentity != provider)
                    return null;
                var allowed = PortalAuthority(db, portal);
                portal = portal with { State = allowed ? "dispatching" : "failed", Version = portal.Version + 1 };
                Save(db, portal, portal.Id, state: portal.State, version: portal.Version);
                return allowed ? portal : null;
            }
        );
    }

    public bool BillingPortalAuthorized(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () => Need<CompanyBillingPortal>(db, id) is { State: "dispatching" } portal && PortalAuthority(db, portal)
        );
    }

    public void RecordBillingPortal(string id, BillingPortalReceipt? receipt, bool unknown = false)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var portal = Need<CompanyBillingPortal>(db, id);
                if (portal.State != "dispatching")
                    return false;
                if (receipt is not null && receipt.CustomerId != portal.CustomerId)
                    throw new WorkspaceException("The billing link belongs to another customer.", 409);
                portal = portal with
                {
                    State =
                        receipt is not null ? "open"
                        : unknown ? "uncertain"
                        : "failed",
                    Receipt = receipt,
                    Version = portal.Version + 1,
                };
                Save(db, portal, portal.Id, state: portal.State, version: portal.Version);
                return true;
            }
        );
    }

    public void RecoverBillingPortals()
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                foreach (var portal in All<CompanyBillingPortal>(db).Where(p => p.State == "dispatching"))
                    Save(
                        db,
                        portal with
                        {
                            State = "uncertain",
                            Version = portal.Version + 1,
                        },
                        portal.Id,
                        state: "uncertain",
                        version: portal.Version + 1
                    );
                return true;
            }
        );
    }
}
