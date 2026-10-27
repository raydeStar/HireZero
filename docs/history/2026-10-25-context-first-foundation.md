# Context-first foundation — 2026-10-25

The owner selected all three phases of the product proposal. The complete scope remains in
[the delivery ledger](../CONTEXT_FIRST_DELIVERY.md); this receipt covers only the first implemented foundation.

## Delivered locally

- A company-scoped business identity with optional website, goal, location and industry. Exact owner confirmation
  precedes research. Existing companies and the earlier UX work are preserved by an additive migration.
- Immutable public source snapshots with final URL, bounded text, original retrieval date and digest. A separate
  audited research tool confines reads to the confirmed website and enforces robots policy, pacing, redirect and
  size/deadline limits. Restricted sources are refused without creating evidence.
- A structured nine-field brief with owner, source-supported, hypothesis or unknown provenance. Citations must quote
  evidence belonging to this company and confirmed identity revision. Approval atomically creates shared knowledge.
- Explicit version/digest revocation before replacement or identity change. Saved corrections and old approvals are
  not silently rewritten; new source reads do not change approved facts.
- Home has one primary first action. The brief supports local draft recovery, saved-state inspection, failure
  retention, readable approved facts, desktop/mobile layouts and a handoff into Marketing hiring.

## Verification

- Company test project build: passed, zero warnings/errors.
- Integration test project build: passed, zero warnings/errors.
- `BusinessContextTests` and `CompanyTenancyTests`: 11 passed. Covers exact approval, source immutability, isolation,
  stale citations, owner corrections, duplicate receipts, role authorization, credential rejection and preservation
  of earlier migration checksums/data.
- `WebResearchTests` and `WebReaderTests`: 29 passed. Fake HTTP handlers cover crawl rules, denied/unavailable policy,
  origin escapes, bounded reads, public-address rules and redirects.
- Existing `ContextDraftTests`: 4 passed with fake providers.
- Frontend build and company UI Node check: passed.
- `business-brief-browser-smoke.mjs`: passed against the real host/store/reader with fictional HTTP responses and an
  isolated headless browser. Covers identity confirmation, blocked reads, citation rejection, save failures, reload,
  exact approval/revocation, company isolation, mobile layout and Marketing handoff.
- Existing `company-onboarding-browser-smoke.mjs`: passed after adopting the business-first Home entry. Existing
  manual hiring remains reachable through People.
- Desktop/mobile screenshots inspected. Fixed inherited header layout, duplicate Home actions and a buried next step.

Compact browser evidence is in ignored `test-results/business-brief-browser.json`, with desktop/mobile screenshots.
Test hosts and browser profiles are disposable and cleaned after execution. No deployment, push, live model call,
external website read, real-company mutation or GPU work was performed for this receipt.

## Remaining work

The brief currently starts from owner inputs and manually selected source quotes. Candidate search, automatic
profile extraction, relevant retrieval, document ingestion, first-deliverable setup, recurring workflows, billing
and advanced execution remain in the full three-phase ledger. Robots enforcement is not a universal terms-of-use
permission check. No customer usefulness, repeat-use, payment or retention claim is established by these fixtures.
