# Company pause for new work — 2026-10-26

Implemented [ADR 0106](../adr/0106-company-work-admission-pause.md). Companies now offers one durable owner
control for new execution admissions, with a workspace-wide banner and explicit consent before resume.
The company usage policy carries the flag; old records default to unpaused and ordinary usage changes preserve
it. There is no migration, separate budget override or runtime setting change.

The gate covers new model reservations, profile generation, memory analysis, coding admission, scripts,
browsers, employee actions, business lookups, source refreshes, publication claims and schedule claims.
Already admitted work may start/finish, while accounting, cancellation, cleanup and recovery remain available.
Neither pause nor resume changes existing approvals, their expiry, compute limits or subscriptions.

The UI prevents duplicate mutations, saves an operation identity before dispatch, reconciles uncertain replies
without toggling again, disables changes after an unavailable result and offers a saved-status refresh.
Confirmed control updates invalidate older workspace reads. The mobile resume checkbox was moved alongside
its explanation after screenshot inspection; pause and retirement use distinct selectors.

## Verification

- **108 focused backend facts passed**, no skips, across work controls, usage, schedules, browser hosting,
  memory analysis, retirement, profile drafting, research lookup, publication, employee actions and source
  freshness. Ten new facts cover persisted/tenant-scoped pause, old-record defaults, owner permissions,
  expected-version conflicts, idempotent receipts, preservation of independent limits, model/compute holds,
  admission-versus-pause concurrency, already-admitted completion/recovery, missed slots and paused publication
  without consuming its approval. These are fictional local-provider checks, not production acceptance.
- The extended settings-recovery Chrome journey passed at desktop and mobile widths. It verifies pause,
  visible status away from Companies, saved state on return, duplicate clicks, resume consent, refused saves,
  lost resume replies, receipt-only recovery and viewport fit. Existing settings and retirement-review checks
  also pass. The final mobile pause screenshot was inspected.
- Shared company-UI recovery contract: passed. TypeScript/Vite and Host/test builds: passed; .NET reported
  zero warnings/errors. Targeted formatting, script syntax and diff whitespace checks: passed.
- An initial new assertion incorrectly expected a specific audit kind; this application stores the event type
  inside a generic event body. Corrected that assertion. The first browser extension exposed a reused CSS
  selector that targeted the pause panel instead of retirement; separated the selectors and reran successfully.
- Owned test hosts/Chrome processes exited before their disposable roots were removed. No real model,
  payment, publish action or customer data was used. No commits, pushes or deployment occurred.

Compact ignored evidence: `test-results/work-control-tests-final.log`, `work-control-build-final.log`,
`work-control-web-build.log`, `work-control-browser.log`, `work-control-ui-contracts.log`,
and `work-control-mobile.png`. Earlier failing logs remain separate.

## Limits and next work

This is an admission pause, not a kill switch, proof of quiescence, comprehensive write freeze or full-company
retirement. Scoped integration/credential cleanup, bootstrap suppression, external settlement and coordinated
erasure remain outstanding. Actual PostgreSQL concurrency and customer/provider acceptance are not claimed.
The separately authorized [Docker inspection](2026-10-26-local-browser-runtime-inspection.md) succeeded but
found no registered runsc runtime, so no image build or runtime qualification was attempted.
