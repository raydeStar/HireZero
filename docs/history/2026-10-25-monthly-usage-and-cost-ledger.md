# Monthly usage and cost receipts — 2026-10-25

Scope: another increment of the full three-phase product goal. Commercial subscriptions remain incomplete.
Decision: [ADR 0061](../adr/0061-monthly-usage-admission-and-cost-receipts.md).

## Delivered

- Monthly company limits for admitted AI jobs and planning tokens, with an explicit pause. Admission is atomic
  with ordinary reservations and covers employee, recurring, developer and pre-hire profile work.
- Exact-version owner settings and limits that cannot erase prior usage or cancel already authorized work.
  Preparing a request does not consume allowance; admitted failed/canceled work retains its job count.
- Outstanding reservations and observed overage survive month rollover and restart. Late usage settles in the
  original month. Earlier undated reservations remain explicit and continue to constrain admission.
- Price snapshots for newly admitted jobs. Settled provider-reported costs, additional estimates, unpriced jobs
  and provisional costs are distinguished. Home/cockpit/cumulative totals use new ledger receipts without
  double-counting attempts or silently repricing them.
- A Usage page with UTC reset dates, remaining allowance, monthly history, receipt details, direct work links,
  reviewed limit changes and company-scoped lost-response recovery. Packet review shows current allowance.
- Clear cumulative-budget and cost labels: estimated model usage is no longer presented as unqualified
  "Spent to date". The monthly page states which costs are outside its scope.

## Verification

317.2 GiB was available before build work. Backend and frontend production builds passed. Runtime pins and
dependencies were unchanged. Scoped formatting and `git diff --check` passed before the final cost-label review.
The final backend/frontend builds and the additional scoped formatting checks also passed.

The first focused usage run passed **nine facts**, zero failures/skips, 1.648 seconds. The broader regression
passed **90 xUnit cases**, zero failures/skips, 74.230 seconds, covering usage, memory, documents, schedules,
notifications, freshness, company context, business drafting, context drafting and all nine CompanySuite groups.
Those groups include security, native/API fixture execution, ordinary queues, measured queues, conversations,
handoffs, HTTP behavior, budgets and integration paths.

After integrating ledger receipts into Home/cockpit/cumulative totals, all **13 usage/pricing facts passed**,
zero failures/skips, 1.662 seconds. Four pricing facts are additional to the 90-case regression, giving
**94 distinct backend cases**. The added pricing assertion proves no repricing, no double counting, provider
cost precedence and exclusion of provisional charges. The usage browser journey passed again against the
final backend and frontend.

The usage facts exercise dispatch-only admission, exact once settlement, concurrent final-slot contention,
cross-month holds and late receipts, unknown/overage persistence, price snapshots, provider cost precedence,
unpriced jobs, pause and stale settings, owner authorization, legacy undated receipts, pre-hire settlement,
tenant isolation and migration 19 preserving earlier data/checksums.

Browser journeys passed with fictional providers:

- `usage-browser-smoke.mjs`: explicit limits consent, lost save reply reconciled once, reload, preparation
  consuming no job, monthly refusal before dispatch, pause surviving rollover, explicit unpause, a new month's
  allowance, historical receipt preservation, provider cost attribution, direct work navigation and no mobile
  horizontal overflow.
- `schedules-browser-smoke.mjs`: existing finite recurring employee work and source checks, pause/resume,
  settlement and exact inbox navigation continue to pass under monthly admission.

The 390 × 844 Usage screenshot was visually inspected. Existing `company-ui.test.mjs` recovery/scope checks
also passed. Browser profiles and data were disposable; there were no live model requests or external payments.

Compact ignored evidence: `test-results/usage-build.log`, `usage-web-build.log`, `usage-tests.log`,
`usage-regression.log`, `usage-browser-run.log`, `usage-browser.json`, `usage-mobile.png`,
`usage-schedules-browser.log`, `usage-pricing-tests.log` and `usage-ui.log`.

## Remaining

Payment-provider and launch plan/price choices were requested from the owner and remain pending. These monthly
limits are safety settings, not paid entitlements. Checkout, subscriptions, invoices, commercial entitlement
enforcement, tenant API keys and compute/tool/storage metering remain in the full acceptance ledger. Legacy
receipts with missing dates/prices are labeled unknown rather than assigned invented historical values.

No deployment, publication, commit, push, live inference or real payment occurred.
