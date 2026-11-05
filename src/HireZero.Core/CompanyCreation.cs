namespace HireZero.Core;

public sealed record CompanyCreation(string Id, string Name, string Role);

public sealed partial class CompanyStore
{
    // Transaction-only view of a just-inserted company; never opens another connection or runs bootstrap.
    private CompanyStore(CompanyStore source, string companyId, string owner)
    {
        _database = source._database;
        _roles = source._roles;
        _modelRouting = source._modelRouting;
        _measuredRuntimeIdentity = source._measuredRuntimeIdentity;
        _owner = owner;
        CompanyId = companyId;
        Clock = source.Clock;
    }

    // Keep the receipt inside the new company. Its name is nobody else's dinner-table conversation.
    private static (string Id, string RequestId) CreationIdentity(string requestId, string subject)
    {
        if (!Guid.TryParseExact(requestId, "D", out var parsed))
            throw new WorkspaceException("A UUID operation ID is required.", 422);
        var normalized = parsed.ToString("D");
        return (
            "co-"
                + CompanyJson.Digest(
                    new
                    {
                        purpose = "company-create-v1",
                        subject,
                        requestId = normalized,
                    }
                )[..40],
            normalized
        );
    }

    public CompanyCreation CreateCompany(string name, string requestId, string subject)
    {
        // Validate before writing anything; a refused name must not leave a phantom company.
        var clean = Text(name, 80);
        var identity = CreationIdentity(requestId, subject);
        var digest = CompanyJson.Digest(
            new
            {
                action = "company.create",
                name = clean,
                subject,
            }
        );
        using var db = Open();
        return db.Transaction(
            "schema",
            () =>
            {
                if (
                    Scalar(db, "SELECT company_id FROM co_signups WHERE subject=$0", subject) is not null
                    || Scalar(db, "SELECT subject FROM co_signups WHERE company_id=$0", CompanyId) is not null
                )
                    throw new WorkspaceException("Free sign-up includes one company per account.", 403);
                var member = Find<CompanyMember>(db, subject);
                if (member is not { State: "active", Role: CompanyRoles.Owner })
                    throw new WorkspaceException("An active company owner must create another company.", 403);
                if (Scalar(db, "SELECT owner FROM co_company WHERE id=$0", identity.Id) is not null)
                {
                    if (
                        Scalar(
                            db,
                            "SELECT digest FROM co_operations WHERE company_id=$0 AND id=$1",
                            identity.Id,
                            identity.RequestId
                        ) != digest
                    )
                        throw new WorkspaceException(
                            "That creation request belongs to different saved input. Check the saved company.",
                            409
                        );
                    return ReadCreatedCompany(db, identity.Id, identity.RequestId, subject)
                        ?? throw new WorkspaceException(
                            "The saved company is no longer available to this member.",
                            403
                        );
                }

                var created = new CompanyCreation(identity.Id, clean, CompanyRoles.Owner);
                var owner = member with { Role = CompanyRoles.Owner, Version = 1 };
                Sql(db, "INSERT INTO co_company(id,owner,name) VALUES($0,$1,$2)", identity.Id, subject, clean);
                var destination = new CompanyStore(this, identity.Id, subject);
                var previous = CurrentActor.Value;
                try
                {
                    CurrentActor.Value = new(subject, CompanyRoles.Owner);
                    destination.Save(db, owner, owner.Id, state: owner.State, version: owner.Version);
                    destination.Event(db, "company.created", identity.Id, null, "Company created.");
                }
                finally
                {
                    CurrentActor.Value = previous;
                }
                Sql(
                    db,
                    "INSERT INTO co_operations(company_id,id,digest,result) VALUES($0,$1,$2,$3)",
                    identity.Id,
                    identity.RequestId,
                    digest,
                    CompanyJson.Encode(created)
                );
                return created;
            }
        );
    }

    public CompanyCreation? CreatedCompany(string requestId, string subject)
    {
        var identity = CreationIdentity(requestId, subject);
        using var db = Open();
        return db.Transaction(
            "schema",
            () =>
            {
                if (Find<CompanyMember>(db, subject) is not { State: "active" })
                    throw new WorkspaceException("You are not a member of this company.", 403);
                return ReadCreatedCompany(db, identity.Id, identity.RequestId, subject);
            }
        );
    }

    private static CompanyCreation? ReadCreatedCompany(StoreConnection db, string id, string requestId, string subject)
    {
        if (
            Scalar(db, "SELECT owner FROM co_company WHERE id=$0", id) != subject
            || Scalar(db, "SELECT result FROM co_operations WHERE company_id=$0 AND id=$1", id, requestId) is null
        )
            return null;
        var member = Rows(
                db,
                "SELECT payload FROM co_members WHERE company_id=$0 AND id=$1",
                row => CompanyJson.Decode<CompanyMember>(row.GetString(0)),
                id,
                subject
            )
            .SingleOrDefault();
        return member is { State: "active" }
            ? new(id, Scalar(db, "SELECT name FROM co_company WHERE id=$0", id) ?? DefaultName, member.Role)
            : throw new WorkspaceException("The company was created, but your access is no longer active.", 403);
    }
}
