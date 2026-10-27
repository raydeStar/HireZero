# 0094: Turn memory findings into separately approved revisions

Date: 2026-10-26

Status: Accepted; fictional lifecycle and owner journeys verified. Real suggestion quality remains unverified.

## Context

[ADR 0091](0091-owner-reviewed-memory-consistency.md) lets the owner check selected records for possible
conflicts, duplicates and stale guidance. Its reports link to the existing records but require the owner to write
every correction from scratch. The proposal calls for advanced memory maintenance while preserving owner control
over authoritative facts. A report should help with the editing work without deciding disputed facts for them.

## Decision

- Permit an optional revision on a cited finding: one target memory ID, up to 1,200 characters of suggested
  statement and 600 characters of rationale. The target must be cited and selected; empty, unchanged, oversized
  or otherwise malformed suggestions invalidate the returned report. Existing findings without suggestions
  remain supported. The existing request, output, time and usage limits apply; no additional provider call occurs.
- Instruct the model to preserve unresolved uncertainty and omit unsupported revisions. It cannot choose which
  conflicting statement is true, invent external facts or grant permission. Exact quote checks establish source
  provenance, not semantic correctness; the owner still reviews the proposed wording.
- Prevent broader disclosure through a proposed revision. Every selected record, including unquoted records,
  must be eligible for the target audience: company-wide sources can inform any audience, department sources
  only that department or its employees, and employee sources only the same employee. Reject the report if a
  proposed revision violates this rule.
- An explicit owner action opens one durable draft for the exact report version and finding digest. It preserves
  the target's type, audience and work citation, sets a visible editable review date thirty days ahead, and binds
  the original record as the replacement. Repeated opens return the saved draft, including owner edits or a later
  discard. They never create another model request or resurrect a discarded draft.
- Store report/finding provenance in an optional input field included in the memory digest. While it is a draft,
  all selected source revisions and lineage must remain current at creation, save and approval. Edits cannot
  remove that provenance or change the inherited type, audience, work citation or replacement. Wording, title,
  rationale and review date remain editable. A stale suggestion can be discarded; a fresh check or independent
  owner-authored revision is a separate action.
- Reuse the existing atomic approval-and-replacement operation. Until approval the old record remains active
  and the draft is excluded from retrieval. After approval the new record is owner-authoritative; the historical
  report naturally becomes stale because its target was replaced. That does not invalidate the approved revision.
  A subsequent manual renewal starts a new owner-authored revision, retaining history without inheriting the old
  report's now-stale draft constraint.
- Show suggested wording and rationale next to its quoted finding, then the earlier statement in draft review.
  Preserve unfinished browser notes and persist pending operation IDs before dispatch. Recovery reads the original
  receipt. Expose an explicit discard-unsaved-edits control and prevent another revision editor from overwriting
  unfinished edits. Changed source records disable approval and explain the next action.
- Omit optional null fields from serialization, preserving old memory and finding digests. No schema migration,
  runtime upgrade or new integration is required. Core remains independent of roles and providers.

## Consequences

The owner can review and edit a proposed correction instead of reconstructing it from a report. This is an
editing aid with explicit acceptance, not autonomous resolution of conflicting business knowledge. Automatic
whole-company scanning, semantic retrieval, real model quality and customer usefulness remain separate work.

Verification: [memory revision receipt](../history/2026-10-26-memory-revision-drafts.md).
