# Owner-reviewed memory consistency reports — 2026-10-26

Implemented the selected-record maintenance slice of P3.5 under
[ADR 0091](../adr/0091-owner-reviewed-memory-consistency.md). The three-phase delivery remains incomplete;
this receipt covers local synthetic behavior, not real model quality or production acceptance.

## Delivered

- Owners select two to eight previously approved memory records, including overdue reviews with current source
  lineage. Preparation exposes the exact context, instructions, route and bounded allowance without a model call.
- One explicit authorization consumes a measured request. Reports contain only bounded hypotheses with exact
  quotations from selected statements. Possible conflicts and duplicates cite two distinct records. Invalid
  output cannot become a report, and a report never changes approved memory.
- Sources, audiences, owner authority and model identity are checked during execution and before result import.
  Completed reports preserve the original source revisions and visibly become historical when sources change.
  Receipt reconciliation retains observed costs and requires confirmation that the provider stopped.
- The existing shared provider gate, company/monthly/purchased allowance and company API funding accounting
  include memory checks. Interrupted and uncertain requests retain reservations and never replay after reload
  or restart. Trustworthy late receipts settle cost without importing content.
- Report reads and saved operation receipts require current owner access and stay within their company.
  HTTP delivery rechecks the owner's role after the provider returns. Audit entries omit source/report text.
- The Memory UI preserves unfinished edits when preparing checks or opening citations. Lost replies are recovered
  through saved receipts. Mobile report controls stack at full width; detailed token receipts are expandable.
- Additive schema migration 27 preserves earlier migration checksums. Downgrade fixtures remove the new table
  before replaying their selected migration sequence.

## Verification

- **45 focused backend facts passed:** 13 memory-analysis facts, 10 existing memory facts, 13 business-draft facts,
  and 9 usage facts. They cover exact citation validation (including twelve malformed/unsupported output variants),
  selection/role/company boundaries, overdue records, expiry, stale source output, unknown/restarted/canceled
  requests, no replay, usage reconciliation, overrun preservation, monthly admission, native synthetic adapter
  scope, current-owner HTTP delivery and protected operation receipts. Existing migration checks passed.
- **Two Chrome journeys passed:** new memory-report preparation, explicit authorization, lost-response recovery,
  exact citations, preserved drafts, unknown-outcome accounting and stale reports; plus the existing memory
  approval/revision/feedback workflow. Desktop and 390-pixel mobile screenshots were inspected. The mobile
  selector was widened after visual review; the final journey asserts readability and no horizontal overflow.
- Frontend typecheck/production build, company UI checks, and Core/Codex/Host/company-test/fake-runtime builds
  passed. C# and frontend formatting and diff checks completed. Vite still reports the existing large main chunk;
  this change does not claim a bundle-performance improvement.
- The first native fixture failure used a fixed test clock with an authorization already expired against the
  adapter's real clock. The native test now uses current time; expiry protection remains intact. A browser wait
  initially expected the older memory form's error wording; the new recovery message and assertion now agree.
- Fake providers only. No live model execution, customer payment, runtime upgrade, deployment, commit or push.
  Browser processes exited before their disposable profiles/data were removed. More than 350 GiB remained free.

Compact local evidence is under ignored `test-results/memory-analysis-*`, including the backend log, browser
receipt, build/UI logs and desktop/mobile screenshots. Existing memory regression evidence remains under
`test-results/memory-browser.json` and `test-results/memory-analysis-regression.log`.

## Still open

Real model agreement/false-positive evaluation, customer usefulness, semantic retrieval, model-authored
replacement proposals and automatic maintenance are unverified or unimplemented. This report is an owner review
aid, not a declaration that company knowledge is correct. Commercial prices, hosted payment acceptance, production
execution qualification and broader three-phase acceptance requirements remain tracked in
[the delivery ledger](../CONTEXT_FIRST_DELIVERY.md).
