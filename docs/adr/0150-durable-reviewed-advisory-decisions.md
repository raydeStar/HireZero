# ADR 0150: Reserve and reconcile reviewed advisory decisions durably

Status: core lifecycle implemented; provider binding, application review and live acceptance pending.

## Context

[ADR 0149](0149-company-bound-jev-decision-transport.md) supplies a company-bound decision transport, but its
in-memory dispatch latch cannot survive a host restart or account for uncertain spending. A recommendation
must not inherit authority to execute the action it recommends. The text-model route is a separate workflow.

## Decision

- Keep the core provider-neutral. A configured integration prepares an immutable company-bound packet and
  verifies its current credential/configuration revision. Defaults refuse preparation and execution; no live
  provider is configured by this change. Review binds the exact JSON, choices, model, funding revision,
  intended allowance, expiry and policy digest. Only an active owner may prepare, authorize, cancel or reconcile.
- Use a ten-minute review expiry, one local 30-second execution deadline, an intended allowance of 0.01 to
  100 US cents and a 20,000-token planning reservation. These are local soft limits, not provider-enforced
  spending guarantees. The future review UI must show these terms before authorization.
- Require an exact current version/digest and explicit soft-limit acceptance. Reserve company and monthly
  usage before dispatch; preserve the packet's funding source. Uncertain owner/model work blocks another
  admission. Apply the existing pause and usage limits.
- Consume one persistent dispatch claim in the same transactional store. Replayed command receipts cannot
  acquire another claim. If authority, expiry or the work pause changes before that claim, settle zero usage
  without invoking the provider. A restart after admission conservatively retains an unknown outcome and
  reservation, even when dispatch was not observed; it never retries the request.
- Watch authority while the invocation runs. Cancellation, a lost reply, incomplete accounting, conflicting
  receipts or an allowance overrun retains the reservation and withholds advice. Missing charges never become
  zero. Preserve the largest valid observed token count and charge, including partial accounting across months.
- A terminated late invocation may settle complete receipt-bound usage, but cannot promote advice after a
  fence or restart. Complete valid advice is only an advisory result; it creates no tasks, work approvals or
  downstream actions. Invalid advice with valid accounting settles as withheld.
- Reconciliation requires current owner authority, the exact record revision/digest, confirmed provider
  termination, a receipt, complete usage and evidence. It cannot reduce previously observed tokens/charges or
  replace an existing receipt. It settles once and never accepts advice or resubmits work.
- Include decision usage in the existing usage/spend views, flag unresolved work in retirement review, and audit
  lifecycle metadata without copying private request JSON. Operation receipts require owner access. Append a
  schema migration while preserving existing migration checksums.

## Remaining delivery

Bind the company-scoped Jev transport to this lifecycle, recheck the exact current packet immediately before
sending, build review/status/reconciliation UI, and exercise those flows end to end with fictional providers.
An owner-approved real Jev decision remains a separate gate for provider accounting and usefulness. This
change supplies no live provider dispatch, changes no Luna routing and activates no scheduled work.

See [lifecycle and migration verification](../history/2026-10-28-advisory-decision-ledger.md).
