# ADR 0106: Durable company pause for new work

Status: accepted, 2026-10-26. Extends ADRs 0061 and 0105.

## Decision

Add an owner-only **Pause new work** control in Companies and a visible workspace banner. Store the flag
as `WorkPaused` in the existing company usage policy. Older JSON records default to false; no schema migration
or duplicate lifecycle store is needed. The flag shares the policy's version and transaction so changing usage
limits and pausing cannot overwrite each other. Updating ordinary model limits preserves the work pause;
resuming preserves both model and compute limits, including their independent pause switches.

Check current owner membership inside the mutation transaction, require the expected version and an explicit
pause/resume decision, and retain the ordinary operation receipt and audit event. A repeated receipt lookup
does not toggle anything. Read-only status and the company snapshot expose only the setting, version and time.

Apply the gate at durable admission: model reservations (including profile generation, memory analysis and
coding), script/browser starts, employee action starts, business lookup/source-refresh starts, publication
claims and schedule claims. Queued ordinary model work is not claimed while paused. The pause and these
admissions serialize on the existing company transaction. Core stays independent of role and integration types.

An admission committed before the pause may still begin or finish afterward, including its already-approved
tool calls. Do not revoke its authority, erase a hold or block result persistence, cancellation, cleanup or
uncertain-outcome recovery. This is not an emergency kill switch and not proof that execution has stopped.
Existing permission, expiry, source-revocation, billing and budget checks remain in force. Resume does not
refresh approvals; schedules keep their existing missed-slot rules rather than replaying every missed occurrence.

Metadata/draft editing, sign-in, integration setup, result review, notifications, billing management and
maintenance remain available. These are intentionally not a comprehensive company write freeze. Closing a
company still requires stronger coordination and verified settlement across all stores and external systems.

## UX and recovery

Explain the admission boundary beside the control. Require explicit consent to resume queued/scheduled work.
Prevent duplicate clicks and retain a pre-dispatch receipt using the shared command helper. If the outcome or
latest state is unavailable, disable further changes and offer a read/receipt refresh. Never toggle to recover
a lost response. A successful control read/change invalidates older workspace snapshot reads, and version checks
prevent older control results from replacing newer ones. Display a workspace-wide pause banner to all members;
only owners see the management action.

## Limits

This improves the owner's execution control and is groundwork for X6, not full-company retirement. No live
provider/runtime qualification, data erasure, subscription cancellation or customer validation follows from a
pause. Verification with fictional inputs is recorded in the dated history receipt.
