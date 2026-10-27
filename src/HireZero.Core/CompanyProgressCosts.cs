namespace HireZero.Core;

public sealed record CompanyProgressCostGap(string TaskId, string Reason);

public sealed record CompanyProgressTaskCosts(
    int CompletedTasks,
    int ReconciledTasks,
    int MissingReviews,
    int StaleReviews,
    int IncompleteUsage,
    int IncludesModelEstimates,
    decimal ReportedModelUsd,
    decimal EstimatedModelUsd,
    decimal CompanyKeyModelUsd,
    CompanyOperatingCost[] OperatingCosts,
    decimal? ReconciledSubtotalUsd,
    decimal? AverageUsdPerCompletion,
    CompanyProgressCostGap? NextReview
);

public sealed partial class CompanyStore
{
    private static CompanyProgressTaskCosts ProgressTaskCosts(CompanyTask[] tasks, TaskCostLedger ledger)
    {
        // Read each ledger once, then apply the same reconciliation rules as the task's own report.
        // An empty drawer is missing evidence, never a complimentary invoice.
        var completed = tasks
            .Where(t => t.State == "completed")
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => TaskCosts(t, ledger))
            .ToArray();
        var reconciled = completed.Where(v => v.ReconciledTotalUsd is not null).ToArray();
        var missing = completed.Where(v => !v.Reviews.Any(r => r.State == "recorded")).ToArray();
        var stale = completed
            .Where(v => !v.ReconciliationCurrent && v.Reviews.Any(r => r.State == "recorded"))
            .ToArray();
        var incomplete = completed.Where(v => v.ReconciliationCurrent && v.ReconciledTotalUsd is null).ToArray();
        var next = completed.FirstOrDefault(v => v.ReconciledTotalUsd is null);
        var subtotal = reconciled.Length == 0 ? (decimal?)null : reconciled.Sum(v => v.ReconciledTotalUsd!.Value);
        return new(
            completed.Length,
            reconciled.Length,
            missing.Length,
            stale.Length,
            incomplete.Length,
            reconciled.Count(v => v.IncludesModelEstimates),
            reconciled.Sum(v => v.ReportedModelUsd),
            reconciled.Sum(v => v.EstimatedModelUsd),
            reconciled.Sum(v => v.CompanyKeyModelUsd),
            OperatingCostCategories
                .Select(category => new CompanyOperatingCost(
                    category,
                    reconciled.Length == 0
                        ? null
                        : reconciled.Sum(v =>
                            v.Reviews.First(r => r.State == "recorded")
                                .Costs.Single(c => c.Category == category)
                                .Usd!.Value
                        )
                ))
                .ToArray(),
            subtotal,
            reconciled.Length == completed.Length && completed.Length > 0 ? subtotal / completed.Length : null,
            next is null
                ? null
                : new(
                    next.TaskId,
                    !next.Reviews.Any(r => r.State == "recorded") ? "not-recorded"
                        : !next.ReconciliationCurrent ? "usage-changed"
                        : "usage-incomplete"
                )
        );
    }
}
