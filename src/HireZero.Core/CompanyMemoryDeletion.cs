namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private CompanyMemoryItem DeleteUnapprovedMemory(StoreConnection db, CompanyCommand command)
    {
        var memory = Need<CompanyMemoryItem>(db, command.Id);
        Version(memory.Version, command.ExpectedVersion);
        if (
            memory.State is not ("draft" or "discarded")
            || memory.ApprovedAt is not null
            || memory.Digest != command.Digest
            || command.Decision != "delete-unapproved"
        )
            throw new WorkspaceException(
                "Confirm deletion of this exact never-approved draft. Previously approved memory may have saved task copies.",
                409
            );
        return EraseMemorySource(db, memory, "unapproved");
    }

    private CompanyMemoryItem DeleteRetiredMemorySource(StoreConnection db, CompanyCommand command)
    {
        var memory = Need<CompanyMemoryItem>(db, command.Id);
        Version(memory.Version, command.ExpectedVersion);
        if (
            memory.State is not ("revoked" or "superseded")
            || memory.ApprovedAt is null
            || memory.Digest != command.Digest
            || command.Decision != "delete-retired-source"
        )
            throw new WorkspaceException(
                "Revoke or replace this memory first, then confirm deletion of its exact saved record. Earlier task copies and backups remain.",
                409
            );
        return EraseMemorySource(db, memory, "retired-source");
    }

    private CompanyMemoryItem EraseMemorySource(StoreConnection db, CompanyMemoryItem memory, string scope)
    {
        var deleted = memory with
        {
            Definition = new(
                "decision",
                scope == "unapproved" ? "Deleted unapproved memory" : "Deleted saved memory",
                "",
                ""
            ),
            Origin = null,
            MeetingOrigin = null,
            Replacement = null,
            State = "deleted",
            Version = memory.Version + 1,
            UpdatedAt = Clock(),
            Reason =
                scope == "unapproved"
                    ? "Saved draft contents were deleted. Existing audit titles, source records and backups remain."
                    : "Saved memory contents were deleted. Earlier task copies, reports, other drafts, audit metadata and backups remain.",
            DeletionScope = scope,
        };
        ClearMemoryIndex(db, memory.Id);
        PutMemory(db, deleted);
        // Keep old request identities locked: a cleared shelf is no invitation to replay yesterday's save.
        Sql(
            db,
            "UPDATE co_operations SET result=$2 WHERE company_id=$0 AND json_extract(result,'$.id')=$1 AND json_extract(result,'$.definition') IS NOT NULL AND json_extract(result,'$.digest') IS NOT NULL",
            CompanyId,
            memory.Id,
            CompanyJson.Encode(deleted)
        );
        Audit(
            db,
            "memory.deleted",
            memory.Id,
            new
            {
                memory.Digest,
                deleted.Version,
                scope,
            }
        );
        return deleted;
    }
}
