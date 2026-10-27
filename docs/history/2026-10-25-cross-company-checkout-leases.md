# Cross-company checkout leases — 2026-10-25

Scope: shared physical checkout reservations in the full product implementation; [ADR 0064](../adr/0064-cross-company-checkout-leases.md).

The company-scoped claim now also takes a database-wide unique lease for the canonical checkout digest.
Two companies cannot concurrently hold the same checkout, including nested path aliases. Only the owning
attempt's confirmed settlement or known unsent outcome releases the lease. Unknown outcomes and restart
recovery retain it. Conflict responses reveal no other company's identifiers.

Migration 20 adds the lease table and lookup indexes without rewriting existing company records. Legacy
claims continue to block conflicting work; an acquisition adopts its own legacy claim only when no other
claim conflicts. Conflicting legacy records are preserved for reconciliation, with no automatic winner.

## Verification

- Company test build passed, zero warnings/errors.
- Four new SQLite facts passed: cross-company/path-alias exclusion, concurrent acquisition with one winner,
  unknown-outcome persistence after reopen, legacy adoption/release, and refusal of conflicting legacy claims.
- All nine existing `CompanySuite` regression groups passed across the initial run and targeted repair run.
  These include native fake-runtime behavior, measured queues, HTTP isolation, security, reviewed coding,
  company concurrency/budgets, handoffs and conversations.
- Ten focused migration/portable-storage checks passed. The initial integration regression exposed an old
  upgrade fixture that did not remove the newly added table when constructing its old database. All equivalent
  fixtures were corrected; the four affected paths then passed. Production migration code was unchanged.
- In total, 23 distinct cases/groups cover this change. Upgrade checks preserve prior migration checksums
  and saved records; the core lease tests inspect retained claims and rollback directly.
- Scoped Csharpier formatting and `git diff --check` passed.

Logs are retained under ignored `test-results/workspace-leases-*`. PostgreSQL SQL translation was checked,
but no disposable PostgreSQL test server was configured, so live PostgreSQL concurrency is unverified.
No live model, external publication, database service changes or production deployment was used.

The lease coordinates one application database. It does not authorize filesystem access, coordinate separate
installations or provide isolated compute. Hosted workers, provider-aware resource identities, operator
reconciliation and the remaining three-phase requirements are still pending.
