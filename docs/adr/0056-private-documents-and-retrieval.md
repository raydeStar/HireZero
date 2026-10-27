# 0056: Approved private documents and bounded retrieval

Status: Accepted. Extends 0051 and 0055; uses the existing company command, membership and exact context contracts.

## Decision

Company documents are persistent local/platform records independent of live employee compute. The initial importer
accepts pasted text and UTF-8 `.txt` or `.md` files. Each document is at most 64 KiB; each company has a fixed
allowance of 50 records and 2 MiB of text, including draft, discarded and revoked records. Title, text, source
filename, employee audience and replacement identity all participate in the approval digest. Filenames cannot
contain paths. ContentGuard rejects credential-like, hidden and instruction-injection content before storage.

Saving creates an unapproved draft. Only an owner can save or approve documents. Approval names the exact version
and digest. An approved record is immutable; a replacement is a new draft naming the earlier document, whose
approval must be explicitly revoked before the replacement can be approved. Discarding a draft or revoking a
document removes it from retrieval, not from history, prior task packets, operation receipts or backups. Permanent
retention/deletion is separate work and must not be implied by these controls.

The employee audience is all employees, one department or one employee. This controls model/task context, not
within-company human viewing: current company members can inspect the library under the existing membership
boundary. Every document, index and retrieval query is company-scoped. Cross-company audience IDs are refused.
Company conversations and meetings receive only company-wide documents; employee work can also receive that
employee's department and individual documents. Revoked or retired employee work gains no execution authority.

## Index and context

Migration 14 adds documents, chunks and a portable term index with compound company foreign keys. Only approval
creates index rows; revocation clears them atomically. Text is split into excerpts of at most 800 characters with
bounded overlap, preserving offsets and digests. Unicode keyword matching uses up to 24 normalized non-stop terms,
with title matches weighted more strongly. It examines a bounded candidate set and returns up to three excerpts,
at most two per document. This is deterministic lexical retrieval, not semantic search; paraphrases can be missed.

Task capture retrieves from the task input without a model request. The packet records the normalized query,
method, exact document identity/version/digest, approval date, source filename and excerpt offset/text/digest.
Unrelated documents and full bodies are excluded. The existing byte/token admission and owner review cover this
packet. Measured preparation packs optional conversation history, then omits lower-ranked automatically retrieved
excerpts if necessary, with an explicit notice in the final packet. Explicit facts, notes and selected artifact
evidence are never silently dropped. Limits and spending authority are not raised.

Dispatch, continuation and result admission recheck each included document's approval, version, digest, audience
and exact excerpt. Revocation invalidates a prepared packet; an in-flight late response is withheld while actual
usage still settles. The captured historical packet remains unchanged. Older packets omit the absent optional
property so their JSON and digests stay stable. Employee instructions treat documents as untrusted references,
prefer explicit owner corrections when sources conflict, and distinguish an approval date from current factual
verification.

## HTTP and browser

The dedicated authenticated document-save route has a 512 KiB request envelope to accommodate JSON encoding of a
64 KiB text document. It only accepts `document.save`; ordinary routes retain their existing 16 KiB body limit.
Store validation remains authoritative. The read-only retrieval preview accepts an active viewer with the normal
origin/CSRF checks. It does not call a model, change documents or create task authority.

The UI retains company-scoped unfinished text, displays a byte count, validates UTF-8, preserves drafts after bad
imports, and uses durable operation receipts for lost replies. Unknown mutations can be checked after reload
without repeating them. Saved documents open directly for exact text/audience review. Markdown, HTML and code
are displayed as text and never executed.

## Remaining scope

PDF/Word extraction, private service connectors, semantic retrieval, retrieval over decisions and completed work,
source freshness, tenant credential management, subscriptions and retention/deletion remain in the full product
ledger. No live provider calls or new runtime dependencies are needed for this implementation.
