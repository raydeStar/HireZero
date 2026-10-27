namespace HireZero.Core;

public sealed record CompanyBusinessInput(
    string Name,
    string? Website,
    string Goal,
    string? Location = null,
    string? Industry = null
);

public sealed record CompanyBusinessIdentity(
    string Id,
    string Name,
    string? Website,
    string Goal,
    string Location,
    string Industry,
    string State,
    int Version,
    string Digest,
    DateTimeOffset UpdatedAt
);

public sealed record CompanyBusinessSource(
    string Id,
    int BusinessVersion,
    string Url,
    string Title,
    string Text,
    string Digest,
    DateTimeOffset RetrievedAt,
    bool Clipped
);

public sealed record CompanyProfileCitation(string SourceId, string Quote);

public sealed record CompanyProfileField(
    string Key,
    string Value,
    string Verification,
    CompanyProfileCitation[] Citations
);

public sealed record CompanyProfileInput(int BusinessVersion, CompanyProfileField[] Fields);

public sealed record CompanyBusinessProfile(
    string Id,
    int BusinessVersion,
    CompanyProfileField[] Fields,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    string? KnowledgeId = null
);

public sealed partial class CompanyStore
{
    public static readonly string[] BusinessProfileFields =
    [
        "business",
        "services",
        "customers",
        "voice",
        "market",
        "competitors",
        "opportunities",
        "goals",
        "boundaries",
    ];
    public const int BusinessSourceCharacters = 6000;
    private const string BusinessIdentityId = "business";
    private const string BusinessContextSchema =
        "CREATE TABLE co_business(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));"
        + "CREATE TABLE co_business_sources(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_business(company_id,id));"
        + "CREATE TABLE co_business_profiles(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_business(company_id,id));";

    public CompanyBusinessIdentity? BusinessIdentity()
    {
        using var db = Open();
        return Find<CompanyBusinessIdentity>(db, BusinessIdentityId);
    }

    public CompanyBusinessSource BusinessSource(string id)
    {
        using var db = Open();
        return Need<CompanyBusinessSource>(db, id);
    }

    // Called after a trusted integration read, never a client-supplied scraped-content command.
    // Unchanged evidence keeps its original discovery date; the butler does not forge fresh receipts.
    public CompanyBusinessSource RecordBusinessSource(
        string subject,
        int businessVersion,
        string url,
        string title,
        string text,
        bool clipped
    )
    {
        var actor = Actor(subject);
        CompanyAccess.Demand(actor, CompanyRoles.Owner, "record business research");
        var previous = CurrentActor.Value;
        CurrentActor.Value = actor;
        try
        {
            using var db = Open();
            return Transaction(
                db,
                () =>
                {
                    ConfirmedBusiness(db, businessVersion);
                    var address =
                        BusinessUrl(url) ?? throw new WorkspaceException("A source needs an https address.", 422);
                    var clean = Text(text, BusinessSourceCharacters).Trim();
                    if (clean.Length == 0)
                        throw new WorkspaceException("The source contains no readable evidence.", 422);
                    var digest = ContentGuard.Digest(clean);
                    var existing = All<CompanyBusinessSource>(db)
                        .FirstOrDefault(s =>
                            s.BusinessVersion == businessVersion && s.Url == address && s.Digest == digest
                        );
                    if (existing is not null)
                        return existing;
                    var source = new CompanyBusinessSource(
                        Id(),
                        businessVersion,
                        address,
                        string.IsNullOrWhiteSpace(title) ? address : Text(title, 200),
                        clean,
                        digest,
                        Clock(),
                        clipped
                    );
                    Save(db, source, source.Id, BusinessIdentityId);
                    Event(
                        db,
                        "business.source-recorded",
                        source.Id,
                        null,
                        "A business source was recorded for review."
                    );
                    return source;
                }
            );
        }
        finally
        {
            CurrentActor.Value = previous;
        }
    }

    private static string? BusinessUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = Text(value, 800);
        if (
            value.Length > 800
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length > 0
            || uri.Host.Length == 0
        )
            throw new WorkspaceException("Use an https website address without credentials, or leave it blank.", 422);
        return new UriBuilder(uri) { Fragment = "" }
            .Uri
            .AbsoluteUri;
    }

    private CompanyBusinessIdentity ConfirmedBusiness(StoreConnection db, int version)
    {
        var identity = Need<CompanyBusinessIdentity>(db, BusinessIdentityId);
        Version(identity.Version, version);
        if (identity.State != "confirmed")
            throw new WorkspaceException(
                "Confirm the correct business before importing or approving its research.",
                409
            );
        return identity;
    }

    private object IdentifyBusiness(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyBusinessInput>(command.Body ?? "null");
        var old = Find<CompanyBusinessIdentity>(db, BusinessIdentityId);
        Version(old?.Version ?? 0, command.ExpectedVersion);
        if (All<CompanyBusinessProfile>(db).Any(p => p.State == "approved"))
            throw new WorkspaceException(
                "Review and explicitly revoke the approved business brief before changing this identity.",
                409
            );
        var name = Text(input.Name, 100).Trim();
        var goal = Text(input.Goal, 1000).Trim();
        if (name.Length == 0 || goal.Length == 0)
            throw new WorkspaceException("Name the business and what you want help accomplishing.", 422);
        var clean = new CompanyBusinessInput(
            name,
            BusinessUrl(input.Website),
            goal,
            string.IsNullOrWhiteSpace(input.Location) ? "" : Text(input.Location, 200).Trim(),
            string.IsNullOrWhiteSpace(input.Industry) ? "" : Text(input.Industry, 200).Trim()
        );
        var identity = new CompanyBusinessIdentity(
            BusinessIdentityId,
            clean.Name,
            clean.Website,
            clean.Goal,
            clean.Location!,
            clean.Industry!,
            "unconfirmed",
            (old?.Version ?? 0) + 1,
            CompanyJson.Digest(clean),
            Clock()
        );
        Save(db, identity, identity.Id, state: identity.State, version: identity.Version);
        Event(db, "business.identified", identity.Id, null, "Business identity is ready for owner confirmation.");
        return identity;
    }

    private object ConfirmBusiness(StoreConnection db, CompanyCommand command)
    {
        var identity = Need<CompanyBusinessIdentity>(db, BusinessIdentityId);
        Version(identity.Version, command.ExpectedVersion);
        if (identity.Digest != command.Digest || identity.State != "unconfirmed")
            throw new WorkspaceException("Confirm the exact business identity shown for review.", 409);
        identity = identity with { State = "confirmed", Version = identity.Version + 1, UpdatedAt = Clock() };
        Save(db, identity, identity.Id, state: identity.State, version: identity.Version);
        Event(db, "business.confirmed", identity.Id, null, "The owner confirmed the business identity.");
        return identity;
    }
}
