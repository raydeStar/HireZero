# 0118: Preserve private import provenance and review source changes

Date: 2026-10-26

Status: Accepted; extends ADRs 0056 and 0090. Verification is recorded separately.

## Context

Drive previews carried their source URL, version and dates in editable text. Those notes were useful to read but
could not establish which source HireZero actually fetched. Owners also had no direct comparison between an
approved copy and a later version of that allowed source.

## Decision

- After a successful bounded Drive read and the existing connection/owner checks, issue an application-protected
  source receipt. ASP.NET Data Protection uses a dedicated purpose and exact company ID. The receipt records
  provider, file ID, version, modified/read times, extracted-text digest and imported-copy digest. It contains no
  service-account key, access token or document text. It proves what this application observed; it is not a Google
  signature, external attestation, fact verification or authority to access the provider again.
- Keep preview, save and approval separate. Unsaved receipts stay with the member/company draft. No new preview
  table, retained source-text copy or migration is introduced. Existing audit metadata remains unchanged.
- A document save accepts only the protected receipt, not client-authored source metadata. Core calls a host
  verifier and retains the immutable source snapshot. Another company cannot reuse it. The document digest binds
  text, audience and receipt. Comparing the saved text with the recorded copy digest labels owner/normalization
  edits explicitly. Plain text source notes do not acquire trusted provenance.
- Preserve an existing recorded source when editing its draft or explicitly revising that document, even if the
  original host key is unavailable later. This references a retained historical observation, not a fresh read.
  A new file import clears the earlier receipt. Copying a saved document into another company requires that
  company's own preview or an explicitly unverified text copy.
- Include structured source metadata and the edited-text flag in approved retrieval excerpts; exclude the opaque
  receipt from model context. Existing audience, version/digest and document-revocation fences still apply.
- Offer owners **Check Drive source** on recorded approved/revoked copies. Read only the exact recorded file
  through the current connection and allowlist. Recheck owner, connection generation and saved document version
  after reading; withhold stale or unauthorized responses. Failures preserve saved documents and drafts, and
  retries are explicit reads, never document mutations.
- Compare extracted-text digests separately from source versions/modification times. Report unchanged,
  metadata-only change or changed text. A comparison is point-in-time evidence; it does not establish that the
  owner's edits are wrong or that every fact is current. Audit the document ID and comparison category, without
  storing the new source text.
- Show the latest source text for review. An explicit revision action preserves the document's audience and
  creates editable work only when no unfinished draft or unresolved receipt would be overwritten. The owner
  chooses which corrections to retain, saves a draft, explicitly revokes the earlier copy and separately approves
  its replacement. No source check rewrites, revokes or approves existing knowledge.
- Deleting an eligible library copy clears its source snapshot and protected receipt along with text and previous
  operation-response copies. Existing task snapshots, audit metadata and backups retain their documented lifecycle.
- Omit optional provenance fields when absent, preserving prior document digests and context wire shapes. Older
  imports remain ordinary approved text copies; reimport and review to record their source. Never infer proof by
  parsing an older editable source header.

## Limits

This implements explicit source comparison and owner-reviewed replacement, not automatic private crawling or
scheduled Drive refresh. Existing selected-file service-account onboarding remains; delegated OAuth/Picker is
separate work. Fictional checks establish local approval, provenance and recovery behavior, not live Google
acceptance, semantic relevance, customer usefulness or full company erasure.
