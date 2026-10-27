using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace HireZero.Core;

public sealed record BillingAllowance(
    int Jobs,
    long Tokens,
    int ComputeJobs,
    int ComputeSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ExtendedScriptSeconds = null
);

// Operator configuration, never an owner-editable safety budget. No commercial defaults are invented here.
public sealed record BillingPlan(string Id, string Name, BillingPrice Price, BillingAllowance Allowance);

public sealed class CompanyBillingConfiguration
{
    public string ProviderIdentity { get; }
    public FrozenDictionary<string, BillingPlan> Plans { get; }

    public CompanyBillingConfiguration(string providerIdentity, IEnumerable<BillingPlan> plans)
    {
        if (providerIdentity.Length != 64 || !providerIdentity.All(Uri.IsHexDigit))
            throw new ArgumentException("Use the verified billing provider identity.");
        var items = plans.ToArray();
        if (items.Length is < 1 or > 20 || items.Select(p => p.Id).Distinct().Count() != items.Length)
            throw new ArgumentException("Configure between one and twenty distinct billing plans.");
        foreach (var plan in items)
            if (
                plan.Id.Length is < 1 or > 60
                || plan.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
                || string.IsNullOrWhiteSpace(plan.Name)
                || plan.Name.Length > 100
                || plan.Name.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(plan.Price.Id)
                || plan.Price.Currency.Length != 3
                || plan.Price.Currency.Any(c => c is < 'a' or > 'z')
                || plan.Price.UnitAmount is <= 0 or > 999_999_999_999
                || plan.Price.Interval != "month"
                || plan.Price.IntervalCount != 1
                || !plan.Price.Active
                || plan.Allowance.Jobs is < 0 or > 10_000
                || plan.Allowance.Tokens is < 0 or > 1_000_000_000
                || plan.Allowance.ComputeJobs is < 0 or > 1000
                || plan.Allowance.ComputeSeconds is < 0 or > 120_000
                || plan.Allowance.ExtendedScriptSeconds is { } extended
                    && (
                        extended is < 121 or > 1800
                        || extended > plan.Allowance.ComputeSeconds
                        || plan.Allowance.ComputeJobs == 0
                    )
            )
                throw new ArgumentException("Configure an explicit monthly price and bounded UTC-month allowances.");
        ProviderIdentity = providerIdentity;
        Plans = items.ToFrozenDictionary(p => p.Id, StringComparer.Ordinal);
    }

    public string Digest(BillingPlan plan) => CompanyJson.Digest(new { ProviderIdentity, Plan = plan });

    public bool Current(CompanyBillingCheckout checkout) =>
        ProviderIdentity == checkout.ProviderIdentity
        && Plans.TryGetValue(checkout.Plan.Id, out var plan)
        && Digest(plan) == checkout.PlanDigest;
}

public sealed record CompanyBillingCheckout(
    string Id,
    string ProviderIdentity,
    BillingPlan Plan,
    string PlanDigest,
    string Digest,
    string State,
    DateTimeOffset ExpiresAt,
    int Version = 1,
    string? ApprovedBy = null,
    DateTimeOffset? DispatchedAt = null,
    BillingCheckoutReceipt? Receipt = null
)
{
    public string BillingKind => "checkout";
}

public sealed record CompanyBillingState(
    string Id = "subscription",
    bool Required = false,
    string? AttemptId = null,
    string? ProviderIdentity = null,
    string? CustomerId = null,
    string? SubscriptionId = null,
    string Status = "not-configured",
    string? RefreshId = null,
    DateTimeOffset? VerifiedAt = null,
    DateTimeOffset? ValidUntil = null,
    DateTimeOffset? PeriodEnd = null,
    bool CancelAtPeriodEnd = false,
    int Version = 0
);

public sealed record CompanyBillingView(
    bool Required,
    bool Configured,
    CompanyBillingState State,
    BillingPlan[] Plans,
    CompanyBillingCheckout? Checkout,
    BillingAllowance? Allowance
);

public sealed record CompanyBillingRefresh(string Id, CompanyBillingCheckout Checkout, string? CustomerId);
