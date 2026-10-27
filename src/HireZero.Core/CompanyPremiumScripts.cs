namespace HireZero.Core;

public sealed record CompanyScriptTiers(
    bool ExtendedRuntime,
    int? ExtendedMaximumSeconds,
    string Status,
    string? PlanDigest = null
);

public sealed partial class CompanyStore
{
    public Func<bool> ExtendedSandboxAvailable { get; init; } = () => false;

    private CompanySandboxPremium? PaidScriptEntitlement(StoreConnection db)
    {
        var allowance = PaidAllowance(db);
        if (allowance?.ExtendedScriptSeconds is not { } maximum)
            return null;
        var checkout = Find<CompanyBillingCheckout>(db, BillingState(db).AttemptId);
        return checkout is null ? null : new(checkout.PlanDigest, maximum);
    }

    public CompanyScriptTiers ScriptTiers()
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var runtime = ExtendedSandboxAvailable();
                var entitlement = PaidScriptEntitlement(db);
                return new CompanyScriptTiers(
                    runtime,
                    entitlement?.MaximumSeconds,
                    !runtime ? "Extended scripts need a separately qualified runtime."
                        : entitlement is null ? "Extended scripts require a verified paid plan that includes them."
                        : "Extended scripts use the same monthly compute pool. Each run needs exact approval; its full time allowance is reserved until cleanup is confirmed.",
                    entitlement?.PlanDigest
                );
            }
        );
    }

    private bool SandboxTierCurrent(StoreConnection db, CompanySandboxJob job) =>
        job.Request.Tier is null
            ? job.Premium is null
            : job.Request.Tier == SandboxRequest.ExtendedTier
                && ExtendedSandboxAvailable()
                && job.Premium is { } premium
                && premium == PaidScriptEntitlement(db)
                && job.Request.Limits.Seconds <= premium.MaximumSeconds;
}
