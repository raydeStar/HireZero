# ADR 0103: Explicit deletion of never-approved memory drafts

Status: accepted, 2026-10-26. Extends ADR 0060 and the X6 deletion requirement.

## Context

Discarded organizational memory retained its complete definition and every saved command response, with no
deletion path. Those unused records also continued to consume the 200-record/1 MiB allowance. Revocation and
discarding express different decisions from deleting saved draft content.

## Decision

Add owner-only `memory.delete` with the exact current version, digest and `delete-unapproved` confirmation.
Only `draft` or `discarded` records with no approval timestamp qualify. Reviewed, expired, replaced and revoked
records remain excluded because they can have historical task copies. Approval and deletion serialize through
the existing company transaction; neither stale consent nor a concurrent approval can authorize both outcomes.

Replace the saved definition, source quote, audience, replacement and report provenance with a content-free
tombstone, remove any retrieval terms, and replace every matching memory-shaped operation response in that
company. Retain IDs, request fingerprints, the last digest and deletion version/time. Replaying an old save,
review or suggested-revision request returns the tombstone and never resurrects its contents. A new explicit
request may create a new independent draft from an existing report, subject to its current source checks.
Deleted records disappear from the library and retained-content allowance; tombstones still consume some
database storage. No migration, automatic expiry or model execution is introduced.

Existing audit entries and events are preserved. Older discard events may contain the draft title, so this
operation does not promise erasure of every copy. New discard events omit titles. Cited work, original analysis
reports, other drafts, another browser's storage, exports, backups and database recovery files remain. Full
historical/company erasure and operator backup retirement are separate work.

The UI puts deletion behind a separate disclosure and checkbox, resets consent when the selected version
changes, and explains the retained copies. Confirmed deletion immediately removes the selected record and
matching local browser edits even if the following refresh fails. It preserves unrelated drafts and reconciles
uncertain requests through the saved receipt. Older reads cannot reintroduce the deleted record or their errors.

## Verification

Focused backend facts cover exact owner consent, tenant isolation, immutable reviewed records, operation
response erasure/replay, storage allowance, the approval race, and report-derived draft deletion. The existing
memory browser journey covers desktop/mobile deletion, explicit consent, a stale list response, a failed
post-deletion refresh, matching local edits, a lost reply, reload recovery and an unrelated preserved draft.
Actual results are recorded in the dated verification receipt; fixtures are fictional.
