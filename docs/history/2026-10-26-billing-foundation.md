# Billing adapter, durable checkout and purchased-allowance verification

Implemented [ADR 0081](../adr/0081-stripe-billing-and-purchased-allowances.md) using fictional transport and
company data. Stripe is the first provisional adapter; no launch price or merchant account was configured.

Verified locally:

- 14 focused billing facts plus 9 monthly-usage regressions: **23 passed**. Exact owner approval, concurrent
  approvals/dispatches, removed-owner authority, restart/no-configuration behavior, changed catalog, unknown
  checkout recovery, customer/receipt isolation, payment failure states, refresh races, cancellation, invoice
  binding, purchased token/job limits, carried reservations and shared script/browser compute admission.
- **12 migration/legacy facts passed**, including migration 25 and unchanged earlier checksum/data checks.
  This overlaps the billing/usage group; it is not a count of 12 additional unique tests.
- **57 integration facts passed**: 13 Stripe adapter/webhook/composed-workflow checks and 44 existing
  browser/script lifecycle checks. The composed transport test loses a fictional POST reply, reopens the
  company database and verifies the original subscription through canonical GETs with exactly one POST.
- Both affected test projects build with zero warnings and errors. Formatting checks passed for 30 scoped
  C# files, and `git diff --check` passed. No frontend changes were made for billing in this slice.

The checks exposed a recovery race: starting a refresh before the creation reply arrived could discard the
only checkout identifier. The ledger now accepts that late original receipt while rejecting an older refresh
and any creation reply that would overwrite newer canonical evidence. A focused regression covers both sides.

Logs are `billing-ledger-build.log`, `billing-ledger-tests.log`, `billing-ledger-migrations.log`,
`billing-adapter-build.log` and `billing-adapter-and-compute-tests.log` under ignored `test-results/`.
Fictional payment fixtures create no external requests; temporary billing databases are removed after use.
Free disk space was above 315 GiB before building. Existing dirty work and the unrelated running application
were preserved.

This is backend composition proof, not a customer checkout E2E acceptance. Host registration, owner routes,
webhook durability/deduplication and reconciliation scheduling, portal flow, Billing UI, hosted test-mode
verification, launch terms and actual paid conversion remain outstanding. No live Stripe, model, GPU,
container, publishing or deployment action occurred.
