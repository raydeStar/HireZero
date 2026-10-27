# 0070 — Unapproved document erasure and retention

Status: accepted. Date: 2026-10-25.

## Problem

Discarding a private document previously kept its text indefinitely and consumed its storage allowance.
Each successful save and review also retained a full document in `co_operations`. Deleting only the latest
document row would leave older text readable through operation recovery; dropping the operation IDs would
allow an old save to execute again.

## Decision

An owner may explicitly delete an exact version and digest of a **never-approved** document in `draft` or
`discarded` state. `document.delete` also requires `delete-unapproved` confirmation. Previously approved
and revoked documents are refused: their text may already exist in task packets, outputs or external systems,
and this control does not claim to erase those copies.

Deletion atomically replaces the document with a content-free tombstone, clears its retrieval index and replaces
all matching document operation results, including earlier edited versions, with that same tombstone. The
title, text, filename, audience and replacement reference are removed. IDs, original content/operation hashes,
version, deletion time and content-free audit events remain. Existing operation IDs and fingerprints remain;
reconciliation returns the tombstone and never repeats an earlier save. Tombstones cannot be approved or edited,
are omitted from the active library, and do not consume the 50-document or 2 MiB content allowance.

Schema migration 22 adds a company-scoped discarded-draft retention policy. Its default is **off**, preserving
existing data. An owner can select 7, 30 or 90 days from the discard timestamp, or turn automatic deletion off.
Saving the policy requires explicit confirmation and immediately applies it to already expired discarded drafts.
The scheduler checks the same policy once per minute while running, including on restart. Ordinary drafts,
approved documents and revoked documents are not eligible. The transaction serializes policy changes, erasure,
approval and saves for the company. Disabling retention cannot restore deleted content.

The Documents UI distinguishes discard, revoke and delete; shows the deletion scope and retention consequences;
requires explicit confirmation; and preserves a recovery request ID through a lost response and page reload.
The current editor is cleared when its deleted document is reconciled. A recovered old policy receipt refreshes
the current policy rather than presenting a superseded value as current.

## Boundaries

This is logical deletion from the active application document store and saved operation results, not forensic
media erasure or complete company deletion. SQL recovery files, existing operator backups, imported originals,
other open browsers and separately copied material are outside this control. Restoring an older backup can
restore deleted text; the operator must manage backup expiry and restored data before reopening access. The
existing backup command's retained-copy count is separate from this per-company policy. No automatic filesystem
deletion, backup pruning, credential revocation or external deletion is performed here.

Deletion of previously approved material, historical task copies, complete companies and their connection files
remains separate work under X6 of the product delivery ledger.

## Verification

Focused tests cover edited historical receipt erasure, stale save and approval refusal, exact consent, owner and
tenant boundaries, expiry at the exact cutoff, disable/re-enable behavior, replay of a superseded policy, storage
allowance recovery, migration checksums and preservation of approved task packets. The browser journey covers
lost deletion and policy replies, reload recovery, removal of the current editor copy, durable settings and mobile layout.
See the dated implementation receipt for actual results.
