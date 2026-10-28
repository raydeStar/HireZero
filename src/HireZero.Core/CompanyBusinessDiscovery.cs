namespace HireZero.Core;

public sealed record CompanyWebsiteCandidate(string Url, string Title, string Description);

public sealed record CompanyBusinessLookup(
    string Id,
    string Kind,
    int BusinessVersion,
    string BusinessDigest,
    string Query,
    string? Website,
    string State,
    DateTimeOffset StartedAt,
    bool Dispatched = false,
    string[]? SourceIds = null,
    CompanyWebsiteCandidate[]? Candidates = null,
    string[]? Notices = null,
    string? AuthorizedBy = null
);

public sealed partial class CompanyStore
{
    private const string BusinessLookupSchema =
        "CREATE TABLE co_business_lookups(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_business(company_id,id));";

    public CompanyBusinessLookup BusinessLookup(string id)
    {
        using var db = Open();
        return Need<CompanyBusinessLookup>(db, id);
    }

    private object StartBusinessLookup(StoreConnection db, CompanyCommand command)
    {
        DemandWorkAdmission(db);
        var identity = Need<CompanyBusinessIdentity>(db, BusinessIdentityId);
        Version(identity.Version, command.ExpectedVersion);
        var goal = command.Action == "goal.lookup.start";
        var kind = goal ? "discovery" : command.Body;
        var website = goal ? BusinessUrl(command.Body) : identity.Website;
        if (
            identity.Digest != command.Digest
            || kind is not ("search" or "discovery")
            || command.Decision != "authorize-one-lookup"
        )
            throw new WorkspaceException("Review the current business and authorize one lookup.", 409);
        if (kind == "discovery" && (identity.State != "confirmed" || website is null))
            throw new WorkspaceException("Confirm the business website before discovering its pages.", 409);
        var recent = All<CompanyBusinessLookup>(db).Where(r => r.StartedAt > Clock().AddHours(-1)).ToArray();
        if (recent.Any(r => r.State == "running"))
            throw new WorkspaceException("A business lookup is already running. Check its saved status.", 409);
        if (recent.Count(r => r.Kind == kind) >= 3)
            throw new WorkspaceException(
                "This company has reached its limit of three lookups of this kind per hour. Use the saved results or try later.",
                429
            );
        var query = string.Join(
            " ",
            new[] { identity.Name, identity.Location, identity.Industry }.Where(s => !string.IsNullOrWhiteSpace(s))
        );
        var lookup = new CompanyBusinessLookup(
            Id(),
            kind,
            identity.Version,
            identity.Digest,
            query,
            website,
            "running",
            Clock(),
            AuthorizedBy: CurrentActor.Value?.Subject ?? _owner
        );
        Save(db, lookup, lookup.Id, BusinessIdentityId, state: lookup.State);
        Event(
            db,
            "business.lookup-started",
            lookup.Id,
            null,
            "The owner authorized one bounded " + lookup.Kind + " request. It is never replayed automatically."
        );
        return lookup;
    }

    // The host's search authority calls this once before the metered tool. No caller-provided query can broaden it.
    public bool ConsumeBusinessSearch(string id, string query)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var run = Find<CompanyBusinessLookup>(db, id);
                var identity = Find<CompanyBusinessIdentity>(db, BusinessIdentityId);
                if (
                    run is not { Kind: "search", State: "running", Dispatched: false }
                    || run.StartedAt < Clock().AddMinutes(-2)
                    || run.Query != query
                    || run.AuthorizedBy is null
                    || Find<CompanyMember>(db, run.AuthorizedBy) is not { State: "active", Role: CompanyRoles.Owner }
                    || identity?.Version != run.BusinessVersion
                    || identity.Digest != run.BusinessDigest
                )
                    return false;
                run = run with { Dispatched = true };
                Save(db, run, run.Id, BusinessIdentityId, state: run.State);
                Event(
                    db,
                    "business.search-dispatched",
                    run.Id,
                    null,
                    "One search API request was consumed. Its provider may charge for this request."
                );
                return true;
            }
        );
    }

    public CompanyBusinessLookup CompleteBusinessLookup(
        string id,
        string subject,
        string[] sourceIds,
        CompanyWebsiteCandidate[] candidates,
        string[] notices,
        bool uncertain = false
    )
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var run = Need<CompanyBusinessLookup>(db, id);
                // Trusted integration completion may close a revoked initiator's in-flight request, but withholds its data.
                // Other callers still need current owner authority; this method is not a client command.
                if (
                    subject != run.AuthorizedBy
                    && Find<CompanyMember>(db, subject) is not { State: "active", Role: CompanyRoles.Owner }
                )
                    throw new WorkspaceException(
                        "Only the initiating owner or a current owner can settle this lookup.",
                        403
                    );
                if (run.State != "running")
                    return run;
                var identity = Find<CompanyBusinessIdentity>(db, BusinessIdentityId);
                var stale =
                    identity?.Version != run.BusinessVersion
                    || identity.Digest != run.BusinessDigest
                    || run.AuthorizedBy is null
                    || Find<CompanyMember>(db, run.AuthorizedBy) is not { State: "active", Role: CompanyRoles.Owner };
                if (sourceIds.Length > 3 || candidates.Length > 5 || notices.Length > 8)
                    throw new WorkspaceException("Research exceeded its saved result limits.", 422);
                if (run.Kind == "search" && sourceIds.Length != 0 || run.Kind == "discovery" && candidates.Length != 0)
                    throw new WorkspaceException("Search candidates and imported sources must stay separate.", 422);
                foreach (var sourceId in sourceIds)
                    if (Need<CompanyBusinessSource>(db, sourceId).BusinessVersion != run.BusinessVersion)
                        throw new WorkspaceException("A source belongs to another business version.", 409);
                var cleaned = candidates
                    .Select(c => new CompanyWebsiteCandidate(
                        BusinessUrl(c.Url) ?? throw new WorkspaceException("Candidate URL is missing.", 422),
                        ContentGuard.Clean(c.Title, 160).Text,
                        ContentGuard.Clean(c.Description, 350).Text
                    ))
                    .ToArray();
                run = run with
                {
                    State =
                        stale ? "withheld"
                        : uncertain ? "unknown"
                        : notices.Length > 0 ? "partial"
                        : "completed",
                    SourceIds = stale ? [] : sourceIds.Distinct().ToArray(),
                    Candidates = stale ? [] : cleaned,
                    Notices = stale
                        ?
                        [
                            "The business identity or authorizing membership changed. These results were withheld; review the current business before another lookup.",
                        ]
                        : notices.Select(n => ContentGuard.Clean(n, 500).Text).ToArray(),
                };
                Save(db, run, run.Id, BusinessIdentityId, state: run.State);
                Event(
                    db,
                    "business.lookup-" + run.State,
                    run.Id,
                    null,
                    "Business lookup " + run.State + ". Results are evidence for review, never approved facts."
                );
                return run;
            }
        );
    }

    private void RecoverBusinessLookups(StoreConnection db)
    {
        foreach (var run in All<CompanyBusinessLookup>(db).Where(r => r.State == "running"))
        {
            var stopped = run with
            {
                State = "unknown",
                Notices = ["The host stopped before a complete result was saved. This request will not be replayed."],
            };
            Save(db, stopped, stopped.Id, BusinessIdentityId, state: stopped.State);
            Event(
                db,
                "business.lookup-unknown",
                run.Id,
                null,
                "An interrupted lookup was fenced. No automatic replay."
            );
        }
    }
}
