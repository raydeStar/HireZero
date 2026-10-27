# Tracked first deliverable and result reading — 2026-10-25

Scope: P1.8 and shared work-review usability in the [full delivery ledger](../CONTEXT_FIRST_DELIVERY.md).
Decision: [ADR 0071](../adr/0071-tracked-first-deliverable.md).

## Delivered locally

- The first Marketing plan becomes an ordinary tracked work task through atomic `assignment.prepare`, bound to
  the current approved company knowledge, employee, private thread, role skill and explicit allowance. Preparation
  does not dispatch or reserve usage. Reconciliation after a lost response does not create a second task.
- A role-owned starter-plan skill requests approved observations, small experiments with success measures,
  sample copy and unknowns. The platform's named deliverable profile allows bounded longer output instead of
  the earlier 80-word conversation reply. Other work and conversation limits remain unchanged.
- The resulting artifact needs owner approval, followed by explicit task completion. Revocation before dispatch
  rejects stale authority; revocation during execution withholds late output while settling reported usage.
- Saved work leads with its result. Original requests and completed execution receipts are expandable, and
  completed tasks no longer display empty model-run controls or claim that approved work still awaits review.
- Read result opens the existing work view. On mobile, the composer disappears while reading/reviewing; desktop
  keeps the adjoining chat. The focused result route survives reload.

## Verification

- Free disk before the final builds: approximately 317 GiB.
- Company test build: zero warnings/errors, 6.51 seconds. Core test build: zero warnings/errors, 2.90 seconds.
- Assignment, fake API-provider and context-draft checks: **23 passed**, 2.767 seconds. Module validation:
  **16 passed**, 0.107 seconds. Existing measured queue and conversation scenarios: **2 passed**, 39.302 seconds.
  Total: **41 test cases passed**. Initial compile/assertion mistakes in the new tests were corrected before this run.
- Expanded business-drafting Chrome journey passed with fictional data: discovery, approved brief, hiring,
  lost preparation response, exact packet review, one authorized starter plan, approval before completion,
  reload, collapsed request/receipt, focused mobile reading and desktop/mobile overflow checks.
- Existing task-handoff Chrome journey passed after teaching the check to expand the deliberately collapsed
  receipt. Existing sandbox review/output/handoff Chrome journey passed with fake execution. No real container ran.
- An initial browser check raced completion against a backend read; it now waits for the UI's confirmed completed
  state before inspecting persisted data. The final business journey passed after the Read result change.
- Company UI recovery checks and TypeScript/Vite build passed. Affected C# and frontend/browser files were formatted.
  Final assets: `index-Bb-WN9ND.js`, `index-DUWlZy7i.css`. Inspected 390-pixel mobile and 1440-pixel desktop screenshots.

Ignored local receipts: `test-results/first-deliverable-build.log`, `first-deliverable-core-build.log`,
`first-deliverable-tests.log`, `first-deliverable-module-tests.log`, `first-deliverable-legacy-regressions.log`,
`first-deliverable-web-build.log`, `first-deliverable-browser.log`, `first-deliverable-handoff-browser.log`,
`first-deliverable-sandbox-browser.log`, `first-deliverable-ui-tests.log`, `business-drafting-browser.json`,
`first-deliverable-mobile.png`, and `first-deliverable-desktop.png`. Browser harnesses disposed their own fixture
hosts and temporary browser/data roots. No live model calls, external requests, deployment or spending occurred.

## Remaining acceptance

The synthetic result proves the lifecycle, bounds and review controls; it does not prove model quality or customer
value. Real onboarding, first-deliverable usefulness and repeat-use observations remain pending. The three-phase
product goal is still incomplete.
