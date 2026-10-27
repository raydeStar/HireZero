# 0086: Paid extended scripts

Date: 2026-10-26

Status: Accepted; fictional lifecycle and owner journey verification. Production runtime qualification and commercial acceptance remain separate.

## Context

Ordinary scripts have a 120-second ceiling. Purchased compute allowances did not grant a distinct execution
tier, and a standard runtime qualification did not prove work beyond that ceiling. Premium execution must
remain bound to both an explicitly purchased plan and a separately qualified runtime, without weakening
company isolation, exact owner approval, accounting or uncertain-outcome recovery.

## Decision

- Keep the default script tier and its 120-second ceiling. Add the explicit `extended-script-v1` tier with a
  maximum of 1,800 seconds per job. Memory, temporary storage, input/output bounds, network denial and secret
  isolation are unchanged. This is bounded batch execution; it does not introduce a resumable environment.
- A reviewed monthly plan may declare `allowance.extendedScriptSeconds` between 121 and 1,800, no greater
  than its included compute seconds and only with a nonzero compute-job allowance. Omission grants no
  extended tier. No production plan, price, overage purchase or recharge is bundled.
- Require current verified paid allowance and extended runtime availability when preparing a review. Bind
  the proposal to the exact plan digest and per-job ceiling. Recheck that authority at dispatch and during
  execution. A changed plan, failed payment or withdrawn runtime qualification prevents new work and cancels
  active extended execution. New approval is required after a plan change; old approvals cannot follow a
  different catalog price or allowance silently.
- Reserve the full requested duration from the existing shared monthly compute pool. A sent run consumes
  that allocation after confirmed release; a confirmed unsent run consumes none. This is allocated runtime,
  not measured CPU time or a monetary charge. Unknown execution or cleanup retains its durable hold.
- Recovery keeps the original resource binding and never executes the script again. Loss of payment or
  extended availability cannot prevent release. Already completed output remains available for download and
  task review while its original task/source authority is current; payment loss does not retroactively erase
  valid work. A result arriving after in-flight authority loss is withheld.
- Extended availability requires the standard qualification checks plus all three exact extended checks:
  `extended-duration`, `extended-deadline`, and `extended-recovery-and-release`. The opt-in operator test proves
  a successful job beyond 120 seconds, an enforced deadline beyond that boundary, receipt recovery and
  confirmed removal. It takes over four minutes and is excluded from routine checks. Old qualification
  receipts enable only standard scripts. Expiry, image/engine binding and configuration checks still apply.
- Runtime probing runs independently of the execution queue, with 30 seconds between readiness checks. Long
  jobs therefore cannot postpone qualification refresh until their completion. Withdrawn capability reaches
  the existing authority watcher; failed monitoring stops execution and surfaces through host supervision.
- The core receives entitlement/runtime capability callbacks and owns durable admission. The sandbox
  integration knows nothing about roles or commercial plans. The host composes these boundaries. Optional
  null fields are omitted from prior request and plan JSON to preserve stored digests; no schema migration or
  runtime pin upgrade is required. Deploying the changed worker still requires a reviewed immutable image
  and fresh qualification before extended execution can be enabled.
- Billing reviews show the per-run ceiling. Script preparation and approval show the chosen tier, exact time
  and allocation consequences; Usage identifies extended work. If entitlement changes, the pending review
  explains the hold and points the owner to Billing and fresh review.

This implements one premium execution class. Long-running browser environments, larger developer workspaces,
actual compute-cost accounting, real isolation qualification and hosted paid acceptance remain open. A
fictional payment or sandbox receipt is not proof of any of those outcomes.

Verification: [extended-script journey](../history/2026-10-26-paid-extended-scripts.md).
