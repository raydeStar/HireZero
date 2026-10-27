# Company creation recovery — 2026-10-26

Implemented [ADR 0107](../adr/0107-atomic-company-creation-and-recovery.md) after the settings audit found that
invalid names left initialized companies behind and a lost creation response could invite a duplicate submission.

The server validates first and commits the new company, owner, destination audit and receipt atomically.
Actor-bound UUIDs make concurrent exact requests idempotent without exposing the new company through the source
company's operation/audit history. Recovery checks current membership and returns the current name; it never
renames a company or restores a revoked member. Legacy requests without an ID retain their one-request behavior.

The UI saves a tab-scoped request ID and name before dispatch. Unknown outcomes offer **Check saved company**, including
after reload. Recovery only reads a saved result. It suppresses the unused name field while recovery is pending
and refuses to dispatch if it cannot persist the request ID.
If the server explicitly reports a missing receipt, **Finish this creation** requires a user click and reuses
that exact saved identity and name. This handles a connection failure before dispatch without leaving creation
permanently stuck. A generic 404 from an older server does not enable a repeat, and revoked access is reported
separately from a missing receipt.

## Verification

- Four new focused xUnit facts passed: invalid names/UUIDs and injected mid-transaction rollback; six concurrent
  exact submissions producing one audited company; persisted recovery and changed-input refusal; current owner,
  actor isolation and destination revocation checks. SQLite fixtures only.
- Extended HTTP tenancy checks cover refused-name cleanup, exact repeated POSTs, no-store GET recovery and
  changed-input conflict. Together with the existing retirement review checks: **12 passed, 0 failed, 0 skipped**.
- Six new client-state checks passed: lost response/503 reconciliation, reload and source-company isolation,
  pre-dispatch storage refusal, and failed removal of an already confirmed receipt. Existing shared company UI
  contracts also passed. Additional cases cover explicit same-request completion after reload and refusal to
  offer a repeat for an older server's ordinary 404.
- The installed headless Chrome journey passed at desktop 1440px and mobile 390px against a disposable fictional
  host. It forces a saved POST to lose its response, blocks recovery reads, double-submits checks, reloads the
  page, then recovers the same company. Exactly one creation POST and one added company are observed per attempt.
  Existing settings-input, work-pause and retirement-review assertions also pass. The journey was rerun after
  hiding the inactive recovery name field; the final mobile screenshot was inspected for fit and clarity.
  A further desktop/mobile case drops the request before it reaches the server, reloads, confirms that a read
  creates nothing, then explicitly finishes using the same saved UUID and name. Double-clicking that action
  still adds only one company. The final backend, client-state and browser checks all pass after this addition.
- TypeScript/Vite and the .NET host/test build pass. .NET reports zero warnings/errors. Vite retains the existing
  main-bundle size advisory. CSharpier checks five changed C# files; targeted Prettier, Node syntax and diff
  whitespace checks pass.

The first build exposed an incorrect disposal call in the new test fixture; it was corrected to the existing
asynchronous fixture lifecycle before tests ran. A transient file-write failure during the small UI follow-up
left the source intact; the same bounded patch then succeeded. No runtime or host setting was changed to bypass it.

Compact logs/screenshots remain under ignored `test-results/company-creation-*`; the settings journey receipt is
`test-results/settings-recovery-browser.json`. Both owned browser/host processes exited and the fixture removed
its own temporary directory. No live model, payment, provider, production deployment or PostgreSQL acceptance was
performed. Docker was neither reconfigured nor stopped. Approximately 349 GiB was free before builds.
