namespace HireZero.Core;

public sealed partial class CompanyStore
{
    // Company records are private; a physical checkout cannot accept two writers merely because their companies differ.
    private const string WorkspaceLeaseSchema = """
        CREATE TABLE co_workspace_leases(resource TEXT PRIMARY KEY,company_id TEXT NOT NULL,attempt_id TEXT NOT NULL,FOREIGN KEY(company_id,attempt_id) REFERENCES co_attempts(company_id,id));
        CREATE INDEX co_workspace_lease_attempt ON co_workspace_leases(company_id,attempt_id);
        CREATE INDEX IF NOT EXISTS co_resource_claim_lookup ON co_resource_claims(id);
        """;

    private void LeaseWorkspace(StoreConnection db, string resource, string attempt)
    {
        // The unique resource key arbitrates concurrent companies on both supported databases. Losing this insert
        // cannot overwrite a lease; the surrounding company transaction rolls back before any execution starts.
        Sql(
            db,
            "INSERT INTO co_workspace_leases(resource,company_id,attempt_id) VALUES($0,$1,$2) ON CONFLICT(resource) DO NOTHING",
            resource,
            CompanyId,
            attempt
        );
        if (
            Scalar(
                db,
                "SELECT count(*) FROM co_workspace_leases WHERE resource=$0 AND company_id=$1 AND attempt_id=$2",
                resource,
                CompanyId,
                attempt
            ) != "1"
            // Pre-migration claims remain authoritative. Even conflicting legacy claims are retained for review.
            || Scalar(
                db,
                "SELECT count(*) FROM co_resource_claims WHERE id=$0 AND (company_id<>$1 OR json_extract(payload,'$.attemptId')<>$2)",
                resource,
                CompanyId,
                attempt
            ) != "0"
        )
            throw new WorkspaceException(
                "This checkout is reserved by another attempt. Wait for its confirmed release or ask the host operator to reconcile it."
            );
    }

    private void ReleaseWorkspaceClaims(StoreConnection db, string attempt)
    {
        Sql(db, "DELETE FROM co_workspace_leases WHERE company_id=$0 AND attempt_id=$1", CompanyId, attempt);
        foreach (var claim in All<CompanyResourceClaim>(db).Where(r => r.AttemptId == attempt))
            Sql(db, "DELETE FROM co_resource_claims WHERE company_id=$0 AND id=$1", CompanyId, claim.Id);
    }
}
