# Task cost reconciliation — 2026-10-26

This turn made **verified progress** on the context-first product proposal. It did not complete the three-phase
goal. Completed work now has an owner-only task cost report combining all model attempts with separately
recorded compute, tool, retrieval, storage and operating expenses. Unknown costs stay blank, estimates are
labeled, changes to usage invalidate old reconciliations, and corrections preserve prior recorded amounts.
See [ADR 0097](../adr/0097-task-cost-reconciliation.md).

The owner journey preserves unfinished edits and recovers lost responses. A failed refresh after a successful
save/record originally risked losing the local recovery receipt; that receipt now remains until the saved view
loads. Recorded amounts never change customer charges or allowances. Export explicitly includes private basis
notes. Model and compute content are omitted from the report.

## Verification

- Core, Host, Company Tests and Integrations Tests builds passed without warnings/errors. TypeScript/Vite passed
  with the existing large-chunk advisory. Free disk space exceeded the required 10 GiB before builds.
- **39 distinct backend facts passed**, zero failures/skips: 22 cost/usage/progress facts (nine new cost facts),
  16 company migration/lease checks (two overlap the preceding 22), and three integration migration checks.
- New facts cover unknown versus zero amounts, failed-attempt aggregation, reported versus estimated model
  costs, company-key attribution, native attempts without prices, outstanding compute cleanup, immutable
  corrections, changed-evidence refusal, exact version/digest review, validation, persistence, company isolation
  and owner-only HTTP/report/operation access.
- Earlier migration fixtures now remove later tables when reconstructing an older schema. The billing-delivery
  fixture also removes newer migration records, and the billing migration check no longer assumes migration 26
  is the latest schema. An initial new test used a nonexistent dashboard attempts field; its fixture now reads
  the persisted attempt through the store. These failures were corrected before the passing runs.
- **1 company UI contract check passed**.
- **2 local Chrome desktop/mobile journeys passed**: the new task-cost journey and existing monthly-usage
  regression. Cost checks include real rendered task-result approval/completion, preserved unsaved edits across
  reload, lost-save recovery, unknown-cost refusal, recovery after a confirmed write followed by failed refresh,
  explicit recording, correction history, JSON export and no horizontal mobile overflow.
- Desktop cost, mobile cost and mobile history screenshots were visually inspected. C# and frontend formatting
  checks and `git diff --check` passed. The task-cost runner waited for its owned fixture processes to exit before
  removing disposable files; compact receipts and screenshots remain.

Evidence: `test-results/task-cost-backend.log`, `task-cost-company-migrations.log`,
`task-cost-integration-migrations.log`, `task-cost-ui.log`, `task-cost-e2e.log`,
`task-cost-usage-regression.log` and `task-cost-browser.json`.

## Limits

All usage and expense inputs in these checks are fictional. This is owner-entered reconciliation support,
not automatic invoice collection or independent verification of the allocation basis. Actual provider and
infrastructure invoices, shared-cost allocation, real useful-task costs and customer economics remain pending.
No live model call, customer charge, paid provider action or deployment was performed. Stripe launch terms,
real hosted payment acceptance, production runtime qualification and customer field validation remain separate.
