# Brief B: hosted sign-up and free model allowances — 2026-11-05

Implemented on `vultr/signup`, based on the supplied `main` checkout. No push or deployment.
Scope: Brief B only; provider/vendor implementation remains Brief A.

- Verified OIDC users without membership can explicitly create one owned company when open sign-up is enabled.
  Enrollment is atomic, subject-unique and limited across the install; ordinary company creation cannot bypass it.
- Lifetime company and shared UTC-day model limits reserve worst-case dollars before transport, settle confirmed
  usage and retain uncertain reservations across restart. The policy survives disabling enrollment.
- The existing allowance module shows the available dollars. Marketing can be hired; workspace/coding roles and
  data-root connections are unavailable to signed-up companies. Capped installs use their configured host API model.
- Settings and behavior are recorded in [SETTINGS](../SETTINGS.md#hosted-open-sign-up) and
  [ADR 0181](../adr/0181-open-signup-and-durable-spend-caps.md).

## Offline verification

Dependencies restored from local caches; no live model, Google or Vultr requests, spending or CI runs.
The identity-provider and HTTP fixtures are fictional and run in process/loopback.

- `dotnet build tests/HireZero.Company.Tests --no-restore -m:1 --nologo -v:q`: passed, no warnings/errors.
- `dotnet test --project tests/HireZero.Company.Tests --no-build --filter-class CompanySignupTests CompanySignupSpendTests HostedSignInTests CompanyCreationTests CompanySettingsTests ApiModelProviderTests CompanyModelConnectionTests CompanyModelConnectionHostingTests CompanyIntegrationIsolationTests StoreDatabaseTests`:
  **74 passed, 5 skipped, 0 failed**. The five PostgreSQL checks require a separately configured disposable server;
  none was configured. SQLite admission, concurrency, persistence, HTTP tenancy and fake OIDC checks passed.
- `npm run typecheck --prefix web`: passed.
- `node --test tests/measured-ui.test.mjs`: passed.
- Required formatters applied; `dotnet csharpier check .`, `npm run format:check --prefix web` and
  `git diff --check`: passed.

These checks do not establish live Google/Vultr, PostgreSQL, deployment or browser visual acceptance. When combining
Briefs A and B, retain the money gate around every configured API transport and verify that its conservative output
bound covers any changed provider output target. No uncertain request is automatically replayed.
