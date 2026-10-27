# 0097: Reconcile completed-task operating costs against saved usage evidence

Date: 2026-10-26

Status: Accepted; fictional backend and owner-journey verification. Actual invoices and customer economics remain separate acceptance.

## Context

The product proposal requires real cost per completed task, including retries and non-model expenses. Monthly
usage already retains model receipts and price snapshots, while isolated jobs retain allocation and cleanup
evidence. Neither a model subtotal nor a compute safety allowance represents the complete operating cost.

## Decision

- Add an owner-only task report aggregating model usage across all attempts, plus started script/browser jobs.
  Keep reported model charges, estimates, company-key funding, unknown prices and outstanding usage distinct.
  Compute allocation and observed wall time are evidence, not prices. Exclude content, tool outputs and credentials.
- Let the owner save a draft of actual non-model charges or documented allocations in five categories: compute,
  tools, retrieval, storage and operating overhead. USD amounts have six decimal places at most. Unknown amounts
  remain null; a known zero must be explicit. Basis notes are private and bounded to 1,000 characters.
- Require a completed task, explicit owner recording, exact version/digest, all five known amounts, a basis and
  no outstanding model/resource holds. Bind each draft to a digest of task state/generation and usage evidence.
  Changing that evidence invalidates approval and any prior current total. No charge or allowance is changed.
- Retain immutable recorded reviews. Corrections create a new draft and require fresh confirmation; one draft
  per task and at most 100 retained reviews bound the history. Show the most recent recorded reconciliation.
  If any model price is unknown, keep the total unavailable even when non-model expenses are recorded.
  A total using model estimates is labeled an estimate. Owner-recorded expenses are not provider verification.
- Add migration 28 for company-scoped reviews, preserving earlier migration checksums and the frozen initial
  schema. Gate both report reads and operation-receipt recovery to owners; another company's IDs stay unavailable.
  Audit the exact amounts/digests and actor without copying private basis notes into audit/event messages.
- Place a collapsed Task costs panel in work details. Preserve unsaved edits across refresh/reload, record a
  recovery receipt before dispatch and retain it through a failed post-write refresh. Check an existing receipt
  before another mutation. Export only on explicit request; explain that the export includes private basis notes.

## Consequences and limits

This supports manual reconciliation with actual invoices; it does not automatically ingest invoices, measure all
shared infrastructure costs, allocate expenses independently or prove sustainable pricing. BYOK costs stay visible
as company-funded spending. Monthly billing, customer prices, Stripe charges and provider configuration are
unchanged. Real full-cost observations, model quality, customer usefulness and commercial validation are pending.

See the [verification receipt](../history/2026-10-26-task-cost-reconciliation.md).
