# ADR 0151: Review Jev decisions through the saved company API account

Status: application workflow implemented and fictionally verified; live acceptance pending.

## Context

The decision transport and durable ledger from ADRs 0149–0150 are separate from text generation. The existing
Integrations page tells owners that Jev through OpenRouter uses the AI model connection, but the transport
factory added in ADR 0149 reads the optional tool credential. Connecting that factory directly would require
a duplicate key or silently choose a different account. Neither matches the owner's intended setup.

## Decision

- Bind the decision workflow to the existing encrypted company AI account when its provider is OpenRouter.
  Retain the exact company identity and saved credential revision. Never copy, display or fall back to another
  key. The optional tool factory remains separate and is not used by this application workflow.
- Keep text routing unchanged. Owners can retain their host/Codex connection alongside the API account. A
  Jev decision neither selects another text model nor routes a future job to its recommended action.
- Add owner-only decision history, exact packet review, one-shot execution, status recovery, unstarted
  cancellation and usage reconciliation to Integrations. Preparation is local and spends nothing. Context is
  the owner's explicit situation, question and two to eight options; company knowledge is not silently added.
- Require an explicitly versioned Jev decision model. Reconstruct a saved request through the same bounded
  transport validator and require exact bytes/digest before dispatch. Check the saved account revision again
  when reading its credential. The reviewed ledger supplies spending admission and the persistent claim.
- Show the actual external JSON and destination, account, intended cents allowance, token reservation,
  deadline, expiry and soft-limit terms. Require separate data-review and one-request authorization controls.
  Display provider confidence as an estimate, not an observed probability of success.
- Keep request receipts in member/company-scoped browser storage before dispatch. Missing receipts and lost
  responses are checked read-only, never resubmitted. A denied storage write prevents dispatch. If a caller's
  pre-dispatch receipt callback fails, clear only that never-sent operation from the shared command guard so
  the app does not become stuck trying to find an operation that cannot exist.
- Preserve owner-only access to private packets and operation receipts, including a fresh role check after
  an awaited provider call. Changing an account invalidates existing reviews; the UI refreshes that status
  while retaining the owner's unfinished form.

## Boundaries

No automatic router, downstream execution, fallback, retry, model request on page load, or automatic approval
is added. Real provider compatibility, charges and usefulness still require separately authorized live
evidence. Existing Luna authorization does not authorize paid Jev requests.

See [workflow verification](../history/2026-10-28-reviewed-jev-workflow.md).
