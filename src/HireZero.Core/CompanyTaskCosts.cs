namespace HireZero.Core;

public sealed record CompanyOperatingCost(string Category, decimal? Usd);

public sealed record CompanyTaskCostInput(string TaskId, CompanyOperatingCost[] Costs, string Basis);

public sealed record CompanyTaskCostReview(
    string Id,
    string TaskId,
    int Sequence,
    CompanyOperatingCost[] Costs,
    string Basis,
    string EvidenceDigest,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    string? RecordedBy = null,
    DateTimeOffset? RecordedAt = null,
    string Kind = "task-cost-review"
);

public sealed record CompanyTaskCostModelLine(
    string Id,
    string Outcome,
    string State,
    decimal? ReportedUsd,
    decimal? EstimatedUsd,
    string Funding
);

public sealed record CompanyTaskCostComputeLine(
    string Id,
    string Kind,
    string State,
    int? AllocatedSeconds,
    int HeldSeconds,
    long? WallMilliseconds,
    bool Released
);

public sealed record CompanyTaskCostView(
    string TaskId,
    string TaskState,
    string EvidenceDigest,
    CompanyTaskCostModelLine[] Models,
    CompanyTaskCostComputeLine[] Compute,
    int UnmeteredAttempts,
    int UnpricedModels,
    int HeldModels,
    decimal ReportedModelUsd,
    decimal EstimatedModelUsd,
    decimal CompanyKeyModelUsd,
    CompanyTaskCostReview[] Reviews,
    bool ReconciliationCurrent,
    decimal? OperatingUsd,
    decimal? ReconciledTotalUsd,
    bool IncludesModelEstimates
);

public sealed partial class CompanyStore
{
    private const string TaskCostSchema =
        "CREATE TABLE co_task_cost_reviews(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_tasks(company_id,id));";
    public static string[] OperatingCostCategories => ["compute", "tools", "retrieval", "storage", "operations"];

    public bool IsTaskCostOperation(string id)
    {
        using var db = Open();
        return Scalar(
                db,
                "SELECT json_extract(result,'$.kind') FROM co_operations WHERE company_id=$0 AND id=$1",
                CompanyId,
                id
            ) == "task-cost-review";
    }

    public CompanyTaskCostView TaskCosts(string taskId)
    {
        using var db = Open();
        return Transaction(db, () => TaskCosts(db, taskId));
    }

    private CompanyTaskCostView TaskCosts(StoreConnection db, string taskId) =>
        TaskCosts(Need<CompanyTask>(db, taskId), ReadTaskCostLedger(db));

    private sealed record TaskCostLedger(
        ILookup<string?, CompanyUsageEntry> Usage,
        ILookup<string, CompanyAttempt> Attempts,
        ILookup<string, CompanySandboxJob> Scripts,
        ILookup<string, CompanyBrowserJob> Browsers,
        ILookup<string, CompanyTaskCostReview> Reviews
    );

    private TaskCostLedger ReadTaskCostLedger(StoreConnection db, CompanyUsageEntry[]? usage = null) =>
        new(
            (usage ?? UsageEntries(db)).ToLookup(e => e.TaskId),
            All<CompanyAttempt>(db).ToLookup(a => a.TaskId),
            All<CompanySandboxJob>(db).ToLookup(j => j.TaskId),
            All<CompanyBrowserJob>(db).ToLookup(j => j.TaskId),
            All<CompanyTaskCostReview>(db).ToLookup(r => r.TaskId)
        );

    private static CompanyTaskCostView TaskCosts(CompanyTask task, TaskCostLedger ledger)
    {
        var taskId = task.Id;
        var usage = ledger.Usage[taskId].OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var known = usage.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var attempts = ledger
            .Attempts[taskId]
            .Where(a => !known.Contains(a.Id) && a.Phase != "not_sent" && a.Measured?.State != "proposed")
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .ToArray();
        var models = usage
            .Select(e => new CompanyTaskCostModelLine(
                e.Id,
                e.Outcome,
                e.State,
                e.ReportedUsd,
                e.EstimatedUsd,
                e.Funding?.Source ?? "host"
            ))
            .ToArray();
        var compute = ledger
            .Scripts[taskId]
            .Where(j => j.StartedAt is not null)
            .Select(j => new CompanyTaskCostComputeLine(
                j.Id,
                "script",
                j.State,
                j.AllocatedSeconds,
                j.SettledAt is null ? j.Request.Limits.Seconds : 0,
                j.Result?.WallMilliseconds,
                j.Release?.Confirmed == true
            ))
            .Concat(
                ledger
                    .Browsers[taskId]
                    .Where(j => j.StartedAt is not null)
                    .Select(j => new CompanyTaskCostComputeLine(
                        j.Id,
                        "browser",
                        j.State,
                        j.AllocatedSeconds,
                        j.SettledAt is null ? j.ReservedSeconds : 0,
                        j.Execution?.WallMilliseconds,
                        j.Release?.Confirmed == true
                    ))
            )
            .OrderBy(j => j.Id, StringComparer.Ordinal)
            .ToArray();
        // Allocation is a safety quota, not a hosting invoice. A tidy number is no excuse to invent a price.
        var digest = CompanyJson.Digest(
            new
            {
                task.Id,
                task.State,
                task.Generation,
                usage,
                unmetered = attempts
                    .Select(a => new
                    {
                        a.Id,
                        a.Phase,
                        a.Version,
                        a.Usage,
                        a.ProviderReceipt,
                    })
                    .ToArray(),
                compute,
            }
        );
        var reviews = ledger.Reviews[taskId].OrderByDescending(r => r.Sequence).ToArray();
        var recorded = reviews.FirstOrDefault(r => r.State == "recorded");
        var current = recorded?.EvidenceDigest == digest;
        var operating = current ? recorded!.Costs.Sum(c => c.Usd!.Value) : (decimal?)null;
        var settled = usage.Where(e => e.State == "settled").ToArray();
        var reported = settled.Sum(e => e.ReportedUsd ?? 0);
        var estimated = settled.Where(e => e.ReportedUsd is null).Sum(e => e.EstimatedUsd ?? 0);
        var unpriced = settled.Count(e => e.ReportedUsd is null && e.EstimatedUsd is null);
        var held = usage.Count(e => e.State == "reserved");
        var complete =
            task.State == "completed"
            && current
            && attempts.Length == 0
            && unpriced == 0
            && held == 0
            && usage.All(e => e.State is "settled" or "released")
            && compute.All(j => j.Released);
        return new(
            taskId,
            task.State,
            digest,
            models,
            compute,
            attempts.Length,
            unpriced,
            held,
            reported,
            estimated,
            settled.Where(e => e.Funding?.Source == "company-key").Sum(e => e.ReportedUsd ?? e.EstimatedUsd ?? 0),
            reviews,
            current,
            operating,
            complete ? reported + estimated + operating : null,
            settled.Any(e => e.ReportedUsd is null && e.EstimatedUsd is not null)
        );
    }

    private object SaveTaskCosts(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyTaskCostInput>(command.Body ?? "null");
        if (
            input is null
            || input.Costs is null
            || input.Costs.Length != OperatingCostCategories.Length
            || input.Costs.Any(c =>
                c is null || c.Usd is < 0 or > 100_000 || c.Usd is { } usd && decimal.Round(usd, 6) != usd
            )
            || !input
                .Costs.Select(c => c.Category)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(OperatingCostCategories.Order(StringComparer.Ordinal))
        )
            throw new WorkspaceException(
                "Record each operating-cost category once, using USD amounts from zero to 100,000 with at most six decimal places. Leave unknown amounts blank.",
                422
            );
        var task = Need<CompanyTask>(db, input.TaskId);
        if (task.Kind != "work")
            throw new WorkspaceException("Choose a work task for cost reconciliation.", 409);
        var previous = command.Id is null ? null : Need<CompanyTaskCostReview>(db, command.Id);
        Version(previous?.Version ?? 0, command.ExpectedVersion);
        if (previous is not null && (previous.TaskId != task.Id || previous.State != "draft"))
            throw new WorkspaceException("Create a new cost draft to correct a recorded reconciliation.", 409);
        var view = TaskCosts(db, task.Id);
        if (previous is null && view.Reviews.Any(r => r.State == "draft"))
            throw new WorkspaceException("Continue the existing cost draft before creating another.", 409);
        if (previous is null && view.Reviews.Length >= 100)
            throw new WorkspaceException("This task has reached its retained cost-review limit.", 429);
        var basis = Text(input.Basis, 1000).Trim();
        var costs = OperatingCostCategories
            .Select(category => input.Costs.Single(c => c.Category == category))
            .ToArray();
        var digest = CompanyJson.Digest(
            new
            {
                taskId = task.Id,
                costs,
                basis,
                evidence = view.EvidenceDigest,
            }
        );
        var next = new CompanyTaskCostReview(
            previous?.Id ?? Id(),
            task.Id,
            previous?.Sequence ?? (view.Reviews.Select(r => r.Sequence).DefaultIfEmpty().Max() + 1),
            costs,
            basis,
            view.EvidenceDigest,
            digest,
            "draft",
            (previous?.Version ?? 0) + 1,
            Clock()
        );
        Save(db, next, next.Id, task.Id, task.EmployeeId, next.State, next.Version);
        Event(
            db,
            "cost.draft",
            task.Id,
            task.EmployeeId,
            "Owner saved an operating-cost draft. No payment or customer charge was created."
        );
        return next;
    }

    private object ReviewTaskCosts(StoreConnection db, CompanyCommand command)
    {
        var review = Need<CompanyTaskCostReview>(db, command.Id);
        Version(review.Version, command.ExpectedVersion);
        if (
            review.State != "draft"
            || review.Digest != command.Digest
            || command.Decision is not ("record-costs" or "discard")
        )
            throw new WorkspaceException("Review the exact saved cost draft or discard it.", 409);
        var task = Need<CompanyTask>(db, review.TaskId);
        if (command.Decision == "record-costs")
        {
            if (task.State != "completed")
                throw new WorkspaceException("Complete the task before recording its reconciled operating costs.", 409);
            if (review.Costs.Any(c => c.Usd is null) || review.Basis.Length < 10)
                throw new WorkspaceException(
                    "Fill every category, using zero only for a known zero cost, and describe the receipt or allocation basis.",
                    422
                );
            var view = TaskCosts(db, task.Id);
            if (view.EvidenceDigest != review.EvidenceDigest)
                throw new WorkspaceException("Task usage changed. Save and review the cost draft again.", 409);
            if (view.HeldModels > 0 || view.Compute.Any(j => !j.Released))
                throw new WorkspaceException(
                    "Reconcile outstanding provider usage and resource cleanup before recording these costs.",
                    409
                );
        }
        var next = review with
        {
            State = command.Decision == "record-costs" ? "recorded" : "discarded",
            Version = review.Version + 1,
            UpdatedAt = Clock(),
            RecordedBy = command.Decision == "record-costs" ? CurrentActor.Value!.Subject : null,
            RecordedAt = command.Decision == "record-costs" ? Clock() : null,
        };
        Save(db, next, next.Id, task.Id, task.EmployeeId, next.State, next.Version);
        Audit(
            db,
            "task-cost-" + next.State,
            next.Id,
            new
            {
                next.TaskId,
                next.Digest,
                next.EvidenceDigest,
                next.Costs,
                next.RecordedAt,
            }
        );
        Event(
            db,
            "cost." + next.State,
            task.Id,
            task.EmployeeId,
            next.State == "recorded"
                ? "Owner recorded task operating costs against the saved usage evidence. No customer was charged."
                : "Owner discarded an unrecorded cost draft."
        );
        return next;
    }
}
