# ADR 0158: Preserve owner fields and canonicalize quotation whitespace

Status: accepted

## Context

The first inspectable real public-site draft showed two failures: a model replaced a source line break with a space,
and it attributed the owner's goal to the website. Exact character matching rejected the first quotation. The
application validated the invented goal citation before replacing the field with the captured owner value, so its
promised owner-field preservation could still block the whole draft.

## Decision

For citations, accept differences in whitespace only and store the exact matched substring from the saved source.
Case, punctuation and words remain exact; no fuzzy or semantic matching is introduced. The same company, confirmed
business revision, source selection and length limits continue to apply.

For generated drafts, require the complete unique field-key structure, then replace model values for captured owner
fields before validating extracted facts. The model cannot change those owner values or confer owner status on other
fields. Prompt instructions explicitly request Unknown placeholders for owner fields and forbid using owner input
as website evidence. Concurrent owner changes still invalidate the prepared request.

## Consequences

Page layout no longer invalidates an otherwise exact quotation, and owner corrections take precedence over model
output. This does not establish that a quotation supports every part of a claim. Reviewers still assess support and
usefulness. Historical rejected requests are not reprocessed; manual corrected drafts retain their separate provenance.
