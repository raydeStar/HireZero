using System.Globalization;

namespace HireZero.Core;

public sealed record CompanyRetirementCollection(string Collection, long Records);

public sealed record CompanyRetirementIssue(string Area, long Records, string Action);

public sealed record CompanyRetirementReview(
    string CompanyId,
    DateTimeOffset CheckedAt,
    CompanyRetirementCollection[] Collections,
    CompanyRetirementIssue[] Issues,
    string[] UnverifiedAreas,
    bool ErasureAvailable = false,
    string Scope = "company-database-review-v1"
);

public sealed partial class CompanyStore
{
    // A review is an inventory, never permission to erase. An empty ledger cannot certify an empty world.
    public CompanyRetirementReview RetirementReview(string subject)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var member = Find<CompanyMember>(db, Text(subject, 400));
                if (member is not { State: "active", Role: CompanyRoles.Owner })
                    throw new WorkspaceException("Only a current company owner may review data retirement.", 403);

                long Count(string table, string condition = "1=1") =>
                    long.Parse(
                        Scalar(db, $"SELECT count(*) FROM {table} WHERE company_id=$0 AND ({condition})", CompanyId)!,
                        CultureInfo.InvariantCulture
                    );
                var collections = Tables
                    .Values.Concat(
                        new[]
                        {
                            "co_document_chunks",
                            "co_document_terms",
                            "co_memory_terms",
                            "co_notice_reads",
                            "co_workspace_leases",
                            "co_billing_objects",
                            "co_events",
                            "co_operations",
                            "co_audit",
                            "co_tool_calls",
                        }
                    )
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .Select(table => new CompanyRetirementCollection(table, Count(table)))
                    .ToList();
                collections.Add(new("co_company", 1));
                var issues = new List<CompanyRetirementIssue>();
                void Check(string area, string table, string condition, string action)
                {
                    var count = Count(table, condition);
                    if (count > 0)
                        issues.Add(new(area, count, action));
                }
                Check(
                    "unfinished-work",
                    "co_tasks",
                    "state NOT IN ('completed','failed','canceled','superseded','awaiting_review')",
                    "Review unfinished assignments and stop work that should no longer run."
                );
                Check(
                    "model-attempts",
                    "co_attempts",
                    "state NOT IN ('committed','withheld','not_sent')",
                    "Resolve prepared, running or uncertain model attempts without replaying uncertain requests."
                );
                Check(
                    "profile-generation",
                    "co_business_drafts",
                    "state NOT IN ('completed','withheld','not_sent','expired','invalid','reconciled')",
                    "Resolve business-profile generation and its usage receipts."
                );
                Check(
                    "memory-analysis",
                    "co_memory_analyses",
                    "state NOT IN ('completed','withheld','not_sent','expired','invalid','reconciled')",
                    "Resolve memory analysis and its usage receipts."
                );
                Check(
                    "budget-holds",
                    "co_reservations",
                    "state <> 'settled'",
                    "Reconcile retained model reservations; an uncertain amount is not zero."
                );
                Check(
                    "usage-holds",
                    "co_usage_entries",
                    "state <> 'settled'",
                    "Reconcile current and carried usage holds before retiring accounting evidence."
                );
                Check(
                    "checkout-claims",
                    "co_resource_claims",
                    "1=1",
                    "Release recorded checkout claims only after the original worker has stopped."
                );
                Check(
                    "checkout-leases",
                    "co_workspace_leases",
                    "1=1",
                    "Confirm release of the physical checkout lease."
                );
                // Decode the nested receipt in Core; the portable SQL dialect only translates top-level JSON fields.
                var codeCleanup = Rows(
                        db,
                        "SELECT payload FROM co_execution_grants WHERE company_id=$0 AND json_extract(payload,'$.dispatchedAt') IS NOT NULL",
                        r => CompanyJson.Decode<CompanyExecutionGrant>(r.GetString(0)),
                        CompanyId
                    )
                    .LongCount(g => g.Release is not { ProcessStopped: true, TemporaryFilesRemoved: true });
                if (codeCleanup > 0)
                    issues.Add(
                        new(
                            "code-cleanup",
                            codeCleanup,
                            "Confirm process exit and temporary-file cleanup; task completion alone does not establish either."
                        )
                    );
                Check(
                    "script-resources",
                    "co_sandbox_jobs",
                    "json_extract(payload,'$.startedAt') IS NOT NULL AND json_extract(payload,'$.settledAt') IS NULL",
                    "Reconcile the original script runtime and confirm resource release."
                );
                Check(
                    "browser-resources",
                    "co_browser_jobs",
                    "json_extract(payload,'$.startedAt') IS NOT NULL AND json_extract(payload,'$.settledAt') IS NULL",
                    "Reconcile the original browser runtime without revisiting websites."
                );
                Check(
                    "publications",
                    "co_publications",
                    "state NOT IN ('published','failed','revoked','expired','refused')",
                    "Close unused publication approvals and reconcile uncertain external actions."
                );
                Check(
                    "website-discovery",
                    "co_business_lookups",
                    "state NOT IN ('completed','partial','withheld')",
                    "Wait for or reconcile unfinished public discovery."
                );
                Check(
                    "source-refresh",
                    "co_business_refreshes",
                    "json_extract(payload,'$.completedAt') IS NULL",
                    "Finish or reconcile recorded source refreshes."
                );
                Check(
                    "recurring-work",
                    "co_schedules",
                    "state IN ('approved','active')",
                    "Pause active schedules before preparing company retirement."
                );
                Check(
                    "email-delivery",
                    "co_notice_deliveries",
                    "state NOT IN ('accepted','closed','skipped')",
                    "Close unsent notices and reconcile uncertain delivery without resending."
                );
                Check(
                    "checkout-payments",
                    "co_billing_checkouts",
                    "state NOT IN ('complete','failed','expired','prepared')",
                    "Verify checkout outcomes with the payment provider; do not create another checkout to recover one."
                );
                Check(
                    "billing-portals",
                    "co_billing_portals",
                    "state IN ('queued','dispatching','uncertain')",
                    "Resolve pending or uncertain customer-portal requests."
                );
                Check(
                    "subscription",
                    "co_billing_state",
                    "json_extract(payload,'$.subscriptionId') IS NOT NULL",
                    "Review subscription disposition with the provider. A local inactive status does not prove cancellation."
                );
                Check(
                    "connections",
                    "co_connections",
                    "state = 'open'",
                    "Resolve pending connection requests and separately disconnect external accounts."
                );
                var relatedEvents = long.Parse(
                    Scalar(
                        db,
                        "SELECT count(*) FROM co_billing_events WHERE state NOT IN ('complete','ignored') AND json_extract(payload,'$.companyId')=$0",
                        CompanyId
                    )!,
                    CultureInfo.InvariantCulture
                );
                if (relatedEvents > 0)
                    issues.Add(
                        new(
                            "billing-events",
                            relatedEvents,
                            "Reconcile queued provider events associated with this company."
                        )
                    );
                string[] unverified =
                [
                    "Connected services, saved credentials and pending connection approvals need a separate check.",
                    "Running programs, temporary files and working folders must be checked on the computers that ran the work.",
                    "Subscriptions, delivered messages and published work must be checked with their providers. Payment events not yet assigned to this company are outside this review.",
                    "Backups, recovery files, exports, data saved in browsers and remote copies remain unchanged.",
                    "A pause on new work does not stop work already admitted or authorize deletion. Permanent deletion is not available here.",
                ];
                var ordered = collections.OrderBy(c => c.Collection, StringComparer.Ordinal).ToArray();
                return new CompanyRetirementReview(CompanyId, Clock(), ordered, issues.ToArray(), unverified);
            }
        );
    }
}
