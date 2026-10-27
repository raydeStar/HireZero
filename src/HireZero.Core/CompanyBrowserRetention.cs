namespace HireZero.Core;

public sealed record CompanyBrowserRetention(string Id, int CaptureDays = 0, int Version = 0);

public sealed record CompanyBrowserRetentionInput(int CaptureDays);

public sealed partial class CompanyStore
{
    private const string BrowserRetentionSchema =
        "CREATE TABLE co_browser_retention(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    public CompanyBrowserRetention BrowserRetention()
    {
        using var db = Open();
        return BrowserRetention(db);
    }

    private CompanyBrowserRetention BrowserRetention(StoreConnection db) =>
        Find<CompanyBrowserRetention>(db, "captures") ?? new("captures");

    private CompanyBrowserRetention ConfigureBrowserRetention(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyBrowserRetentionInput>(command.Body ?? "null");
        if (input is null || input.CaptureDays is not (0 or 7 or 30 or 90))
            throw new WorkspaceException(
                "Keep browser captures, or delete them after 7, 30 or 90 days from confirmed cleanup.",
                422
            );
        if (command.Decision != "apply-capture-retention")
            throw new WorkspaceException(
                "Confirm capture deletion, including already expired captures. Saved task results remain.",
                422
            );
        var current = BrowserRetention(db);
        Version(current.Version, command.ExpectedVersion);
        var policy = current with { CaptureDays = input.CaptureDays, Version = current.Version + 1 };
        Save(db, policy, policy.Id, version: policy.Version);
        Audit(db, "browser.retention", policy.Id, new { policy.CaptureDays, policy.Version });
        MaintainBrowserRetention(db, policy);
        return policy;
    }

    private CompanyBrowserJob DeleteBrowserCaptures(StoreConnection db, CompanyCommand command)
    {
        var job = Need<CompanyBrowserJob>(db, command.Id);
        Version(job.Version, command.ExpectedVersion);
        if (job.ResultDigest != command.Digest || command.Decision != "delete-captures")
            throw new WorkspaceException(
                "Confirm deletion of these exact saved captures. Selected task results remain.",
                409
            );
        return EraseBrowserCaptures(db, job, "owner");
    }

    private CompanyBrowserJob EraseBrowserCaptures(StoreConnection db, CompanyBrowserJob job, string reason)
    {
        if (job.CaptureDeletion is not null)
            return job;
        if (job.SettledAt is null || job.Release?.Confirmed != true || job.Execution?.Result is null)
            throw new WorkspaceException(
                "Saved captures can be deleted only after the original browser resources have been released.",
                409
            );
        // Preserve only proofs for already saved, valid excerpts. The broom is not a new source of authority.
        var retained = Rows(
                db,
                "SELECT payload FROM co_artifacts WHERE company_id=$0 AND parent_id=$1",
                r => CompanyJson.Decode<CompanyArtifact>(r.GetString(0)),
                CompanyId,
                job.TaskId
            )
            .Where(a => a.BrowserOrigin?.JobId == job.Id && BrowserArtifactCurrent(db, a))
            .Select(a => new CompanyBrowserRetainedArtifact(a.Id, a.Digest, CompanyJson.Digest(a.BrowserOrigin)))
            .ToArray();
        var deleted = job with
        {
            Execution = job.Execution with { Result = null, Reason = null },
            CaptureDeletion = new(Clock(), reason, retained),
            Version = job.Version + 1,
            UpdatedAt = Clock(),
        };
        SaveBrowser(db, deleted);
        // Keep operation IDs/fingerprints: a lost old response must never restore content or repeat a visit.
        Sql(
            db,
            "UPDATE co_operations SET result=$2 WHERE company_id=$0 AND json_extract(result,'$.id')=$1 AND json_extract(result,'$.request') IS NOT NULL AND json_extract(result,'$.runtimeIdentity') IS NOT NULL",
            CompanyId,
            job.Id,
            CompanyJson.Encode(deleted)
        );
        Audit(
            db,
            "browser.captures-deleted",
            job.Id,
            new
            {
                job.ResultDigest,
                deleted.Version,
                reason,
                retainedArtifacts = retained.Length,
            }
        );
        Event(
            db,
            "browser.captures-deleted",
            job.Id,
            job.EmployeeId,
            "Saved page captures were deleted. Selected task results, itinerary, usage and audit history remain."
        );
        return deleted;
    }

    public int MaintainBrowserRetention()
    {
        using var db = Open();
        return Transaction(db, () => MaintainBrowserRetention(db, BrowserRetention(db)));
    }

    private int MaintainBrowserRetention(StoreConnection db, CompanyBrowserRetention policy)
    {
        if (policy.CaptureDays == 0)
            return 0;
        var cutoff = Clock().AddDays(-policy.CaptureDays);
        // Read only eligibility metadata, then erase a bounded batch. No parade of screenshots through memory.
        var expired = Rows(
                db,
                "SELECT id,json_extract(payload,'$.settledAt') FROM co_browser_jobs WHERE company_id=$0 AND state IN ('completed','failed','timed-out','canceled','cleanup-incomplete') AND json_extract(payload,'$.captureDeletion') IS NULL AND json_extract(payload,'$.settledAt') IS NOT NULL",
                r =>
                    (
                        Id: r.GetString(0),
                        SettledAt: DateTimeOffset.Parse(
                            r.GetString(1),
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    ),
                CompanyId
            )
            .Where(j => j.SettledAt <= cutoff)
            .OrderBy(j => j.SettledAt)
            .ThenBy(j => j.Id, StringComparer.Ordinal)
            .Take(20)
            .ToArray();
        foreach (var job in expired)
            EraseBrowserCaptures(db, Need<CompanyBrowserJob>(db, job.Id), "retention");
        return expired.Length;
    }
}
