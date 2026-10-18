using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Publishing a reviewed execution's changes (docs/adr/0012-company-publication.md). The module proposes the exact local
// write calls (such as staging and committing) and describes where the result goes; the owner approves that proposal
// once the change's artifact is approved. Each local write is then authorized only as the next proposed call, and the
// single external call (such as a push) only after the module binds it, once. An external call whose outcome is not
// confirmed becomes unknown and is never replayed.
public sealed record CompanyPublicationProposal(string GrantId, string ArtifactId, string Summary, string[] Calls);

public sealed record CompanyPublication(
    string Id,
    string GrantId,
    string TaskId,
    string EmployeeId,
    string ArtifactId,
    string ReviewDigest,
    string Summary,
    string[] Calls,
    string Digest,
    string State,
    DateTimeOffset ExpiresAt,
    int Used = 0,
    string? Result = null,
    string? ExternalDigest = null,
    bool ExternalSent = false,
    string? Reason = null,
    int Version = 1
);

public sealed partial class CompanyStore
{
    public const string PublicationAuthorityKind = "company-publication";

    // The verifier hosts register so publication calls can be authorized against this store.
    public IToolAuthorityVerifier PublicationAuthority => new PublicationVerifier(this);

    public CompanyPublication ProposePublication(CompanyPublicationProposal proposal)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var g = Need<CompanyExecutionGrant>(db, proposal.GrantId);
                var a = Need<CompanyArtifact>(db, proposal.ArtifactId);
                if (
                    g.State != "completed"
                    || g.Review is not { Files.Length: > 0 } review
                    || a.TaskId != g.TaskId
                    || !a.Body.Contains(review.Digest, StringComparison.Ordinal)
                    || a.State is not ("needs_review" or "approved")
                )
                    throw new WorkspaceException("Only a completed, reviewed change can be proposed for publication.");
                if (
                    proposal.Calls.Length is 0 or > 8
                    || proposal.Calls.Any(c => c.Length != 64 || !c.All(Uri.IsHexDigit))
                )
                    throw new WorkspaceException("A publication needs between one and eight exact call digests.");
                if (All<CompanyPublication>(db).Any(p => p.GrantId == g.Id && p.State is not ("revoked" or "failed")))
                    throw new WorkspaceException("This change already has a publication.");
                var summary = Text(proposal.Summary, 600);
                var id = Id();
                var p = new CompanyPublication(
                    id,
                    g.Id,
                    g.TaskId,
                    g.EmployeeId,
                    a.Id,
                    review.Digest,
                    summary,
                    proposal.Calls,
                    CompanyJson.Digest(proposal with { Summary = summary }),
                    "proposed",
                    Clock().AddHours(1)
                );
                Save(db, p, id, p.TaskId, p.EmployeeId, p.State, p.Version);
                Event(db, "publication.proposed", id, p.EmployeeId, "Publication proposed: " + summary);
                return p;
            }
        );
    }

    public CompanyPublication Publication(string id)
    {
        using var db = Open();
        return Need<CompanyPublication>(db, id);
    }

    public CompanyArtifact Artifact(string id)
    {
        using var db = Open();
        return Need<CompanyArtifact>(db, id);
    }

    public CompanyArtifact[] ArtifactsOf(string taskId)
    {
        using var db = Open();
        return [.. All<CompanyArtifact>(db).Where(a => a.TaskId == taskId)];
    }

    private CompanyPublication ReviewPublication(SqliteConnection db, CompanyCommand c)
    {
        var p = Need<CompanyPublication>(db, c.Id);
        Version(p.Version, c.ExpectedVersion);
        if (c.Decision == "revoke")
        {
            if (p.State is not ("proposed" or "approved"))
                throw new WorkspaceException("This publication has already started.");
            return Put(db, p with { State = "revoked", Version = p.Version + 1 }, "Owner revoked the publication.");
        }
        var artifact = Need<CompanyArtifact>(db, p.ArtifactId);
        if (
            c.Decision != "approve"
            || p.State != "proposed"
            || p.Digest != c.Digest
            || Clock() >= p.ExpiresAt
            || !Unchanged(db, p)
        )
            throw new WorkspaceException("Approve the exact current, unexpired publication.");
        if (artifact.State != "approved")
            throw new WorkspaceException("Approve the reviewed change before its publication.");
        return Put(
            db,
            p with
            {
                State = "approved",
                Version = p.Version + 1,
            },
            "Owner approved one publication: " + p.Summary
        );
    }

    // Starts an approved publication, once. Its local calls may run after this.
    public CompanyPublication ClaimPublication(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var p = Need<CompanyPublication>(db, id);
                if (p.State != "approved" || Clock() >= p.ExpiresAt || !Unchanged(db, p))
                    throw new WorkspaceException("This publication is not approved, current and unstarted.");
                return Put(db, p with { State = "committing", Version = p.Version + 1 }, "Publication started.");
            }
        );
    }

    // After every local call ran, the module binds its one external call (for example a push of the new commit).
    public CompanyPublication BindPublication(string id, string result, string externalDigest)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var p = Need<CompanyPublication>(db, id);
                if (
                    p.State != "committing"
                    || p.Used != p.Calls.Length
                    || externalDigest.Length != 64
                    || !externalDigest.All(Uri.IsHexDigit)
                )
                    throw new WorkspaceException("Every approved local call must run before the external call.");
                return Put(
                    db,
                    p with
                    {
                        State = "pushing",
                        Result = Text(result, 100),
                        ExternalDigest = externalDigest,
                        Version = p.Version + 1,
                    },
                    "Publication ready to send."
                );
            }
        );
    }

    // Records how a started publication ended. Without the external call it can only fail; once that call was
    // authorized it is either confirmed published or unknown, never retried.
    public CompanyPublication FinishPublication(string id, bool confirmed, string? reason = null)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var p = Need<CompanyPublication>(db, id);
                if (p.State is not ("committing" or "pushing"))
                    return p;
                var state =
                    !p.ExternalSent ? "failed"
                    : confirmed ? "published"
                    : "unknown";
                return Put(
                    db,
                    p with
                    {
                        State = state,
                        Reason = reason is null ? null : Text(reason, 300),
                        Version = p.Version + 1,
                    },
                    state == "published" ? "Published: " + p.Summary
                        : state == "unknown" ? "The publication's outcome is unknown; it will not be retried."
                        : "The publication stopped before anything was sent."
                );
            }
        );
    }

    // A read-only reconciliation found the published result, so an unknown publication is confirmed.
    public CompanyPublication ConfirmPublication(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var p = Need<CompanyPublication>(db, id);
                if (p.State != "unknown")
                    return p;
                return Put(
                    db,
                    p with
                    {
                        State = "published",
                        Version = p.Version + 1,
                    },
                    "Reconciled: published " + p.Summary
                );
            }
        );
    }

    private void RecoverPublications(SqliteConnection db)
    {
        foreach (var p in All<CompanyPublication>(db).Where(p => p.State is "committing" or "pushing"))
            Put(
                db,
                p with
                {
                    State = p.ExternalSent ? "unknown" : "failed",
                    Reason = "publication-interrupted",
                    Version = p.Version + 1,
                },
                "A publication was interrupted by a restart; it will not be retried."
            );
    }

    // The change the owner approved is still the one recorded: the grant's review and the approved artifact.
    private static bool Unchanged(SqliteConnection db, CompanyPublication p)
    {
        var g = Need<CompanyExecutionGrant>(db, p.GrantId);
        var a = Need<CompanyArtifact>(db, p.ArtifactId);
        var task = Need<CompanyTask>(db, p.TaskId);
        return g.Review?.Digest == p.ReviewDigest
            && a.State is "needs_review" or "approved"
            && task.State is not ("canceled" or "superseded");
    }

    private CompanyPublication Put(SqliteConnection db, CompanyPublication p, string text)
    {
        Save(db, p, p.Id, p.TaskId, p.EmployeeId, p.State, p.Version);
        Event(db, "publication." + p.State, p.Id, p.EmployeeId, text);
        return p;
    }

    // Consumes the authority for one call: a Write must be the next proposed local call, an External the bound
    // external call. Consumption is recorded before the call runs, so a failed call is never authorized again.
    private bool AuthorizePublication(ToolInvocation invocation)
    {
        if (invocation.Authority is not { Kind: PublicationAuthorityKind } authority)
            return false;
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var p = Find<CompanyPublication>(db, authority.Id);
                if (
                    p is null
                    || invocation.Caller.EmployeeId != p.EmployeeId
                    || invocation.Caller.TaskId != p.TaskId
                    || !Unchanged(db, p)
                )
                    return false;
                if (
                    invocation.Contract.Risk == ToolRisk.Write
                    && p.State == "committing"
                    && p.Used < p.Calls.Length
                    && p.Calls[p.Used] == invocation.InputDigest
                )
                {
                    Save(
                        db,
                        p with
                        {
                            Used = p.Used + 1,
                            Version = p.Version + 1,
                        },
                        p.Id,
                        p.TaskId,
                        p.EmployeeId,
                        p.State,
                        p.Version + 1
                    );
                    return true;
                }
                if (
                    invocation.Contract.Risk == ToolRisk.External
                    && p.State == "pushing"
                    && !p.ExternalSent
                    && p.ExternalDigest == invocation.InputDigest
                )
                {
                    Put(db, p with { ExternalSent = true, Version = p.Version + 1 }, "Publication sent: " + p.Summary);
                    return true;
                }
                return false;
            }
        );
    }

    private sealed class PublicationVerifier(CompanyStore store) : IToolAuthorityVerifier
    {
        public string Kind => PublicationAuthorityKind;

        public bool Authorizes(ToolInvocation invocation) => store.AuthorizePublication(invocation);
    }
}
