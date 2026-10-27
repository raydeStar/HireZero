# Completed-task cost coverage — 2026-10-26

Progress now includes the owner-reviewed operating costs previously available only in individual task reports.
The owner sees coverage, a reviewed subtotal, and an average only when every completed work task has a current
total. Missing reviews, changed usage and incomplete evidence remain explicit. Model estimates and company-key
funding stay labeled. The breakdown and export share the same cohort and retain exact decimal amounts.

The aggregation reuses task reconciliation rules from a single company database snapshot. Ledger rows are loaded
and grouped once instead of issuing a full ledger read per task. A newer draft does not replace recorded costs;
a recorded correction replaces the old amount without double counting. Cost evidence is owner-only, with current
membership checked inside the snapshot. Other active members retain ordinary Progress. The metrics export adds
aggregate figures and a next-review record ID/reason, without expense basis text, member identities or work content.

The new link to work needing cost review uncovered a responsive navigation issue. Progress opened work beside
chat, so narrowing the screen remounted the task and closed its cost panel. Progress now opens the full work
view, preserving the active panel through that viewport change. Other navigation entry points keep their mode.

Decision: [ADR 0101](../adr/0101-completed-task-cost-progress.md).

## Verified locally

- **16 backend tests passed**: 12 task-cost tests, including three new aggregation facts and extended HTTP
  permission checks, plus four existing progress tests. Coverage includes empty/missing/draft costs, partial
  coverage, corrections, restart, stale usage, unpriced attempts, company-key estimates, current owner access,
  viewer/approver exclusion, company isolation, content-free export and unchanged audit counts on reads.
- **Two Chrome journeys passed**: the extended task-cost/Progress desktop/mobile journey and the existing
  business-drafting/onboarding/Progress journey. The first verifies the missing-cost link, full work mode,
  corrected task totals, preserved review UI on viewport change, responsive layout and exact metrics export.
  The original cost-receipt recovery and correction-history checks also pass.
- Desktop/mobile cost summary screenshots were visually inspected. The mobile card keeps the category breakdown
  collapsed until requested and has no horizontal overflow.
- Core and company-test/host builds passed with zero warnings/errors. TypeScript/Vite passed with the existing
  large-chunk advisory. Formatting, syntax and whitespace checks passed.
- A first host build overlapped a frontend asset replacement and failed on a removed hashed asset. The completed
  frontend build was followed by a sequential host/test build; no runtime or dependency workaround was introduced.
  The first extended browser run exposed the navigation remount above; the final run passes after its UX fix.

Compact ignored evidence: `test-results/progress-cost-tests.log`, `progress-tests.log`, `progress-cost-build.log`,
`progress-cost-web-build.log`, `progress-cost-browser.log`, `progress-cost-onboarding-browser.log`,
`task-cost-browser.json`, and `progress-task-cost-*.png`. Fixture hosts/browsers exited and temporary data was removed.

## Remaining acceptance

These are fictional lifecycle and UI observations. No model, payment, outreach, production deployment or runtime
activation occurred. The subtotal excludes brief preparation, chat, unfinished tasks and unallocated company
expenses; it is not a profit calculation. Real cost inputs and allocation evidence, commercial terms, hosted
provider/payment acceptance, actual runtime qualification and customer usefulness/retention remain required.
The three-phase goal is still active. Runtime-host approval remains pending.
