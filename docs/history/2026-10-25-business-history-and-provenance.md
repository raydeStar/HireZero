# Business brief history and employee source provenance

Scope remains all three phases in [the delivery ledger](../CONTEXT_FIRST_DELIVERY.md). This is a local implementation
receipt, not a completed product phase, hosted acceptance test or customer-usefulness claim.

## Implemented

- Saved brief drafts, model-generated drafts, owner edits, approval, rejection and revocation retain immutable
  company-scoped snapshots. Reviews do not masquerade as fact edits. Replayed operations do not duplicate history.
- A stable, paged history API and expandable UI show field values, verification status, supporting quotes and source
  retrieval dates. Earlier pre-feature records keep an honest unavailable-author label; missing historical edits are
  not reconstructed. The new migration preserves existing data and prior migration checksums.
- Captured employee context carries the selected approved brief's exact field provenance, source addresses,
  digests and dates. Unreferenced sources and full raw pages stay out. Existing context admission, digest review and
  knowledge-revocation fences cover the added packet. Empty provenance leaves old serialized packets unchanged.
- Text instructions distinguish owner corrections, source-supported claims, hypotheses and unknowns. Source support
  is not a claim that an external fact is still current. No authority comes from a quote.
- Mobile history wraps without horizontal overflow. Search consent has its checkbox beside its label; duplicate
  Unknown labels were removed. No new paid request, external write or model execution capability was added.

## Evidence

- Company test project build and frontend TypeScript/production build passed with zero build warnings/errors.
- Focused company run: 54 passed, zero failed; four PostgreSQL facts were skipped because no disposable server was
  configured. Coverage includes history, paging during concurrent new edits, earlier-record migration, audit integrity,
  company isolation, native/API fake models, drafting, lookup, settings and SQLite store behavior.
- Existing integration-path, measured-queue and conversation suites passed (three xUnit facts). The old integration
  migration fixture was updated to remove every later business table before simulating a version-one database.
- One additional backward-serialization fact passed after the main run, proving an older context packet keeps its
  exact JSON and digest. Total for these non-overlapping company runs: 58 passed, four skipped.
- Both browser flows passed through the real host/store/UI with fictional HTTP and model fixtures. The manual flow
  asserts six draft/review/correction records and cross-company history isolation. The generated flow asserts model,
  owner edit and approval history, source links and provenance in the exact first Marketing request packet.
- Mobile history and candidate results were inspected at 390 pixels. The final presentation was exercised again
  after adjusting consent layout and history wording. Company UI Node checks and scoped C# formatting checks passed.

Ignored evidence: `test-results/business-history-core.log`, `business-history-existing-flows.log`,
`business-brief-browser.json`, `business-drafting-browser.json`, `business-history-mobile.png`, and
`business-search-mobile.png`. Related decisions: [ADR 0055](../adr/0055-business-history-and-provenance.md).

## Remaining

Document ingestion and relevant retrieval, source freshness/change review, retention/deletion, recurring work,
billing, advanced execution and real customer validation remain in the product ledger. A very large cited brief
can exceed an existing job's context limit; the system refuses oversized packets instead of silently dropping
provenance. PostgreSQL migration execution remains unverified in this session.

All data and provider replies in checks were fictional. No live model/API request, production deployment, push,
publication, outreach or GPU inference occurred.
