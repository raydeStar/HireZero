# 0091: Owner-reviewed memory consistency reports

Date: 2026-10-26

Status: Accepted; synthetic verification is separate from real model quality acceptance.

## Context

Saved decisions and feedback can disagree even when each record was approved. Lexical retrieval and review dates
do not identify semantic disagreements. The owner needs a cited review aid without allowing a model to silently
rewrite company knowledge or incur repeated charges after an uncertain response.

## Decision

- Add a role-independent, owner-only memory check over two to eight explicitly selected records. Include approved
  records and previously approved records whose review dates have arrived, provided their cited work remains
  current and their employee audiences remain active. Overdue records remain excluded from employee retrieval.
- Persist the exact source revisions, context, instructions, model/runtime identity and allowance before dispatch.
  Preparing sends no model request. Consume one explicit, unexpired owner authorization through the existing
  serialized provider gate and monthly, subscription and company budget admission controls.
- Use a distinct measured policy: at most 32 KiB application input, one request, 3,000 output target tokens,
  26,000 planning tokens, a 120-second local deadline and 14,000 visible output characters. These are measured
  safeguards, not guaranteed upstream subscription credit or token caps. Company API connections retain their
  existing pre-send price checks and funding receipts. No tools or employee authority are supplied.
- Accept at most six findings: possible conflict, possible duplicate or needs review. Require bounded explanations
  and one to four exact quotes from selected statements. Conflicts and duplicates require two distinct cited
  records. Validate IDs, kinds, lengths and quote membership before importing the report. Citation validation
  proves provenance, not the model's interpretation. An empty report is not a consistency guarantee.
- Keep the report separate from approved memory. Findings are hypotheses for owner review; opening a citation
  preserves an unfinished draft. Revisions and approval continue through the existing memory workflow.
- Recheck exact source versions, source lineage, audiences, runtime and the authorizing owner's active role
  during dispatch and before importing output. Interrupted or stale output is withheld. Historical completed
  reports retain their original snapshots and show when sources cease to be current. Changing the current model
  connection does not rewrite a historical source assessment.
- Preserve uncertain reservations across restart; never replay an uncertain request. A late trustworthy receipt
  may settle cost but cannot import a late report. Owner reconciliation requires exact request/version/digest,
  complete nonnegative usage, provider termination and an explanation; it cannot reduce observed usage or cost.
- Reports, their list and their saved operation receipts require current owner access and use no-store responses.
  A neighboring company cannot address them. Recheck host delivery authority after the model call. Audit states,
  source identities/digests and usage, not statement or report text.
- Add database migration 27 without changing earlier migration checksums. List the latest 100 report summaries;
  exact older records remain individually addressable. Persist pending browser operation IDs before dispatch
  so recovery checks the saved receipt rather than resubmitting the action.

This extends [ADR 0060](0060-structured-organizational-memory.md),
[ADR 0061](0061-monthly-usage-admission-and-cost-receipts.md) and
[ADR 0088](0088-result-feedback-memory.md). Automatic whole-company scanning, model-authored replacement drafts,
semantic retrieval and real model accuracy/usefulness evaluation remain separate work. No provider calls or paid
transactions are part of routine verification.

Verification: [memory consistency receipt](../history/2026-10-26-memory-consistency-reports.md).
