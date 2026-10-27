# 0121: Delete revoked or replaced saved memory

Status: implemented; verification is recorded separately. Extends ADRs 0060 and 0103; supports P3.5 and X6.

Never-approved drafts already support deletion, but revoked and replaced records retained their full contents
indefinitely. Add a separate owner-only `memory.source.delete` command for a previously approved record whose
current state is `revoked` or `superseded`. Require its exact version, digest and `delete-retired-source` consent.
Active or merely overdue memory must first be explicitly revoked or replaced. The draft-delete command keeps
its original never-approved boundary.

In one company transaction, remove the saved definition (including audience, dates, quotes and revision source),
artifact/meeting citation, replacement reference and keyword index. Replace earlier operation-result copies of
that record with the same content-free tombstone. Retain IDs, operation fingerprints, original digest, approval
date and deletion metadata to prevent old requests from restoring content. Record a content-free deletion audit.
The tombstone is excluded from listings, retrieval, analysis and retained-memory capacity, and cannot be approved
or edited back into a record. Its optional deletion scope preserves older record wire shapes when absent.

This erases the current memory record and its saved command responses. Earlier task/context packets, analysis
reports, cited results/minutes, other drafts, audit/event titles, other browser copies, exports, database recovery
files and backups have separate lifecycles. Preserve them and state that boundary before confirmation. Do not
claim company-wide erasure or silently rewrite historical evidence. Existing revocation/version checks continue
to prevent dispatch from stale prepared context.

An already approved replacement remains unchanged. A separate unfinished replacement draft remains saved, but
its earlier version binding is stale after deletion and cannot be approved as-is. Explicitly saving/reviewing a
manual revision may bind the retired-source tombstone. Its approval must never rewrite that tombstone into a
superseded record. Never-approved deleted drafts cannot serve as replacement sources. Model-suggested revisions
retain their independent exact-report/source validity checks.

Show the control only for eligible records and owners, inside a separate disclosure with an unchecked consent
box. Preserve unrelated unsaved drafts. Use existing durable command receipts for lost replies and reload
recovery, without sending another deletion. Clear matching local edits and outdated previews after confirmed
deletion. A revoked record no longer offers the already-invalid revoke action.

No automatic expiry or new runtime/provider action is introduced. Full company erasure and production privacy
acceptance remain open; fictional deletion tests do not establish remote deletion or backup retirement.
