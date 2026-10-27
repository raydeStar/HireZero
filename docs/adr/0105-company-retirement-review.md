# ADR 0105: Owner review before company retirement

Status: accepted, 2026-10-26. Supports X6; does not implement company erasure.

## Context

Deleting the company row or its directory is insufficient. Uncertain model usage, checkout leases, coding
cleanup, compute allocations, subscriptions and external deliveries have independent lifecycles. Removing
their evidence can abandon resources or make an uncertain action appear safe to repeat. The default company
also bootstraps at startup, while legacy local integrations share storage with the host.

## Decision

Provide an owner-only, read-only review at `GET /api/company/retirement-review` and under **Companies**.
Recheck current owner membership inside the company transaction and send `Cache-Control: no-store`.
Return company-scoped table counts and outstanding-action counts, without record contents, resource paths,
provider identifiers, receipt text or credentials. Include all company tables and special indexes/ledgers;
the shared merchant inbox is counted separately only for unresolved events explicitly assigned to the company.
Unassigned events require separate operator reconciliation, even if their provider object could later resolve
to this company. Do not reveal another company's event counts.

Distinguish settled history from unresolved actions using each workflow's actual states. Count coding cleanup
from its process-exit and temporary-file receipts, and script/browser allocations from settlement timestamps.
A task marked completed cannot clear these concerns. A locally canceled subscription still requires provider
review. A read never calls a provider, releases a hold, pauses a schedule or deletes a record.

The result always reports `erasureAvailable: false`. Even an empty issues list is not a deletion authorization.
There is no approval digest: counts cannot represent every content change or establish an atomic snapshot of
remote resources. Company records are read under the existing company transaction; the global merchant inbox
has a separate writer scope and can change during the review on PostgreSQL.

The UI loads only on explicit request, prevents duplicate reads, clears an older result before refreshing,
offers retry after failure and ignores responses after unmount. It states the check time and that ongoing work
can change the result. Raw table names remain in the API inventory, not the normal customer-facing display.

## Outstanding retirement work

Coordinated closure must stop new admissions across all execution paths while allowing original work and
uncertain payments to reconcile; prevent default-company resurrection; remove only verified company-owned
credentials and integration records; and account for runtime hosts, provider accounts, published/delivered
content, local browser storage, exports, backups and recovery files. This review does not certify those areas.

Keep full-company retirement open in the delivery ledger. Verification results are recorded separately in
the dated history receipt; fictional tests cannot establish production erasure or provider cancellation.

Update: [ADR 0106](0106-company-work-admission-pause.md) adds a separate pause on new execution admissions.
Already admitted work and maintenance may continue, so full quiescence and coordinated erasure remain open.
