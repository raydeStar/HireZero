# Source freshness and owner review — 2026-10-25

Scope: another increment of the full three-phase context-first product goal. No product phase is claimed complete.
Decision: [ADR 0057](../adr/0057-source-freshness-and-owner-review.md).

## Delivered

- Owner-started refresh of one to three sources cited by the exact approved business brief, with a three-per-hour
  company limit and one running refresh. Existing guarded public-page integration; no model or search API.
- Immutable source checks, separate check/snapshot dates, unchanged snapshot reuse, changed-source comparison,
  partial results and restart recovery without replay. Approved profile text and knowledge remain intact.
- Exact owner field assessments: verified, possible change, conflicting or outdated. Verification requires a
  quote in a current checked snapshot. Critical assessments persist through later source reads.
- Compact freshness provenance in employee packets. Changed freshness refuses stale prepared work; critical
  owner assessments pause use of the brief. Late responses are withheld while reported usage still settles.
- Business brief controls for source selection, comparison, review and correction. Company/profile-scoped lost
  receipt recovery survives reload and preserves unsaved brief edits. Running status polls only saved records.
- Today identifies changed/unavailable/critical evidence and routes the owner to Business brief. Correcting an
  approved brief retains the existing explicit revoke-then-approve replacement sequence.

## Verification

317.7 GiB free before builds. Frontend production build passed; company test project build passed with zero
warnings/errors. Scoped Csharpier check passed for 15 files; changed frontend formatting and `git diff --check`
passed.

Backend focused run: **38 passed, zero failed/skipped** across `BusinessFreshnessTests`, `BusinessContextTests`,
`CompanyDocumentTests`, `CompanyTodayTests` and `BusinessLookupTests`. The six new facts cover exact owner and
tenant boundaries, one-use HTTP orchestration, check versus discovery dates, retained approved wording, stale
review rejection, persistent critical assessments, verification evidence, quota/running limits, restart and
partial reads, stale dispatch/late-result fencing, usage settlement, explicit replacement and migration 15 with
earlier checksums preserved.

Existing `CompanySuite.Security`, `IntegrationPath`, `MeasuredQueue` and `Conversation`: **4 passed**, zero
failed/skipped, 50.169 seconds. Total non-overlapping backend results for this slice: **42 passed**.

Browser journeys passed:

- `freshness-browser-smoke.mjs`: fictional original and changed pages through the real reader/routes/store;
  comparison identifies an absent old quote; lost response plus unavailable receipt survives reload; one refresh
  only; unsaved owner correction retained; outdated assessment pauses brief use; Today identifies the review;
  new approval requires explicit revocation; replacement clears the prior brief's pause; zero browser exceptions.
- Existing `business-brief-browser-smoke.mjs`: business confirmation, bounded research, exact approval, correction,
  recovery and replacement still pass.
- `company-ui.test.mjs` passes.

The freshness browser run at 390 × 844 had no horizontal overflow. Its screenshot was visually inspected:
readable warning, check status, comparison summary and retained quote. Comparison text stays scrollable inside
bounded panels. Initial browser testing found a misleading receipt-pending label while a request was still in
progress; that state now has separate copy, and a resumed running refresh polls status without repeating reads.

Compact local evidence in ignored `test-results/`: `freshness-core.log`, `freshness-existing-flows.log`,
`freshness-build.log`, `freshness-browser.json`, `freshness-mobile.png`. Fictional fixture only; no live model,
paid search, external website, publication, deployment, commit or push.

## Remaining

Checks are manually started. Recurring scheduling, semantic relevance filtering, real-site evaluation and
customer repeat-use evidence are still pending. A bounded text comparison cannot prove a whole site or fact is
current. Owner verification retains the original claim classification. Historical source/check/assessment rows,
receipts and task packets remain retained; permanent deletion is separate unfinished scope. The full delivery
ledger remains authoritative for all three phases.
