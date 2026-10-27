namespace HireZero.Core;

public sealed record CompanySourceCheck(
    string Id,
    string RunId,
    string ProfileId,
    string SourceId,
    string? ObservedSourceId,
    string Status,
    DateTimeOffset CheckedAt,
    string? Notice
);

public sealed record CompanySourceRefresh(
    string Id,
    string ProfileId,
    int ProfileVersion,
    string ProfileDigest,
    int BusinessVersion,
    string Website,
    string AuthorizedBy,
    string[] SourceIds,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    CompanySourceCheck[]? Checks = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? ScheduleRunId = null
);

public sealed record CompanyFactReviewInput(
    string Field,
    string EvidenceDigest,
    string? PreviousReviewId,
    string Status,
    string Note,
    string? SourceId = null,
    string? Quote = null
);

public sealed record CompanyFactReview(
    string Id,
    string ProfileId,
    CompanyFactReviewInput Review,
    string ActorName,
    DateTimeOffset ReviewedAt
);

public sealed record CompanyFactFreshness(
    string Field,
    string Status,
    string EvidenceDigest,
    CompanySourceCheck[] Checks,
    CompanyFactReview? Review
);

public sealed record CompanyBusinessFreshness(
    string ProfileId,
    string Digest,
    bool Blocked,
    CompanyFactFreshness[] Fields,
    CompanySourceRefresh[] Runs
);

public sealed record CompanySourceCheckResult(string SourceId, string? ObservedSourceId, string? Notice = null);

public sealed partial class CompanyStore
{
    private const string BusinessFreshnessSchema =
        "CREATE TABLE co_business_refreshes(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_business_profiles(company_id,id));"
        + "CREATE TABLE co_business_checks(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_business_profiles(company_id,id));"
        + "CREATE INDEX co_business_checks_latest ON co_business_checks(company_id,parent_id,state,revision);"
        + "CREATE TABLE co_business_fact_reviews(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_business_profiles(company_id,id));"
        + "CREATE INDEX co_business_fact_reviews_latest ON co_business_fact_reviews(company_id,parent_id,state,revision);";

    private T? LatestFreshness<T>(StoreConnection db, string profile, string key)
        where T : class =>
        Rows(
                db,
                $"SELECT payload FROM {Tables[typeof(T)]} WHERE company_id=$0 AND parent_id=$1 AND state=$2 ORDER BY revision DESC LIMIT 1",
                r => CompanyJson.Decode<T>(r.GetString(0)),
                CompanyId,
                profile,
                key
            )
            .FirstOrDefault();

    public CompanySourceRefresh BusinessRefresh(string id)
    {
        using var db = Open();
        return Need<CompanySourceRefresh>(db, id);
    }

    public CompanyBusinessFreshness BusinessFreshness(string profileId)
    {
        using var db = Open();
        return Transaction(db, () => BusinessFreshness(db, Need<CompanyBusinessProfile>(db, profileId)));
    }

    private CompanyBusinessFreshness BusinessFreshness(StoreConnection db, CompanyBusinessProfile profile)
    {
        var fields = profile
            .Fields.Select(field =>
            {
                var checks = field
                    .Citations.Select(c => LatestFreshness<CompanySourceCheck>(db, profile.Id, c.SourceId))
                    .OfType<CompanySourceCheck>()
                    .OrderBy(c => c.SourceId)
                    .ToArray();
                // A new check date alone does not erase the owner's assessment of identical evidence.
                var evidence = CompanyJson.Digest(
                    checks
                        .Select(c => new
                        {
                            c.SourceId,
                            c.ObservedSourceId,
                            c.Status,
                        })
                        .ToArray()
                );
                var review = LatestFreshness<CompanyFactReview>(db, profile.Id, field.Key);
                var status =
                    checks.Any(c => c.Status == "changed") ? "possible"
                    : checks.Any(c => c.Status == "unavailable") ? "unavailable"
                    : checks.Length > 0 && checks.Length == field.Citations.Length ? "unchanged"
                    : "unchecked";
                if (
                    review is not null
                    && (review.Review.Status is "conflicting" or "outdated" || review.Review.EvidenceDigest == evidence)
                )
                    status = review.Review.Status;
                return new CompanyFactFreshness(field.Key, status, evidence, checks, review);
            })
            .ToArray();
        var runs = Rows(
            db,
            "SELECT payload FROM co_business_refreshes WHERE company_id=$0 AND parent_id=$1 ORDER BY revision DESC LIMIT 20",
            r => CompanyJson.Decode<CompanySourceRefresh>(r.GetString(0)),
            CompanyId,
            profile.Id
        );
        return new(
            profile.Id,
            CompanyJson.Digest(fields),
            fields.Any(f => f.Status is "conflicting" or "outdated"),
            fields,
            runs
        );
    }

    private CompanyBusinessProfile ExactFreshnessProfile(StoreConnection db, CompanyCommand command)
    {
        var profile = Need<CompanyBusinessProfile>(db, command.Id);
        Version(profile.Version, command.ExpectedVersion);
        if (profile.State != "approved" || profile.Digest != command.Digest)
            throw new WorkspaceException(
                "Review the current approved brief before checking its sources or facts.",
                409
            );
        ConfirmedBusiness(db, profile.BusinessVersion);
        return profile;
    }

    private object StartBusinessRefresh(StoreConnection db, CompanyCommand command)
    {
        DemandWorkAdmission(db);
        var profile = ExactFreshnessProfile(db, command);
        var sources = command.Evidence ?? [];
        var cited = profile.Fields.SelectMany(f => f.Citations).Select(c => c.SourceId).ToHashSet();
        if (
            command.Decision != "refresh-approved-sources"
            || sources.Length is < 1 or > 3
            || sources.Distinct().Count() != sources.Length
            || sources.Any(id => !cited.Contains(id))
        )
            throw new WorkspaceException(
                "Choose one to three sources cited by this brief and authorize one refresh.",
                422
            );
        var business = Need<CompanyBusinessIdentity>(db, BusinessIdentityId);
        if (command.Scope is not null)
        {
            if (command.Scope != "scheduled-source" || !SourceScheduleCurrent(db, command.RequestId))
                throw new WorkspaceException("This scheduled source refresh no longer has authority.", 409);
            var scheduled = Need<CompanySchedule>(db, Need<CompanyScheduleRun>(db, command.RequestId).ScheduleId);
            if (
                scheduled.Definition.ProfileId != profile.Id
                || scheduled.ApprovedBy != (CurrentActor.Value?.Subject ?? _owner)
                || !(scheduled.Definition.SourceIds ?? []).SequenceEqual(sources)
            )
                throw new WorkspaceException("The source refresh differs from its approved recurring scope.", 409);
        }
        if (business.Website is null)
            throw new WorkspaceException("This business has no confirmed website to refresh.", 409);
        var runs = All<CompanySourceRefresh>(db);
        if (runs.Any(r => r.State == "running"))
            throw new WorkspaceException("A source refresh is already running. Check its saved status.", 409);
        if (runs.Count(r => r.StartedAt > Clock().AddHours(-1)) >= 3)
            throw new WorkspaceException(
                "This company can refresh sources three times per hour. Review the saved checks or try later.",
                429
            );
        var run = new CompanySourceRefresh(
            Id(),
            profile.Id,
            profile.Version,
            profile.Digest,
            profile.BusinessVersion,
            business.Website,
            CurrentActor.Value?.Subject ?? _owner,
            sources,
            "running",
            Clock(),
            ScheduleRunId: command.Scope is null ? null : command.RequestId
        );
        Save(db, run, run.Id, profile.Id, state: run.State, version: runs.Length + 1);
        Event(
            db,
            "business.refresh-started",
            run.Id,
            null,
            "The owner authorized one bounded source refresh. Approved facts remain unchanged."
        );
        return run;
    }

    private bool RefreshCurrent(StoreConnection db, CompanySourceRefresh run) =>
        run.State == "running"
        && (run.ScheduleRunId is null || SourceScheduleCurrent(db, run.ScheduleRunId))
        && Find<CompanyMember>(db, run.AuthorizedBy) is { State: "active", Role: CompanyRoles.Owner }
        && Find<CompanyBusinessProfile>(db, run.ProfileId) is { State: "approved" } profile
        && profile.Version == run.ProfileVersion
        && profile.Digest == run.ProfileDigest
        && Find<CompanyBusinessIdentity>(db, BusinessIdentityId) is { State: "confirmed" } business
        && business.Version == run.BusinessVersion;

    public bool MayRefreshBusinessSource(string id, string sourceId)
    {
        using var db = Open();
        var run = Need<CompanySourceRefresh>(db, id);
        return run.SourceIds.Contains(sourceId) && run.StartedAt >= Clock().AddMinutes(-2) && RefreshCurrent(db, run);
    }

    // Trusted integration completion only. Neither page text nor a browser command may manufacture a check.
    public CompanySourceRefresh CompleteBusinessRefresh(
        string id,
        string subject,
        CompanySourceCheckResult[] results,
        bool uncertain = false
    )
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var run = Need<CompanySourceRefresh>(db, id);
                if (subject != run.AuthorizedBy)
                    throw new WorkspaceException("Only this refresh's initiating owner can settle its result.", 403);
                if (run.State != "running")
                    return run;
                var current = RefreshCurrent(db, run);
                if (
                    results.Select(r => r.SourceId).Distinct().Count() != results.Length
                    || results.Any(r => !run.SourceIds.Contains(r.SourceId))
                )
                    throw new WorkspaceException("Refresh results must match the authorized sources.", 422);
                var checks = new List<CompanySourceCheck>();
                if (current)
                    foreach (var sourceId in run.SourceIds)
                    {
                        var result = results.SingleOrDefault(r => r.SourceId == sourceId);
                        var old = Need<CompanyBusinessSource>(db, sourceId);
                        var observed = result?.ObservedSourceId is { } observedId
                            ? Need<CompanyBusinessSource>(db, observedId)
                            : null;
                        if (observed is not null && observed.BusinessVersion != run.BusinessVersion)
                            throw new WorkspaceException(
                                "The refreshed source belongs to another business revision.",
                                409
                            );
                        var check = new CompanySourceCheck(
                            Id(),
                            run.Id,
                            run.ProfileId,
                            sourceId,
                            observed?.Id,
                            observed is null ? "unavailable"
                                : observed.Digest == old.Digest ? "unchanged"
                                : "changed",
                            Clock(),
                            observed is null
                                ? Text(result?.Notice ?? "This source did not return a complete readable result.", 500)
                                : null
                        );
                        var previous = Rows(
                                db,
                                "SELECT revision FROM co_business_checks WHERE company_id=$0 AND parent_id=$1 AND state=$2 ORDER BY revision DESC LIMIT 1",
                                r => r.GetInt32(0),
                                CompanyId,
                                run.ProfileId,
                                sourceId
                            )
                            .FirstOrDefault();
                        Save(db, check, check.Id, run.ProfileId, state: sourceId, version: previous + 1);
                        checks.Add(check);
                    }
                run = run with
                {
                    State =
                        !current ? "withheld"
                        : uncertain ? "unknown"
                        : checks.Any(c => c.Status == "unavailable") ? "partial"
                        : "completed",
                    CompletedAt = Clock(),
                    Checks = checks.ToArray(),
                };
                // Preserve the insertion order when settling; dates are not a sequence generator.
                var sequence = Rows(
                        db,
                        "SELECT revision FROM co_business_refreshes WHERE company_id=$0 AND id=$1",
                        r => r.GetInt32(0),
                        CompanyId,
                        id
                    )
                    .Single();
                Save(db, run, run.Id, run.ProfileId, state: run.State, version: sequence);
                Event(
                    db,
                    "business.refresh-" + run.State,
                    run.Id,
                    null,
                    "Source refresh " + run.State + ". No approved fact was overwritten."
                );
                return run;
            }
        );
    }

    private object ReviewBusinessFact(StoreConnection db, CompanyCommand command)
    {
        var profile = ExactFreshnessProfile(db, command);
        var input = CompanyJson.Decode<CompanyFactReviewInput>(command.Body ?? "null");
        var field =
            BusinessFreshness(db, profile).Fields.SingleOrDefault(f => f.Field == input.Field)
            ?? throw new WorkspaceException("Choose a field in this brief.", 422);
        if (input.EvidenceDigest != field.EvidenceDigest || input.PreviousReviewId != field.Review?.Id)
            throw new WorkspaceException(
                "The source evidence or owner review changed. Reload freshness before reviewing.",
                409
            );
        if (input.Status is not ("verified" or "possible" or "conflicting" or "outdated"))
            throw new WorkspaceException("Choose verified, possible change, conflicting or outdated.", 422);
        var note = Text(input.Note, 240).Trim();
        var quote = input.Quote is null ? null : Text(input.Quote, 600).Trim();
        if (note.Length == 0)
            throw new WorkspaceException("Explain this fact review for the team.", 422);
        if (input.Status == "verified")
        {
            if (
                input.SourceId is null
                || !field.Checks.Any(c => c.ObservedSourceId == input.SourceId)
                || string.IsNullOrWhiteSpace(quote)
                || !Need<CompanyBusinessSource>(db, input.SourceId).Text.Contains(quote, StringComparison.Ordinal)
            )
                throw new WorkspaceException(
                    "Verification needs an exact quote from a current checked source for this field.",
                    422
                );
        }
        else if (input.SourceId is not null || !string.IsNullOrEmpty(quote))
            throw new WorkspaceException(
                "Attach a checked quote when verifying a fact; use the note for other assessments.",
                422
            );
        var actor = CurrentActor.Value?.Subject ?? _owner;
        var review = new CompanyFactReview(
            Id(),
            profile.Id,
            input with
            {
                Note = note,
                Quote = quote,
            },
            Find<CompanyMember>(db, actor)?.Name ?? "Owner",
            Clock()
        );
        var sequence =
            Rows(
                    db,
                    "SELECT revision FROM co_business_fact_reviews WHERE company_id=$0 AND parent_id=$1 AND state=$2 ORDER BY revision DESC LIMIT 1",
                    r => r.GetInt32(0),
                    CompanyId,
                    profile.Id,
                    input.Field
                )
                .FirstOrDefault() + 1;
        Save(db, review, review.Id, profile.Id, state: input.Field, version: sequence);
        Event(
            db,
            "business.fact-reviewed",
            review.Id,
            null,
            "The owner marked " + input.Field + " " + input.Status + ". Approved wording was retained."
        );
        return review;
    }

    private bool BusinessFreshnessCurrent(StoreConnection db, CompanyContext context, CompanyTask task)
    {
        var profile = All<CompanyBusinessProfile>(db)
            .SingleOrDefault(p => p.State == "approved" && p.KnowledgeId == task.CompanyKnowledgeId);
        if (profile is null)
            return context.BusinessBrief is null;
        var freshness = BusinessFreshness(db, profile);
        return !freshness.Blocked
            && context.BusinessBrief?.Freshness?.Digest
                == (freshness.Fields.Any(f => f.Checks.Length > 0 || f.Review is not null) ? freshness.Digest : null);
    }

    private void RecoverBusinessRefreshes(StoreConnection db)
    {
        foreach (var run in All<CompanySourceRefresh>(db).Where(r => r.State == "running"))
        {
            var sequence = Rows(
                    db,
                    "SELECT revision FROM co_business_refreshes WHERE company_id=$0 AND id=$1",
                    r => r.GetInt32(0),
                    CompanyId,
                    run.Id
                )
                .Single();
            Save(
                db,
                run with
                {
                    State = "unknown",
                    CompletedAt = Clock(),
                },
                run.Id,
                run.ProfileId,
                state: "unknown",
                version: sequence
            );
            Event(
                db,
                "business.refresh-unknown",
                run.Id,
                null,
                "An interrupted source refresh was not replayed. Check saved evidence before another request."
            );
        }
    }
}
