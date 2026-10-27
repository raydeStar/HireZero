# Company retirement review — 2026-10-26

Added the read-only owner review specified by [ADR 0105](../adr/0105-company-retirement-review.md).
The Companies screen can now inventory the selected company's records and identify unresolved actions without
exposing record contents. Core rechecks current owner membership inside the transaction; the endpoint disables
caching. Checks use actual lifecycle states and preserve independent resource/usage concerns after task completion.
Explicitly assigned unresolved merchant events are counted separately from company tables. Unassigned global
events and external resources are outside the review.

The first mobile inspection revealed an unnecessarily dense limitations list. The final UI uses shorter wording,
paragraph spacing and an expandable explanation. It keeps the absence of whole-company deletion visible.
Refresh clears older results, duplicate clicks produce one request, failures allow retry, and late responses
cannot populate a remounted screen. No mutation or provider call is performed by the review.

## Verification

- Six focused xUnit facts passed, with no skips: current-owner enforcement after demotion; all company-table
  inventory coverage; tenant isolation; no returned private contents or record mutation; actual terminal/open
  states and future unknown attempt state; completed work with missing cleanup/held resources; explicitly
  assigned versus unrelated/unassigned merchant events; and the real local HTTP endpoint's selected-company,
  `no-store` and post-demotion 403 behavior. Several related assertions share each fact.
- The existing settings-recovery Chrome journey passed with added desktop (1440px) and mobile (390px) checks
  for loading, duplicate-click prevention, empty-result caveat, expandable backup boundaries, failure/retry,
  no stale inventory after failure, navigation during an outstanding read and no horizontal overflow.
  Existing member, budget, rename and create-company recovery checks in that journey still pass.
- The shared company UI recovery contract test passed.
- TypeScript/Vite and Host/test builds passed. The .NET build reported zero warnings/errors; Vite retains its
  existing main-chunk size advisory. C# formatting, targeted Prettier checks and diff whitespace checks passed.
- The final mobile screenshot was inspected. Disposable Chrome/host processes exited before their verified
  temporary fixture root was removed. No real company, payment, model or external runtime was used.

Compact ignored evidence: `test-results/retirement-tests.log`, `retirement-build-final.log`,
`retirement-web-build.log`, `retirement-browser.log`, `retirement-ui-contracts.log`, and `retirement-mobile.png`.
An initial compilation failed because a target-typed return did not infer the generic transaction result;
the explicit result type fixed it before the passing checks above.

## Remaining scope

This is preparation for X6, not erasure or a company closure mechanism. Company-wide admission shutdown,
safe completion/reconciliation, bootstrap suppression, scoped credential/integration cleanup and historical,
backup and remote-copy disposition remain unfinished. The global billing inbox is not atomically frozen by
the company transaction on PostgreSQL. No live PostgreSQL, provider cancellation, runtime qualification,
production deployment, actual customer retention or paid conversion is claimed by these fictional checks.
