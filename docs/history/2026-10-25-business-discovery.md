# Business search and bounded website discovery

Scope remains all three phases in [the delivery ledger](../CONTEXT_FIRST_DELIVERY.md). This receipt advances
context-first onboarding, not completion of a product phase or a live deployment.

## Implemented

- Owner-authorized candidate search with exact query consumption, company isolation, per-hour limits, no retries,
  durable receipts and withholding after identity changes or owner revocation. Only name, location and industry
  go to the search service. Choosing a candidate fills the form; business confirmation remains separate.
- The official Brave API adapter is behind a neutral Spend tool. Credentials use the existing server secret source;
  missing configuration explains the manual-website fallback. No live credential or request was used in checks.
- Discovery reads the confirmed page and at most two relevant same-origin links. Robots, public-address,
  redirect, pacing, size and time checks remain in force. Explicit nofollow and known account/action/download links
  are excluded. Partial failures preserve good sources and explain limitations.
- Source snapshots remain immutable evidence. Discovery does not approve company knowledge. The manual page
  reader remains available. Restart and uncertain outcomes never trigger an automatic replay.

## Evidence

- Company and integration test builds passed with zero warnings/errors; frontend TypeScript and production build
  passed. Free disk remained above 317 GiB.
- 22 focused company facts passed (lookup, context, settings and tenancy). An initial sandbox-only tenancy failure
  was caused by denied loopback/key-file operations; the complete run passed with its disposable host permitted.
- 35 integration facts passed (candidate adapter, bounded discovery, web research and reader).
- The expanded browser journey passed: no-URL identity, search, lost-response receipt recovery, explicit candidate
  selection and confirmation, three-page discovery, profile drafting, owner review and first Marketing plan.
- Existing manual business-brief browser regression passed. Candidate results fit a 390-pixel mobile viewport.
  A later provenance pass also corrected the consent checkbox layout and repeated both browser journeys.

Ignored receipts: `test-results/business-discovery-company.log`, `business-drafting-browser.json`,
`business-brief-browser.json`, `business-search-mobile.png`. See [ADR 0054](../adr/0054-bounded-business-discovery.md).
All websites, search results and models in verification were fictional fixtures. No deployment, push, publication,
outreach, live model request, paid search request or GPU inference occurred.

Real matching quality, operator search setup, full provider cost accounting, broader source eligibility, customer
usefulness and the remaining product phases remain open. This is not a universal source-terms checker or a grant
to scrape restricted accounts.
