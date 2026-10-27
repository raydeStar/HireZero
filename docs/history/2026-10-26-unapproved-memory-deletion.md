# Never-approved memory deletion — 2026-10-26

A requirements audit found an independent X6 implementation gap while real runtime/provider/customer acceptance
remained unavailable: discarded memory had no deletion path and continued consuming its retained-content quota.
This pass implements the bounded owner action in [ADR 0103](../adr/0103-unapproved-memory-deletion.md).

## Changes

- Owner-only deletion requires the exact never-approved draft version, digest and distinct confirmation. Approval
  and deletion share the existing transaction. Previously approved, revoked, expired or replaced memory cannot
  use this action.
- The saved definition, citations, audience, replacement and report provenance are cleared. Matching saved command
  responses become tombstones, while request identities and fingerprints still prevent old saves from replaying.
  Deleted records stop counting toward the 200-record/1 MiB retained-content allowance.
- Existing audit history is preserved, including titles older discard events may contain. New discard events
  omit the title. Source work, original reports, other drafts, other browser copies, exports and backups remain.
- The UI requires separate confirmation, clears matching browser edits, preserves unrelated drafts, removes a
  confirmed deletion immediately even if refresh fails, and ignores older list responses. Lost responses recover
  through the persisted operation receipt without repeating deletion.
- The delivery ledger's P2.7 row now reflects the already completed cross-phase regression audit rather than
  incorrectly listing that audit as still missing. No external acceptance status was promoted.

## Verification

- **35 focused backend tests passed**, including five new deletion checks. Coverage includes ownership, tenant
  isolation, exact consent, saved-response erasure/replay, retained quota, approval races, report-derived drafts,
  unchanged approved sources/reports, existing memory retrieval and HTTP authorization/recovery.
- The extended **memory desktop/mobile Chrome journey passed**. It holds an older list response, forces a
  post-deletion refresh failure, verifies matching browser edits disappear, loses a deletion reply, reloads and
  reconciles the receipt while preserving an unrelated unsaved draft. Existing approval, renewal, feedback and
  source-revocation cases also pass. The mobile deletion confirmation was visually inspected without overflow.
- The related **memory-analysis desktop/mobile Chrome journey passed**, including suggested revisions through
  separate owner approval. Both fixture hosts and browsers exited through their cleanup paths.
- The shared company UI recovery check passed. TypeScript/Vite and the Host/Core/test builds passed; Vite retains
  its existing large-chunk advisory. The new race assertion initially triggered an xUnit analyzer and was fixed.
  A report comparison initially compared array references and was corrected to compare serialized contents.
  The existing HTTP test initially hit restricted localhost sockets; its permitted rerun passed.
- Free disk space before the build was about 350 GiB. No production data, live models, payments, runtime engine,
  deployment or external publishing was used. Test fixtures are fictional.

Evidence is in ignored `test-results/memory-delete-*` logs and `memory-browser.json`. Full historical/company
erasure, backup retirement, actual containment qualification, hosted provider/payment acceptance and real
customer usefulness/retention remain open. This is incremental verified work, not completion of all three phases.
