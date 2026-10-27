namespace HireZero.Core;

public sealed record CompanyDocumentRetention(string Id, int DiscardedDays = 0, int Version = 0);

public sealed record CompanyDocumentRetentionInput(int DiscardedDays);

public sealed partial class CompanyStore
{
    private const string DocumentRetentionSchema =
        "CREATE TABLE co_document_retention(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    public CompanyDocumentRetention DocumentRetention()
    {
        using var db = Open();
        return DocumentRetention(db);
    }

    private CompanyDocumentRetention DocumentRetention(StoreConnection db) =>
        Find<CompanyDocumentRetention>(db, "documents") ?? new("documents");

    private CompanyDocumentRetention ConfigureDocumentRetention(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyDocumentRetentionInput>(command.Body ?? "null");
        if (input.DiscardedDays is not (0 or 7 or 30 or 90))
            throw new WorkspaceException("Keep discarded drafts, or delete them after 7, 30 or 90 days.", 422);
        if (command.Decision != "apply-retention")
            throw new WorkspaceException("Confirm this policy, including deletion of already expired drafts.", 422);
        var current = DocumentRetention(db);
        Version(current.Version, command.ExpectedVersion);
        var policy = current with { DiscardedDays = input.DiscardedDays, Version = current.Version + 1 };
        Save(db, policy, policy.Id, version: policy.Version);
        Audit(db, "document.retention", policy.Id, new { policy.DiscardedDays, policy.Version });
        MaintainDocumentRetention(db, policy);
        return policy;
    }

    private CompanyDocument DeleteUnapprovedDocument(StoreConnection db, CompanyCommand command)
    {
        var document = Need<CompanyDocument>(db, command.Id);
        Version(document.Version, command.ExpectedVersion);
        if (command.Digest != document.Digest || command.Decision != "delete-unapproved")
            throw new WorkspaceException("Confirm deletion of this exact unapproved document version.", 409);
        return EraseUnapprovedDocument(db, document, "owner");
    }

    private CompanyDocument EraseUnapprovedDocument(StoreConnection db, CompanyDocument document, string reason)
    {
        // Only these immutable transitions prove that no employee packet could have included this document.
        if (document.State is not ("draft" or "discarded"))
            throw new WorkspaceException(
                "Only never-approved drafts can be deleted here. Previously approved documents may have saved task copies.",
                409
            );
        return EraseDocumentSource(db, document, "unapproved", reason);
    }

    private CompanyDocument DeleteRevokedDocumentSource(StoreConnection db, CompanyCommand command)
    {
        var document = Need<CompanyDocument>(db, command.Id);
        Version(document.Version, command.ExpectedVersion);
        if (
            document.State != "revoked"
            || command.Digest != document.Digest
            || command.Decision != "delete-revoked-source"
        )
            throw new WorkspaceException(
                "Revoke access first, then confirm deletion of this exact library copy. Earlier task copies and backups are retained.",
                409
            );
        return EraseDocumentSource(db, document, "revoked-source", "owner");
    }

    private CompanyDocument EraseDocumentSource(
        StoreConnection db,
        CompanyDocument document,
        string scope,
        string reason
    )
    {
        var deleted = document with
        {
            Title = scope == "revoked-source" ? "Deleted document library copy" : "Deleted unapproved document",
            Text = "",
            DepartmentId = null,
            EmployeeId = null,
            SourceName = null,
            Origin = null,
            ReplacesId = null,
            State = "deleted",
            Version = document.Version + 1,
            UpdatedAt = Clock(),
            DeletionScope = scope,
        };
        ClearDocumentIndex(db, document.Id);
        Save(db, deleted, deleted.Id, state: deleted.State, version: deleted.Version);
        // Keep the operation identities and fingerprints. The broom must not make an old save executable again.
        Sql(
            db,
            "UPDATE co_operations SET result=$2 WHERE company_id=$0 AND json_extract(result,'$.id')=$1 AND json_extract(result,'$.text') IS NOT NULL AND json_extract(result,'$.digest') IS NOT NULL",
            CompanyId,
            document.Id,
            CompanyJson.Encode(deleted)
        );
        Audit(
            db,
            "document.deleted",
            document.Id,
            new
            {
                document.Digest,
                deleted.Version,
                scope,
                reason,
            }
        );
        return deleted;
    }

    // Automatic expiry stays limited to unused drafts; reviewed sources need their own owner decision.
    public int MaintainDocumentRetention()
    {
        using var db = Open();
        return Transaction(db, () => MaintainDocumentRetention(db, DocumentRetention(db)));
    }

    private int MaintainDocumentRetention(StoreConnection db, CompanyDocumentRetention policy)
    {
        if (policy.DiscardedDays == 0)
            return 0;
        var cutoff = Clock().AddDays(-policy.DiscardedDays);
        var expired = StoredDocuments(db).Where(d => d.State == "discarded" && d.UpdatedAt <= cutoff).ToArray();
        foreach (var document in expired)
            EraseUnapprovedDocument(db, document, "retention");
        return expired.Length;
    }
}
