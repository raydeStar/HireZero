# Runtime release and local coding isolation — 2026-10-25

Scope: the local coding lifecycle within the full context-first product goal; [ADR 0063](../adr/0063-explicit-runtime-release-receipts.md).

## Changes

Coding execution now records process stop separately from temporary-file removal. Preflight failures use the
same release path. Only a confirmed stop permits removal of the exact generated scratch/input pair, after
checking its parent, name, ancestors and contents. Links, junctions, locked files and oversized inspections
retain an explicit attention receipt. The checkout, reviewed output and unrelated directories remain intact.

The grant stores an immutable company-scoped receipt. Attention outcomes generate an Inbox notice linking
to the task and show a generated resource reference, without exposing a host path. Older grants acquire no
invented cleanup evidence. Unconfirmed termination retains the existing unknown-outcome hold.

The host's qualified local coding runner is available only to the original local company. Capability output,
integration status and execution requests all enforce this boundary. Other companies cannot borrow the
operator's coding identity or machine. The execution panel explains unavailable coding, keeps the task saved,
and omits checkout/start controls that cannot work. Unknown execution outcomes do not offer a new proposal.

## Verification

- Company test build passed with zero warnings/errors.
- 47 backend cases passed in 33.712 seconds: runtime release, native fake coding, reviewed PR-fix execution
  and owner notifications. Filesystem checks included a real Windows junction and a locked temporary file.
- Following host isolation changes, 23 cases passed in 33.026 seconds: PR-fix and company integration
  isolation. The new HTTP case proves a second company cannot start the qualified host runner, even with
  fixture Git tools and an approved grant. The grant remains unsent. The existing original-company execution
  journey still succeeds. Across these two runs there were 54 distinct cases.
- All 10 execution-panel checks and the company UI recovery checks passed.
- TypeScript and the frontend production build passed after adding the missing capability type.
- The isolated Chrome Marketing-to-Developer handoff journey passed: explicit sharing approval, no added
  coding authority or dispatch, visible unavailable-runtime explanation, no unusable checkout form, scoped
  revocation, earlier work history, and desktop/mobile layout. The 390 × 844 screenshot was inspected.
- Scoped formatting and `git diff --check` passed. The browser fixture now waits for its own processes to
  exit before removing its verified temporary root.

Compact logs and screenshots remain ignored under `test-results/`. All providers were fake; no live model,
external push, publication, payment or production deployment was involved.

## Remaining boundaries

This does not provide a hosted/container worker, stronger OS process containment, crash/orphan recovery,
operator cleanup controls, temporary storage quotas or compute billing. The process-tree stop mechanism is
the existing adapter's mechanism. Global physical-checkout leasing was subsequently addressed in
[the separate lease change](2026-10-25-cross-company-checkout-leases.md); non-default companies remain denied this local runtime. The three-phase goal
remains in progress, including browser execution, premium environments and real product validation.
