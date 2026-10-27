# 0101: Completed-task cost coverage in Progress

Date: 2026-10-26

Status: Accepted; local verification recorded separately. Actual cost inputs and pricing acceptance remain pending.

## Context

Progress exposed model spending, while ADR 0097 kept operating expenses in individual task reports. Owners could
not see how many completed tasks had usable cost evidence or an average that included those recorded expenses.
Summing recorded reviews directly would double-count corrections and preserve stale costs after usage changed.

## Decision

- Add an optional owner-only `taskCosts` section to the existing progress response and metrics export. Existing
  progress fields retain their meaning. Resolve current membership inside the same database snapshot as the
  report; active viewers and approvers retain ordinary progress without the operating-cost section. Anonymous
  internal reads omit it, unknown/revoked subjects are refused, and HTTP responses use `Cache-Control: no-store`.
- Reuse the task report's exact reconciliation rules. Read and group each company ledger once for the report,
  then evaluate completed work tasks against current usage, attempts, compute cleanup and the latest recorded
  expense review. Draft corrections do not replace recorded evidence. Recorded corrections replace earlier
  amounts; they are never added to them.
- Show completed and reconciled task counts, missing reviews, stale reviews, and current reviews whose usage is
  incomplete. These gap counts partition completed tasks lacking a total. Offer a direct link to a task needing
  attention without including its business text in the report.
- A subtotal includes only tasks with a current reconciled total and stays null when none qualify. Its category
  breakdown has the same cohort. An average appears only when every completed task qualifies and at least one
  exists. Known zero expenses remain zero; absent evidence stays unknown. No automatic cost estimates or billing
  charges are created by opening Progress.
- Separately identify model-price estimates and company-key-funded model spending already included in the
  subtotal. Owner-entered costs retain their saved receipt/allocation basis; the aggregate does not independently
  verify invoices. Brief generation, chat, unfinished work and unallocated company expenses are outside this
  completed-task cohort. The result is neither platform profit nor a customer charge.
- Export only aggregate costs, counts and the next review's record ID/reason. Do not export expense basis text,
  member identities, business content, provider secrets or task results through this view.

This completes the local aggregation path for measured task costs. Real invoices, defensible shared allocations,
customer usefulness, conversion/retention observations, launch pricing and merchant acceptance remain separate.
