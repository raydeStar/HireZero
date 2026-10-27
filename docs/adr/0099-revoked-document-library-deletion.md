# 0099 — Revoked document library deletion

Status: accepted. Date: 2026-10-26.

## Problem

ADR 0070 deliberately permits deletion only for never-approved drafts. An owner could revoke an approved
document but could not remove its source text or recover the library allowance. Retaining prior task evidence
does not require retaining the entire revoked library source forever.

## Decision

Add a separate owner-only `document.source.delete` command. It requires a currently revoked document, its exact
version and digest, and the distinct `delete-revoked-source` confirmation. Approved documents must first be
revoked; the original draft-deletion confirmation cannot authorize this operation. Automatic discarded-draft
retention remains unchanged and never deletes reviewed sources.

The transaction clears the document source text, title, filename, audience, replacement reference and retrieval
index. It replaces all document save/review operation-response copies with the same content-free tombstone.
Existing operation IDs and fingerprints remain, so recovery and replay return the tombstone without recreating
the document or repeating approval. The tombstone retains ID, original digest, version and deletion time, plus
`deletionScope: "revoked-source"`. Unapproved deletions record `"unapproved"`; old tombstones without this optional
payload field retain their existing behavior. No schema migration or package change is needed.

The deleted source is omitted from the active library and its storage allowance. Existing authority checks
continue to reject its excerpts for new dispatch or result use. Deletion does not restore revoked authority or
rewrite a saved task packet. Content-free audit evidence remains verifiable.

Separate revision drafts remain separate records. A revision may still reference a deleted, formerly reviewed
source, including when an owner saves an already-open revision after deletion. Such a draft requires its own
approval. This does not permit revising a deleted unapproved draft or resurrecting the original source.

## Owner experience and scope

After revocation, Documents offers **Delete library copy** with its own checkbox and exact command. The review
and success/recovery notice distinguish removal of the library source from retained copies. Lost responses
retain the existing operation ID across reload; recovery reads that receipt instead of sending another deletion.

Earlier task excerpts and results, other saved or unsaved drafts, imported originals, downloads, provider copies,
backups and database recovery files are outside this deletion. Open browsers may also retain previously read
content. The UI explicitly explains that this is not erasure everywhere and does not recall already sent data.
This extends library lifecycle controls under X6; full-company erasure, historical-copy deletion and backup
retirement remain separate acceptance work. No user data is removed merely by deploying this feature.

## Verification

Focused fictional tests cover exact authority, stale consent, cross-company refusal, immutable task evidence,
blocked dispatch, historical document-response erasure, replay after reopen, revision continuity, storage/index
removal and audit integrity. The Documents Chrome journey covers separate consent, a double click, a lost response,
reload recovery, retained task copies and desktop/mobile disclosure. See the
[dated history](../history/2026-10-26-revoked-document-library-deletion.md) for run results.
