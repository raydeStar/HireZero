# ADR 0148: Employee action recovery survives reload and navigation

Status: implemented; extends ADRs 0011 and 0114.

## Problem

Employee actions retained their request identity only in React state. After a lost response, reloading or
leaving the conversation discarded it and offered a new Run button. Server idempotency protects the same
request ID, but cannot recognize a fresh ID as the earlier action. Some integrations can perform billable
classification, so this is more than a lost form. A confirmed save followed by a failed workspace read also
compressed the recovery controls behind longer explanatory text.

## Decision

- Before sending an action, durably retain its request ID and submitted inputs in the existing member-bound
  session storage, keyed by company, employee and conversation. Verify the write; unavailable storage prevents
  dispatch. Invalid saved receipts prevent new action dispatch rather than silently abandoning an unknown run.
- Keep the receipt through known completion until the owner explicitly starts another run. On reload, restore
  the inputs and open the action with **Check saved status**. Submitted inputs stay locked while unresolved.
- Recovery reads the original operation, verifies the action/employee/conversation, and reads the current task.
  It never posts or resubmits, including after a missing receipt, failed read or mismatched result. A remembered
  task ID avoids depending on the original operation response after it has been confirmed.
- An admitted but still-gathering task is not completion. Keep status recovery available and prevent another
  run until a terminal/review state is confirmed. A known pre-admission refusal permits correcting the form;
  lost replies and server failures retain their receipts. No automatic retry is introduced.
- Remount action state when the conversation changes. A late reply from an unmounted form cannot rewrite a
  receipt already recovered or cleared in a newer form. Existing session-change checks still apply.
- Show recovery controls before long status text, collapse submitted inputs, and let buttons wrap. A transient
  workspace-read notice must not obscure the action's own recovery controls.

## Boundary

This covers actions started after the update in the same retained browser tab/session. It does not recover
previously discarded identities, restore cleared browser storage or prove external completion after a worker
failure. Existing server receipts, admission rules, tool permissions and owner review remain authoritative.
Session storage is not encrypted storage; inputs follow the existing private-draft/member-isolation policy.

This change neither enables Jev nor changes model routing. The separate Jev integration must use reviewed
company-scoped credentials, explicit metered consent and non-replaying outcomes before any paid decision call.
See [verification and integration findings](../history/2026-10-27-action-reload-recovery.md).
