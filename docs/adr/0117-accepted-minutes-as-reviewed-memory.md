# 0117: Turn accepted minutes into separately reviewed memory

Date: 2026-10-26

Status: Accepted; extends ADR 0060 and ADR 0094. Verification is recorded separately.

## Context

Accepted meeting minutes were durable records but had no cited handoff into organizational memory. An owner had
to retype a decision without a link to the minutes. Automatically indexing a discussion would also confuse
acceptance of its record with approval to use each statement as future employee guidance.

## Decision

- Offer owners **Remember a decision** on closed meetings. Open the memory page with that meeting's exact ID in
  the URL and offer an explicit editable draft. Preserve an unfinished draft or unresolved operation receipt.
- Copy at most the opening 600 characters as a starting passage and exact citation. Ask the owner to write one
  reusable decision, check the company/department/employee audience and review date, then save and separately
  approve it. This is a text handoff, not an additional model call or automatic extraction.
- Permit one source per memory: an approved work result or accepted meeting minutes. Minutes must be closed,
  have an outcome digest and contain the exact quote. Pin the meeting ID, accepted version, digest and quote in
  the saved memory and its bounded retrieval excerpt. Source reads use the existing company membership boundary;
  memory mutations retain owner-only authority.
- Recheck accepted source state/version/digest/quote when approving, retrieving, maintaining or analyzing memory.
  Prepared task context uses the existing memory freshness fence. Revoked or changed memory cannot authorize
  dispatch of an already prepared task.
- Treat the owner's accepted minutes as the source record. This does not automatically approve individual
  discussion artifacts or represent their model observations as independently verified facts. Follow-up work
  still needs its existing task/plan approvals.
- Preserve the selected minutes citation in model-suggested revisions. Changing or removing that citation makes
  the suggestion's provenance invalid; the owner may discard it and start an independent record.
- Provide a source preview, explicit retry after a failed source read, saved citation navigation and an exact
  accepted-meeting destination. Source failure preserves the editable text and blocks saving until verification.
- Keep existing memory wire shapes and digests unchanged when no meeting citation exists. Optional meeting
  provenance is omitted when null; the existing JSON-backed memory storage needs no schema migration.
- Deleting a never-approved memory draft clears its minutes citation and prior operation-response copies with
  the existing tombstone/replay rules. The source meeting, audit metadata, retained task copies and backups have
  separate retention lifecycles and are not erased by this action.

## Limits

Local fictional checks establish approval, provenance, isolation and recovery behavior. They do not establish
that an actual meeting produces useful decisions, that lexical retrieval finds every relevant decision, or that
customers return or pay for the workflow. Closed minutes have no public edit command; stale-source checks also
protect retained records if that source changes through a later migration or administrative repair.
