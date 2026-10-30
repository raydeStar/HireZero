namespace HireZero.Core;

public sealed record CompanyProgressOnboarding(
    DateTimeOffset? IdentifiedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ApprovedAt,
    double? SecondsToApprovedBrief,
    int GeneratedBriefs,
    int ApprovedBriefs,
    int EditedGeneratedBriefs,
    int OwnerFieldEdits
);

public sealed record CompanyProgressAssignment(
    string TaskId,
    string State,
    DateTimeOffset PreparedAt,
    DateTimeOffset? CompletedAt,
    CompanyProgressResult[]? Results = null
);

public sealed record CompanyProgressResult(string ArtifactId, string Digest, string Provenance, DateTimeOffset ReadyAt);

public sealed record CompanyProgressWork(
    int Tasks,
    int Completed,
    int AwaitingReview,
    int MemberRequestDays,
    int ScheduledTasks,
    DateTimeOffset? FirstCompletedAt,
    CompanyProgressAssignment? FirstAssignment
);

public sealed record CompanyProgressModelCosts(
    int Jobs,
    int SettledJobs,
    decimal ReportedUsd,
    decimal EstimatedUsd,
    int UnpricedJobs,
    int HeldJobs
);

public sealed record CompanyProgressView(
    string Schema,
    string CompanyId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? ObservationStartedAt,
    bool HasEarlierRecords,
    CompanyProgressOnboarding Onboarding,
    CompanyProgressWork Work,
    CompanyProgressModelCosts ModelCosts,
    CompanyProgressModelCosts CompletedWorkModelCosts,
    int CompletedTasksWithoutModelReceipt,
    decimal? AverageRecordedModelUsdPerCompletion,
    CompanyProgressTaskCosts? TaskCosts = null
);

public sealed partial class CompanyStore
{
    // Read the persisted ledger in one snapshot. A receipt is evidence; a page refresh is not a new achievement.
    public CompanyProgressView Progress(string? subject = null)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var member = subject is null ? null : Find<CompanyMember>(db, Text(subject, 400));
                if (subject is not null && member is not { State: "active" })
                    throw new WorkspaceException("You are not a member of this company.", 403);
                var events = Rows(
                    db,
                    "SELECT payload FROM co_events WHERE company_id=$0 ORDER BY seq",
                    r => CompanyJson.Decode<CompanyEvent>(r.GetString(0)),
                    CompanyId
                );
                DateTimeOffset? First(string type) => events.FirstOrDefault(e => e.Type == type)?.At;
                var history = All<CompanyProfileRevision>(db).OrderBy(r => r.Sequence).ToArray();
                var generated = history
                    .Where(r => r.Origin == "model")
                    .GroupBy(r => r.Profile.Id)
                    .Select(g => g.First())
                    .ToArray();
                // Count saved field edits after generation and before the first approval. Approval/revocation alone
                // changes no fields; manual-only briefs have no model baseline and cannot become model errors.
                var edits = generated
                    .Select(first =>
                    {
                        var approved = history.FirstOrDefault(r =>
                            r.Profile.Id == first.Profile.Id && r.Profile.State == "approved"
                        );
                        return history
                            .Where(r =>
                                r.Profile.Id == first.Profile.Id
                                && r.Sequence > first.Sequence
                                && (approved is null || r.Sequence < approved.Sequence)
                                && r.Origin == "owner"
                                && r.Profile.State == "draft"
                            )
                            .Sum(r => r.ChangedFields.Length);
                    })
                    .ToArray();
                var identifiedAt = First("business.identified");
                var approvedAt = First("business.profile-approved");
                var tasks = All<CompanyTask>(db).Where(t => t.Kind == "work").ToArray();
                var taskIds = tasks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                var completedIds = tasks
                    .Where(t => t.State == "completed")
                    .Select(t => t.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var scheduledIds = All<CompanyScheduleRun>(db)
                    .Where(r => r.TaskId is not null)
                    .Select(r => r.TaskId!)
                    .ToHashSet(StringComparer.Ordinal);
                var assignments = events.Where(e => e.Type == "task.assigned" && taskIds.Contains(e.Id)).ToArray();
                var assignedIds = assignments.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
                var completions = events.Where(e => e.Type == "task.completed" && taskIds.Contains(e.Id)).ToArray();
                var firstAssignment = events.FirstOrDefault(e =>
                    e.Type == "assignment.prepared" && taskIds.Contains(e.Id)
                );
                CompanyProgressResult[]? firstResults = null;
                if (firstAssignment is not null && member?.Role == CompanyRoles.Owner)
                {
                    var artifacts = All<CompanyArtifact>(db)
                        .Where(a => a.TaskId == firstAssignment.Id)
                        .ToDictionary(a => a.Id);
                    // Saved creation order survives approval and editorial copies. Export references, never the copy.
                    firstResults = events
                        .Where(e => e.Type == "artifact.ready" && artifacts.ContainsKey(e.Id))
                        .Select(e => new CompanyProgressResult(
                            e.Id,
                            artifacts[e.Id].Digest,
                            artifacts[e.Id].Provenance,
                            e.At
                        ))
                        .DistinctBy(r => r.ArtifactId)
                        .ToArray();
                }
                var usage = UsageEntries(db);
                var completedUsage = usage
                    .Where(e => e.TaskId is not null && completedIds.Contains(e.TaskId))
                    .ToArray();
                var completedCosts = ProgressCosts(completedUsage);
                var pricedTaskIds = completedUsage.Select(e => e.TaskId!).ToHashSet(StringComparer.Ordinal);
                var withoutReceipt = completedIds.Count(id => !pricedTaskIds.Contains(id));
                var historicalProfileIds = history.Select(r => r.Profile.Id).ToHashSet(StringComparer.Ordinal);
                var earlier =
                    history.Any(r => r.Origin == "earlier")
                    || usage.Any(e => e.Period == "earlier")
                    || tasks.Any(t => !assignedIds.Contains(t.Id))
                    || All<CompanyBusinessProfile>(db).Any(p => !historicalProfileIds.Contains(p.Id));
                return new CompanyProgressView(
                    "company-progress-v1",
                    CompanyId,
                    Clock(),
                    events.FirstOrDefault()?.At,
                    earlier,
                    new(
                        identifiedAt,
                        First("business.confirmed"),
                        approvedAt,
                        identifiedAt is { } start && approvedAt is { } end && end >= start
                            ? (end - start).TotalSeconds
                            : null,
                        generated.Length,
                        events.Where(e => e.Type == "business.profile-approved").Select(e => e.Id).Distinct().Count(),
                        edits.Count(n => n > 0),
                        edits.Sum()
                    ),
                    new(
                        tasks.Length,
                        completedIds.Count,
                        tasks.Count(t => t.State == "awaiting_review"),
                        assignments
                            .Where(e =>
                                !scheduledIds.Contains(e.Id) && e.Actor is not null && e.Actor != CompanyActor.System
                            )
                            .Select(e => e.At.UtcDateTime.Date)
                            .Distinct()
                            .Count(),
                        tasks.Count(t => scheduledIds.Contains(t.Id)),
                        completions.FirstOrDefault()?.At,
                        firstAssignment is null
                            ? null
                            : new(
                                firstAssignment.Id,
                                tasks.Single(t => t.Id == firstAssignment.Id).State,
                                firstAssignment.At,
                                completions.FirstOrDefault(e => e.Id == firstAssignment.Id)?.At,
                                firstResults
                            )
                    ),
                    ProgressCosts(usage),
                    completedCosts,
                    withoutReceipt,
                    completedIds.Count > 0
                    && withoutReceipt == 0
                    && completedCosts.UnpricedJobs == 0
                    && completedCosts.HeldJobs == 0
                    && completedCosts.SettledJobs == completedCosts.Jobs
                        ? (completedCosts.ReportedUsd + completedCosts.EstimatedUsd) / completedIds.Count
                        : null,
                    member?.Role == CompanyRoles.Owner ? ProgressTaskCosts(tasks, ReadTaskCostLedger(db, usage)) : null
                );
            }
        );
    }

    private static CompanyProgressModelCosts ProgressCosts(CompanyUsageEntry[] entries)
    {
        var settled = entries.Where(e => e.State == "settled").ToArray();
        return new(
            entries.Length,
            settled.Length,
            settled.Sum(e => e.ReportedUsd ?? 0),
            settled.Where(e => e.ReportedUsd is null).Sum(e => e.EstimatedUsd ?? 0),
            settled.Count(e => e.ReportedUsd is null && e.EstimatedUsd is null),
            entries.Count(e => e.State == "reserved")
        );
    }
}
