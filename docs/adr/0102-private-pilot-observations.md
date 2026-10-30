# 0102: Private offline pilot observations and denominators

Date: 2026-10-26

Status: Accepted; preparation and fictional verification only. No customer pilot is claimed.

The [2026-10-29 first-draft reporting correction](../history/2026-10-29-first-draft-usefulness-evidence.md)
separates exact saved-result usefulness from workflow completion. The completed-state requirement below
now applies only to compatibility with older task-only exports; it is not a new approval step for using a draft.

## Context

The product proposal requires observed onboarding usefulness, useful recurring work and paid value. Saved workflow
metrics and fictional E2E tests do not supply these judgments. The repository lacked a repeatable pilot protocol
and a way to combine private observations with the existing Progress export.

## Decision

- Supply a session protocol, an empty cohort manifest and an explicitly unclassified participant template.
  Actual contacts, source content, receipts and session notes remain in private operator storage. The optional
  repository-local `.pilot/` working folder is ignored; this does not replace encryption or retention policies.
- Add a dependency-free Node report command. It reads a bounded manifest and explicitly named sibling Progress
  exports without networking, writes JSON only to stdout, and never mutates app state, sends outreach, runs a model
  or charges a customer. Refuse path traversal and companions resolving outside the manifest directory.
- Require explicit customer/fictional classification and report each separately. Reject duplicate pseudonyms,
  reused export files and duplicate company snapshots. Missing observations are not failures; expose assessed
  and whole-cohort denominators. Missing files remain explicit rather than fabricating an empty company snapshot.
- Link first-deliverable judgments to the exported first assignment. A useful completion requires its saved
  completed state. Repeat usefulness is dated observer/customer-report evidence, separate from request-day or
  scheduled-task counts. Paid/renewed outcomes require the observer to record provider-receipt review and a dated
  follow-up, but the tool never claims independent provider verification or computes a renewal retention rate.
- Reuse owner-exported task-cost coverage. Validate cohort counts and cost arithmetic; pool subtotals by task
  counts, with no complete average until every participant supplies owner cost evidence and all completed tasks
  are reconciled. Preserve estimate/company-key labels. Aggregate amounts to twelve significant digits.
- Output only generic cohort labels, aggregates, declared evidence limitations and an input digest. Do not echo
  source company/task IDs, participant names, receipt text or raw business content. The digest is provenance for
  the selected inputs, not a signature or proof that observations happened.

Real recruitment, participant consent, approved spend, hosted acceptance, runtime qualification, commercial terms,
receipt verification and measured useful retention remain separate work. A passing reporter test or a completed
template cannot satisfy the product's field-validation gates.
