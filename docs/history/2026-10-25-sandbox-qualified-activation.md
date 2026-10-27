# Qualified sandbox activation and recoverable disabled state

Implements [ADR 0068](../adr/0068-qualified-runtime-activation.md). This advances P3.2; it does not complete the
three-phase product goal or claim a production-qualified engine.

## Changes

- Added typed operator configuration and a qualification-based Docker provider composition path, off by default.
  The opt-in runtime test now emits a private seven-day qualification receipt only after checks and cleanup pass.
- Bound provider identity to engine, installation and immutable image. Replacement-engine absence cannot prove
  cleanup of an original resource. Recovery on the original engine remains possible after execution support changes.
- Bounded receipt loading, checked exact scope/check set/expiry, compared CLI hash and engine/runtime profile,
  periodically refreshed readiness, and rechecked before provisioning. Cancellation before provisioning records
  a confirmed unsent result instead of inventing an unresolved resource hold.
- Separated new-work readiness from recovery capability in the host and UI. Paused or expired execution does not
  disable recovery. New proposals/approvals are refused while unavailable; previously accepted request receipts
  still reconcile idempotently. A changed provider binding returns an actionable recovery error.
- Added human-readable runtime states and refresh in Usage, preserving unsaved limit edits. Script results retain
  a usable recovery button when the runtime is configured but new execution is disabled.

## Verification

Free disk space exceeded 317 GiB before builds. No live model calls, Docker/WSL starts, image builds/pulls,
real container executions, payment actions, commits, pushes or deployments were performed.

- Integration build: no warnings/errors. Focused lifecycle/provider/qualification suite: **39 total, 38 passed,
  one real-runtime qualification skipped**, 2.506 seconds. Covers engine replacement, loss of execution support,
  expired/disabled qualification recovery, changed executable/profile/receipt, invalid scope/checks and early cancellation.
- Host build: no warnings/errors. Settings and sandbox host suite: **9 passed**, 1.990 seconds. Covers complete
  configuration, disabled-runtime admission, idempotent accepted approvals, recovery, tenant isolation and actionable
  provider mismatch. Combined backend evidence: **47 passed, one explicit runtime skip**.
- Script browser journey passed: exact owner review, selected inputs, lost approval reply without duplicate work,
  retained output after cleanup failure, recovery with both company compute paused and runtime execution disabled,
  revocation withholding late output, released allocations, desktop/mobile layouts and no browser errors.
- Usage browser regression and company UI receipt guards passed. TypeScript/Vite build passed; inspected
  1440-pixel desktop and 390-pixel mobile result screenshots showed readable output and no horizontal overflow.
  Final assets: `index-Ni4N0nVp.js` and `index-b2D2_azI.css`.

Compact local evidence is under ignored `test-results/`: `sandbox-qualification-build.log`,
`sandbox-qualification-tests.log`, `sandbox-activation-host-build.log`, `sandbox-activation-host-tests.log`,
`sandbox-activation-web-build.log`, `sandbox-activation-browser.log`, `sandbox-activation-usage-browser.log`,
`sandbox-activation-ui-tests.log`, `sandbox-browser.json`, `sandbox-desktop.png` and `sandbox-mobile.png`.
Fixture hosts and temporary browser profiles were disposed by their test harnesses.

## Remaining acceptance

Real runtime installation, operator qualification and workload security review remain required before activation.
The receipt is operator evidence and a configuration drift check, not a signed attestation or proof of every
containment property. Operators must preserve the original binding for unresolved work and requalify runtime
updates. Artifact promotion, plan/schedule composition, actual compute costs, scoped browser execution and the
other product-phase requirements remain on the full delivery ledger. Commercial billing terms and real customer
validation are still pending.
