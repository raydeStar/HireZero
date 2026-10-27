namespace HireZero.Core;

public sealed record CompanyProfileRevision(
    string Id,
    int Sequence,
    CompanyBusinessProfile Profile,
    string Origin,
    string ActorName,
    DateTimeOffset RecordedAt,
    string[] ChangedFields,
    string? DraftId = null
);

public sealed record CompanyProfileHistory(CompanyProfileRevision[] Items, int? Before);

public sealed record CompanyBusinessEvidence(
    string Id,
    string Url,
    string Title,
    string Digest,
    DateTimeOffset RetrievedAt,
    bool Clipped
);

public sealed record CompanyBusinessProvenance(
    string ProfileId,
    string Digest,
    int BusinessVersion,
    DateTimeOffset ApprovedAt,
    CompanyProfileField[] Fields,
    CompanyBusinessEvidence[] Sources,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyBriefFreshness? Freshness = null
);

public sealed record CompanyFactState(string Field, string Status, string? OwnerNote, DateTimeOffset? CheckedAt);

public sealed record CompanyBriefFreshness(string Digest, CompanyFactState[] Fields);

public sealed partial class CompanyStore
{
    private const string BusinessHistorySchema =
        "CREATE TABLE co_business_history(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),UNIQUE(company_id,revision),FOREIGN KEY(company_id,parent_id) REFERENCES co_business_profiles(company_id,id));";

    public CompanyProfileHistory BusinessProfileHistory(int? before = null)
    {
        using var db = Open();
        var rows = Rows(
            db,
            "SELECT payload FROM co_business_history WHERE company_id=$0 AND revision<$1 ORDER BY revision DESC LIMIT 21",
            r => CompanyJson.Decode<CompanyProfileRevision>(r.GetString(0)),
            CompanyId,
            before ?? int.MaxValue
        );
        return new(rows.Take(20).ToArray(), rows.Length > 20 ? rows[19].Sequence : null);
    }

    private void PreserveEarlierProfile(StoreConnection db, CompanyBusinessProfile? profile)
    {
        if (profile is null || Find<CompanyProfileRevision>(db, profile.Id + ":" + profile.Version) is not null)
            return;
        RecordProfileRevision(db, profile, "earlier", null);
    }

    // Append an immutable snapshot alongside the mutation. Even a butler cannot rewrite yesterday's ledger.
    private void RecordProfileRevision(
        StoreConnection db,
        CompanyBusinessProfile profile,
        string origin,
        string? draftId = null
    )
    {
        var last = Rows(
                db,
                "SELECT payload FROM co_business_history WHERE company_id=$0 ORDER BY revision DESC LIMIT 1",
                r => CompanyJson.Decode<CompanyProfileRevision>(r.GetString(0)),
                CompanyId
            )
            .FirstOrDefault();
        var prior = Rows(
                db,
                "SELECT payload FROM co_business_history WHERE company_id=$0 AND parent_id=$1 ORDER BY revision DESC LIMIT 1",
                r => CompanyJson.Decode<CompanyProfileRevision>(r.GetString(0)),
                CompanyId,
                profile.Id
            )
            .FirstOrDefault();
        var baseline =
            prior?.Profile ?? (last?.Profile.BusinessVersion == profile.BusinessVersion ? last.Profile : null);
        var actor = CurrentActor.Value?.Subject ?? _owner;
        var revision = new CompanyProfileRevision(
            profile.Id + ":" + profile.Version,
            (last?.Sequence ?? 0) + 1,
            profile,
            origin,
            origin == "model" ? "Profile drafting service"
                : origin == "earlier" ? "Earlier author unavailable"
                : Find<CompanyMember>(db, actor)?.Name ?? "Owner",
            Clock(),
            profile
                .Fields.Where(f =>
                    CompanyJson.Encode(f) != CompanyJson.Encode(baseline?.Fields.SingleOrDefault(p => p.Key == f.Key))
                )
                .Select(f => f.Key)
                .ToArray(),
            draftId
        );
        // No update path: repeated operation receipts must not invent another edit.
        if (Find<CompanyProfileRevision>(db, revision.Id) is not null)
            throw new WorkspaceException("This brief revision was already recorded.", 409);
        Save(db, revision, revision.Id, profile.Id, state: profile.State, version: revision.Sequence);
    }

    private CompanyBusinessProvenance? BusinessProvenance(StoreConnection db, string? knowledgeId)
    {
        if (knowledgeId is null || Find<CompanyKnowledge>(db, knowledgeId)?.State != "approved")
            return null;
        var profile = All<CompanyBusinessProfile>(db)
            .SingleOrDefault(p => p.KnowledgeId == knowledgeId && p.State == "approved");
        if (profile is null)
            return null;
        var sources = profile
            .Fields.SelectMany(f => f.Citations)
            .Select(c => c.SourceId)
            .Distinct()
            .Select(id => Need<CompanyBusinessSource>(db, id))
            .Select(s => new CompanyBusinessEvidence(s.Id, s.Url, s.Title, s.Digest, s.RetrievedAt, s.Clipped))
            .ToArray();
        var freshness = BusinessFreshness(db, profile);
        if (freshness.Blocked)
            throw new WorkspaceException(
                "An owner marked a business fact conflicting or outdated. Open Business brief, review freshness and resolve or replace the brief before new work.",
                409
            );
        var states = freshness.Fields.Where(f => f.Checks.Length > 0 || f.Review is not null).ToArray();
        return new(
            profile.Id,
            profile.Digest,
            profile.BusinessVersion,
            profile.UpdatedAt,
            profile.Fields,
            sources,
            states.Length == 0
                ? null
                : new(
                    freshness.Digest,
                    states
                        .Select(f => new CompanyFactState(
                            f.Field,
                            f.Status,
                            f.Review?.Review.Note,
                            f.Checks.Select(c => (DateTimeOffset?)c.CheckedAt).DefaultIfEmpty(null).Max()
                        ))
                        .ToArray()
                )
        );
    }
}
