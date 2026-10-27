# Private text documents and task-specific retrieval

This advances the full three-phase [delivery ledger](../CONTEXT_FIRST_DELIVERY.md). It does not claim a completed
product phase, hosted deployment, semantic search or customer validation.

## Implemented

- Company-scoped document storage and a portable chunk/term index. Owners can paste or import UTF-8 text/Markdown,
  save private drafts, review exact text and employee audience, approve, revoke, discard or create revised drafts.
- Immutable reviewed records, explicit revocation before replacement, bounded text/storage allowances, credential
  and injection screening, and compound company foreign keys. Index creation/removal and approval are atomic.
- Keyword retrieval over approved documents, capped at three excerpts and two per document, with exact source
  versions/digests/offsets. Company conversations exclude department and individual employee documents.
- Relevant excerpts reach captured employee task packets and exact measured approval. Existing context budgets
  remain unchanged; automatically retrieved excerpts can be omitted with a visible notice if they do not fit.
  Revocation fences prepared dispatch and withholds late output while settling observed usage.
- An authenticated larger upload envelope is limited to document saves; unrelated routes retain their existing
  body limit. No file executes, follows links or calls a model during ingestion or preview.
- A library-first responsive UI, explicit Add document action, exact review checkbox, byte counts, scope labels,
  no-model retrieval preview, draft preservation after malformed imports, and durable lost-response recovery.

## Verification

- Frontend TypeScript/production build and company test project build passed with zero build warnings/errors.
  Disk space remained above 317 GiB.
- Focused company run: 38 passed, zero failed, four skipped. It covers eight document facts plus business context,
  profile drafting, SQLite store behavior and tenancy. Tests include exact approval/replay, scope isolation,
  immutable replacements, relevance and excerpt bounds, storage caps, revocation before/after dispatch, migration,
  backward packet compatibility and packing within the existing measured allowance. Four PostgreSQL checks were
  skipped because no disposable server was configured.
- Existing Security, IntegrationPath, MeasuredQueue and Conversation suites all passed (four xUnit facts).
  Combined non-overlapping company tests for the final run: 42 passed, four skipped.
- `documents-browser-smoke.mjs` passed through the actual host/store/UI: importing more than 16 KiB, saving and
  approving, previewing scoped excerpts, checking a real captured task packet, refusing dispatch after revocation,
  cross-company reads, invalid UTF-8 preserving the draft, lost replies, and a pending receipt surviving reload.
  It also proves ordinary routes still reject oversized bodies and the upload route rejects other commands.
- Existing search-to-first-Marketing-plan browser flow passed. Company UI Node recovery checks, scoped format
  checks and `git diff --check` passed. The final document library was inspected at 390 pixels with no overflow.

An initial new packing test omitted the required task version; correcting its setup made the final suite pass.
This did not require weakening the version or context-budget checks.

Ignored receipts: `test-results/documents-core.log`, `documents-existing-flows.log`, `documents-browser.json`,
`documents-mobile.png` and `business-drafting-browser.json`. Decision: [ADR 0056](../adr/0056-private-documents-and-retrieval.md).

## Open work

PDF/Word conversion and connected private-service ingestion are not implemented. Retrieval is keyword-based and
can miss paraphrases; it does not yet search decisions or prior work. Discard/revoke retain records and historical
packets; full retention/deletion remains separate work. PostgreSQL execution of migration 14 remains unverified
in this session. Source freshness, recurring jobs, notifications, billing, advanced execution and real customer
acceptance remain in the full ledger.

Verification used fictional data and fake providers only. No live model requests, paid API calls, publication,
outreach, deployment, push or GPU inference occurred.
