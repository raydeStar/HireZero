# Script output review, completion and handoff

Implements [ADR 0069](../adr/0069-reviewed-sandbox-output-reuse.md), advancing P3.2 and P3.3 without reducing the
full product scope. A saved script report can now become reviewed work instead of leaving its parent task waiting.

## Changes

- Full UTF-8 output download plus exact selected-output/excerpt preparation into the existing artifact flow.
  A task result is bounded to 12,000 selected characters; the complete output remains unchanged and downloadable.
- Source lineage pins job, packet/result/output digests, filename and selection range. Existing content sanitation
  precedes artifact review. Preparation, approval, completion and employee sharing remain distinct actions.
- Duplicate operation receipts and repeated exact selections resolve to one artifact. Cleanup revisions do not
  invalidate the immutable output selection. Disabled compute/runtime does not block reuse of saved valid results.
- Current source authority is checked across approval, completion, evidence capture/dispatch, handoffs, memory
  and publication. Input or script revocation blocks future reuse while preserving historical content.
- Dedicated bounded transport supports Unicode excerpts beyond the generic command limit. Origin metadata cannot
  be authored through ordinary artifact creation. Optional origin JSON preserves existing artifacts without a schema change.
- The UI moves focus to the new task result, offers download and return-to-review controls, and stops offering new
  scripts for completed tasks. A reviewed script result can complete a task and enter an explicit Developer handoff.

## Verification

Free space exceeded 317 GiB before builds. All data/providers were fictional. No live model calls, real container
execution, daemon setup, external publication, payment, commit, push or deployment occurred.

- Final integration build: no warnings/errors. Focused sandbox/lifecycle/qualification suite: **45 total,
  44 passed, one real-runtime test skipped**, 3.832 seconds. Six added facts cover durable/idempotent reuse,
  owner/company scope, exact text, source revocation through downstream use, long outputs and withheld/failed output.
- Host build: no warnings/errors. Affected host, memory, Wiki publication, employee action and marketing publication
  tests: **32 passed**, 5.965 seconds. The host test submits a 12,000-character Unicode output through the dedicated
  route after runtime execution is disabled, then verifies exact review text and source lineage.
- Existing company and task-handoff scenario groups: **2 passed**, 3.850 seconds, including the existing company
  isolation/concurrency/fencing/budget/coordination checks. Combined backend total: **78 passed, one explicit skip**.
- Expanded script browser journey passed: actual file download, exact-excerpt validation, lost output-preparation
  response yielding one artifact, focus on the saved review, separate approval/completion, then a Developer handoff
  with no model/repository execution. Earlier cleanup/cancellation/allowance checks still pass.
- Existing Marketing-to-Developer handoff browser regression and company UI receipt guards passed.
- TypeScript/Vite and scoped formatter checks passed. Inspected 1440-pixel desktop and 390-pixel mobile output
  screenshots: readable download/review controls, completed task state, no horizontal overflow or browser exceptions.
  Final assets: `index-ClV36NqO.js`, `index-b2D2_azI.css`.

Local ignored evidence: `test-results/sandbox-output-build.log`, `sandbox-output-tests.log`,
`sandbox-output-host-build.log`, `sandbox-output-host-tests.log`, `sandbox-output-handoff-regressions.log`,
`sandbox-output-web-build.log`, `sandbox-output-browser.log`, `sandbox-output-existing-handoff-browser.log`,
`sandbox-output-ui-tests.log`, `sandbox-browser.json`, `sandbox-mobile.png`, and `sandbox-desktop.png`.
The browser harnesses disposed their hosts/profiles and the test download under their own temporary roots.

## Remaining acceptance

Real runtime qualification, artifact usefulness with real workloads, plan/schedule composition, measured compute
costs and further developer/browser capabilities remain pending. The full phase ledger also retains commercial,
integration, privacy/retention and real-customer validation requirements; no complete phase is claimed here.
