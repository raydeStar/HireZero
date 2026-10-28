using System.Globalization;

namespace HireZero.Core;

public sealed record CompanyUsageLimits(int Jobs = 100, long Tokens = 1_000_000, bool Paused = false);

public sealed record CompanyUsagePolicy(
    string Id,
    CompanyUsageLimits Limits,
    int Version,
    DateTimeOffset UpdatedAt,
    bool WorkPaused = false
);

public sealed record CompanyModelFunding(string Source, string Provider, string ConnectionRevision);

public sealed record CompanyUsageEntry(
    string Id,
    string Period,
    string Kind,
    string? TaskId,
    string? EmployeeId,
    string Route,
    DateTimeOffset? StartedAt,
    long PlanningTokens,
    ModelPrice? Price,
    int PolicyVersion,
    string State = "reserved",
    string Outcome = "dispatching",
    long? ActualTokens = null,
    long? ObservedTokens = null,
    decimal? ReportedUsd = null,
    decimal? EstimatedUsd = null,
    string? Receipt = null,
    DateTimeOffset? SettledAt = null,
    int Version = 1,
    CompanyModelFunding? Funding = null
);

public sealed record CompanyUsageSummary(
    string Period,
    DateTimeOffset ResetsAt,
    int Jobs,
    decimal Tokens,
    int HeldJobs,
    decimal HeldTokens,
    int CarriedJobs,
    decimal CarriedTokens,
    decimal ReportedUsd,
    decimal EstimatedUsd,
    int UnpricedJobs,
    int ProvisionalCosts,
    decimal CompanyKeyReportedUsd = 0,
    decimal CompanyKeyEstimatedUsd = 0
);

public sealed record CompanyUsageView(
    CompanyUsagePolicy Policy,
    CompanyUsageSummary Current,
    CompanyUsageSummary Selected,
    CompanyUsageEntry[] Entries,
    string? NextBefore,
    string[] Periods,
    int EarlierJobs,
    BillingAllowance? SubscriptionAllowance = null,
    bool SubscriptionRequired = false
);

public sealed partial class CompanyStore
{
    public Func<string, ModelPrice?> UsagePrice { get; init; } = ModelRoutes.Price;
    public Func<string, CompanyModelFunding?> UsageFunding { get; init; } = _ => null;
    public Func<string, string, CompanyModelFunding?>? UsageRouteFunding { get; init; }
    private const string UsageSchema =
        "CREATE TABLE co_usage_policy(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));"
        + "CREATE TABLE co_usage_entries(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";
    private string UsagePeriod => Clock().UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateTimeOffset PeriodEnd(string period)
    {
        if (period == "earlier")
            return DateTimeOffset.MinValue;
        if (
            !DateOnly.TryParseExact(
                period + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
            || date.Year >= 9999
        )
            throw new WorkspaceException("Choose a usage month as yyyy-MM.", 422);
        return new DateTimeOffset(date.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private CompanyUsagePolicy UsagePolicy(StoreConnection db) =>
        Find<CompanyUsagePolicy>(db, "monthly") ?? new("monthly", new(), 0, DateTimeOffset.MinValue);

    // Earlier receipts have no reliable dispatch date. Do not invent a month or an old price.
    private CompanyUsageEntry[] UsageEntries(StoreConnection db)
    {
        var saved = All<CompanyUsageEntry>(db);
        var ids = saved.Select(e => e.Id).ToHashSet();
        var attempts = All<CompanyAttempt>(db).ToDictionary(a => a.Id);
        var drafts = All<CompanyBusinessDraft>(db).ToDictionary(d => d.Id);
        return
        [
            .. saved,
            .. All<CompanyReservation>(db)
                .Where(r => !ids.Contains(r.Id))
                .Select(r =>
                {
                    attempts.TryGetValue(r.Id, out var a);
                    drafts.TryGetValue(r.Id, out var d);
                    var usage = a?.Usage ?? d?.Usage;
                    var observed = a?.ReportedUsage ?? d?.ReportedUsage;
                    var consistent = usage is not null && usage.Consistent(observed) && usage.BilledUsd is not < 0;
                    return new CompanyUsageEntry(
                        r.Id,
                        "earlier",
                        d is null ? "employee-job" : "profile-draft",
                        a?.TaskId,
                        a?.EmployeeId,
                        a?.Measured?.Route ?? d?.Job.Route ?? "Unknown earlier route",
                        null,
                        r.Amount,
                        null,
                        0,
                        r.State,
                        a?.Phase ?? d?.State ?? r.State,
                        r.Actual,
                        observed is >= 0 ? observed : null,
                        consistent ? usage!.BilledUsd : null,
                        Receipt: a?.ProviderReceipt ?? d?.ProviderReceipt
                    );
                }),
        ];
    }

    private static decimal HeldTokens(CompanyUsageEntry e) =>
        e.State == "reserved" ? Math.Max((decimal)e.PlanningTokens, e.ObservedTokens ?? 0) : 0;

    private static CompanyUsageSummary UsageSummary(CompanyUsageEntry[] entries, string period)
    {
        var selected = entries.Where(e => e.Period == period).ToArray();
        var carried = entries.Where(e => e.Period != period && e.State == "reserved").ToArray();
        var settled = selected.Where(e => e.State == "settled").ToArray();
        return new(
            period,
            PeriodEnd(period),
            selected.Length,
            settled.Sum(e => (decimal)(e.ActualTokens ?? 0)),
            selected.Count(e => e.State == "reserved"),
            selected.Sum(HeldTokens),
            carried.Length,
            carried.Sum(HeldTokens),
            settled.Sum(e => e.ReportedUsd ?? 0),
            settled.Where(e => e.ReportedUsd is null).Sum(e => e.EstimatedUsd ?? 0),
            settled.Count(e => e.ReportedUsd is null && e.EstimatedUsd is null),
            selected.Count(e => e.State == "reserved" && (e.ReportedUsd is not null || e.EstimatedUsd is not null)),
            settled.Where(e => e.Funding?.Source == "company-key").Sum(e => e.ReportedUsd ?? 0),
            settled
                .Where(e => e.Funding?.Source == "company-key" && e.ReportedUsd is null)
                .Sum(e => e.EstimatedUsd ?? 0)
        );
    }

    public CompanyUsageView Usage(string? period = null, string? before = null)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                period ??= UsagePeriod;
                PeriodEnd(period);
                var all = UsageEntries(db);
                var selected = all.Where(e => e.Period == period)
                    .OrderByDescending(e => e.StartedAt)
                    .ThenByDescending(e => e.Id, StringComparer.Ordinal)
                    .ToArray();
                var start = 0;
                if (before is not null)
                {
                    var index = Array.FindIndex(selected, e => e.Id == before);
                    if (index < 0)
                        throw new WorkspaceException(
                            "This usage page changed. Open the latest entries for this month.",
                            404
                        );
                    start = index + 1;
                }
                var page = selected.Skip(start).Take(51).ToArray();
                return new CompanyUsageView(
                    UsagePolicy(db),
                    UsageSummary(all, UsagePeriod),
                    UsageSummary(all, period),
                    page.Take(50).ToArray(),
                    page.Length > 50 ? page[49].Id : null,
                    all.Select(e => e.Period)
                        .Append(UsagePeriod)
                        .Distinct()
                        .OrderByDescending(p => p, StringComparer.Ordinal)
                        .ToArray(),
                    all.Count(e => e.Period == "earlier"),
                    PaidAllowance(db),
                    BillingRequired(db)
                );
            }
        );
    }

    private object ConfigureUsage(StoreConnection db, CompanyCommand command)
    {
        var current = UsagePolicy(db);
        Version(current.Version, command.ExpectedVersion);
        var limits = CompanyJson.Decode<CompanyUsageLimits>(command.Body ?? "null");
        if (limits.Jobs is < 0 or > 10_000 || limits.Tokens is < 0 or > 1_000_000_000)
            throw new WorkspaceException(
                "Choose 0–10,000 AI jobs and 0–1,000,000,000 planning tokens per UTC month.",
                422
            );
        var next = current with { Limits = limits, Version = current.Version + 1, UpdatedAt = Clock() };
        Save(db, next, next.Id, version: next.Version);
        Event(
            db,
            "usage.limits-changed",
            next.Id,
            null,
            "Owner updated monthly safety limits. No job or payment was authorized."
        );
        return next;
    }

    private void ReserveUsage(
        StoreConnection db,
        string id,
        string kind,
        string? task,
        string? employee,
        string route,
        long tokens,
        CompanyModelFunding? funding = null
    )
    {
        DemandWorkAdmission(db);
        if (Find<CompanyUsageEntry>(db, id) is not null)
            throw new WorkspaceException(
                "This job already consumed its monthly admission. It cannot be replayed.",
                409
            );
        var policy = UsagePolicy(db);
        var started = Clock();
        var period = started.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var summary = UsageSummary(UsageEntries(db), period);
        DemandBillingUsage(db, summary, tokens);
        if (
            policy.Limits.Paused
            || summary.Jobs + summary.CarriedJobs >= policy.Limits.Jobs
            || summary.Tokens + summary.HeldTokens + summary.CarriedTokens + tokens > policy.Limits.Tokens
        )
            throw new WorkspaceException(
                "Monthly usage prevents this job. Open Usage to review limits, remaining allowance and unresolved earlier reservations.",
                409
            );
        var entry = new CompanyUsageEntry(
            id,
            period,
            kind,
            task,
            employee,
            route,
            started,
            tokens,
            UsagePrice(route),
            policy.Version,
            Funding: funding ?? UsageRouteFunding?.Invoke(kind, route) ?? UsageFunding(kind)
        );
        Save(db, entry, id, state: entry.State, version: entry.Version);
    }

    private void SyncUsage(StoreConnection db, string id)
    {
        var entry = Find<CompanyUsageEntry>(db, id);
        if (entry is null)
            return;
        var reservation = Find<CompanyReservation>(db, id);
        if (reservation is null)
            return;
        var attempt = Find<CompanyAttempt>(db, id);
        var draft = Find<CompanyBusinessDraft>(db, id);
        var analysis = Find<CompanyMemoryAnalysis>(db, id);
        var decision = Find<CompanyDecision>(db, id);
        var usage = attempt?.Usage ?? draft?.Usage ?? analysis?.Usage ?? decision?.Usage;
        var reported =
            attempt?.ReportedUsage ?? draft?.ReportedUsage ?? analysis?.ReportedUsage ?? decision?.ReportedUsage;
        var consistent = usage is not null && usage.Consistent(reported) && usage.BilledUsd is not < 0;
        var next = entry with
        {
            State = reservation.State,
            Outcome = attempt?.Phase ?? draft?.State ?? analysis?.State ?? decision?.State ?? entry.Outcome,
            ActualTokens = reservation.Actual,
            ObservedTokens = reported is >= 0
                ? Math.Max(reported.Value, entry.ObservedTokens ?? 0)
                : entry.ObservedTokens,
            ReportedUsd = decision?.ReportedCostUsd ?? (consistent ? usage!.BilledUsd : entry.ReportedUsd),
            EstimatedUsd = consistent ? entry.Price?.Cost(usage!) : entry.EstimatedUsd,
            Receipt =
                attempt?.ProviderReceipt
                ?? draft?.ProviderReceipt
                ?? analysis?.ProviderReceipt
                ?? decision?.ProviderReceipt
                ?? entry.Receipt,
            SettledAt = entry.SettledAt ?? (reservation.State == "settled" ? Clock() : null),
        };
        if (next == entry)
            return;
        next = next with { Version = entry.Version + 1 };
        Save(db, next, next.Id, state: next.State, version: next.Version);
    }
}
