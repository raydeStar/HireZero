# Retired saved-memory deletion — 2026-10-26

Owners can now delete the saved contents of explicitly revoked or replaced memory under
[ADR 0121](../adr/0121-retired-memory-source-deletion.md). Active and overdue records first require revocation
or replacement. This complements existing never-approved draft deletion; it does not perform company erasure.

Deletion clears the current definition, audience, citations, replacement pointer and keyword index, and scrubs
prior command-response copies of the same record. A content-free tombstone preserves operation identities and
version/digest evidence. Old saves cannot replay or restore the text. Earlier task/context copies, reports,
other drafts, source records, audit/event metadata, exports, browser copies and backups remain; the UI explains
these boundaries before separate exact consent.

Approved replacements and unrelated unsaved drafts survive. A separately saved and reviewed manual revision
can bind a deleted retired-source tombstone without reviving it. An older revision cannot silently use a stale
source version. A deleted never-approved draft cannot become a replacement source. The UI also no longer
offers the invalid revoke action on a record already revoked.

## Verification

- Web production build and TypeScript passed, with the existing large-chunk advisory.
- Locked company-test build passed with zero warnings/errors; changed C# and web/test formatting passed.
- **46 focused memory and analysis facts passed**: 45 in the initial run; the remaining HTTP owner-access
  fact was blocked by restricted localhost/temporary-key access and passed individually with the required
  local access. Five new facts cover saved-response erasure/non-replay, preserved historical task packets,
  owner/company/exact-consent refusal, preserved replacements/drafts, tombstone revision behavior and the
  concurrent deletion/replacement race. These are fictional tests, with no live model calls.
- The expanded memory browser journey passed. It confirms separate deletion consent, no delete control on
  active memory, explicit revocation, a lost source-deletion reply recovered after reload, one deletion
  dispatch, unchanged replacement content/version, retained unrelated unsaved edits, and cleared cited
  feedback/audience fields. Existing draft, citation, retrieval and renewal paths still pass.
- Desktop and 390-pixel mobile deletion views were visually inspected; no horizontal overflow or browser
  errors were recorded. The fixture and its temporary browser exited and cleanup completed.

Compact private receipts/captures remain under ignored `test-results/`: `memory-browser.json`,
`memory-source-delete-desktop.png`, `memory-source-delete-mobile.png`, and `memory-source-owner-check.log`.
Diff whitespace checks passed. No deployment, real customer deletion, provider action, runtime activation or
change to the pending script host task-budget proposal occurred.

This strengthens P3.5 and X6. Full company erasure, backup retirement, real provider acceptance and all customer
validation targets remain separate work in the delivery ledger.
