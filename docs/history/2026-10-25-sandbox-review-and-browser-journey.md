# Script review and browser journey — 2026-10-25

Continues the full context-first goal, primarily P3.2 and X8/X9. Decisions are in
[ADR 0067](../adr/0067-script-review-ui-and-recoverable-queue.md).

## Implemented and corrected

- Added advanced task script tools with approved document selection, a row-count example, exact immutable
  review, one-run consent, allowance checks, saved outputs and distinct cleanup/recovery controls.
- Added script allocations, pause controls and recent-work links to Usage without changing AI token limits.
- Added the bounded host worker, all-company startup recovery, owner-only proposal/recovery routes and
  tenant-safe capability reporting. A dedicated bounded JSON route supports the full script size.
- Queued work retains its review window; the short execution deadline begins at durable claim.
- Browser testing exposed a genuine Stop race: background progress advanced the version between display and
  click. Revocation now accepts an older positive revision of that same immutable job. Approval stays exact.
- The editor waits for document loading, bounds selected bytes, and closes/moves focus to the prepared review.
  Recovery controls continue working while new execution is paused. Previously saved output survives cleanup
  failure and remains readable after page reload.

## Evidence

Free disk space before builds was approximately 317 GiB. No images or VMs were built or copied.

- Host/company test build and integration test build: passed, zero warnings/errors.
- Final integration run: `CompanySandboxTests`, `SandboxTests`, `SandboxRuntimeTests` — **32 total, 31 passed,
  one explicitly skipped real-runtime check, zero failures** (2.683 seconds). Includes the new stale-revocation
  and queue-deadline facts, plus the prior authority, isolation, metering, recovery and adapter checks.
- `CompanySandboxHostingTests`: **2 passed**, including a 20-KiB HTTP proposal, duplicate approval suppression,
  cross-company recovery refusal and startup recovery of an unopened company (1.304 seconds).
- `node tests/sandbox-browser-smoke.mjs`: passed against the fictional executor and real UI/HTTP/store/worker.
  Verified policy consent, exact script review, selected file bytes, lost approval response with one execution,
  retained output after cleanup failure, recovery while paused, cancellation and withheld late output.
  The fixture observed two executions, ten allocated script seconds and no remaining resource hold.
- `node tests/usage-browser-smoke.mjs`: passed the existing AI allowance/receipt/mobile regression.
- `node tests/company-ui.test.mjs`: passed company-scoped receipt and response-loss checks.
- Frontend TypeScript/Vite build, scoped Csharpier/Prettier formatting and `git diff --check` passed. Git emitted
  only the existing line-ending normalization warnings. Final web assets: `index-CXxMi9PT.js`, `index-b2D2_azI.css`.

Both 1440-pixel desktop and 390-pixel mobile screenshots were inspected. The actual saved result is readable
on mobile with no horizontal overflow; headings, limits, output and release state remain distinct.

Ignored proof artifacts include `test-results/sandbox-browser.json`, `sandbox-mobile.png`, `sandbox-desktop.png`,
`sandbox-browser-run.log`, `sandbox-hosting-tests.log`, `sandbox-usage-regression.log`, `sandbox-ui-build.log`,
`sandbox-ui-web-build.log`, `sandbox-lifecycle-tests.log` and `sandbox-ui-receipt-tests.log`.

## Boundaries and next work

The real Docker executor remains unregistered in the production host. Test injection cannot be activated by
an ordinary API request. No submitted JavaScript, real container, live model, payment, push or deployment ran.
The browser fixture returns fictional receipts, so this is product-flow evidence and not containment proof.

Next: operator configuration and qualification-based activation of the pinned runtime; then durable artifact
promotion and remaining browser/premium/billing capabilities. Real customer usefulness, retention and commercial
validation remain open across the full three-phase acceptance ledger.
