# Vultr provider: Brief A verification

Implements Brief A of [the hosted briefs](2026-11-05-vultr-hosted-briefs.md) and [ADR 0179](../adr/0179-hosted-hirezero-on-vultr.md).
Open sign-up, install spending caps and deployment remain separate briefs.

- Added Vultr HTTP inference, the reviewed price/name, secret lookup and optional validated base URL.
- Added an opt-in Vultr-only boundary across model selection, company connections, decisions and optional transports.
  Unsupported coding roles and integrations report unavailable before new work is admitted.
- New Vultr text policies seal 24,000 output tokens, 60,000 planning tokens and a 360-second deadline before approval.
  The ordinary `CompleteAsync` entry point keeps the API providers' existing refusal: it carries no approved allowance.
- Token-derived decision costs remain estimates in the usage ledger. They are not labeled provider-reported bills.

Offline verification on November 5, 2026:

- Company test executable: **186 passed, zero failed/skipped**, selecting `Vultr*`, `ApiModelProviderTests`,
  `CompanyModelConnectionTests`, `CompanyMixedModelTests`, `CompanyDecision*`, `MeasuredEffortTests`,
  `CompanySettingsTests`, `CompanyIntegrationIsolationTests`, `CompanyScheduleTests`, `CompanyGoalStartTests`,
  `CompanyMemoryAnalysisTests`, and `BusinessDraftTests`.
- `dotnet csharpier format .` / `check .`; `npm run format --prefix web` / `format:check`; `git diff --check`.
- `npm run typecheck --prefix web`, `npm run build --prefix web`, and `node tests/decisions-browser-smoke.mjs` passed.
  The web build retains its existing advisory about large chunks.
- All model responses were fictional HTTP fixtures; HTTP host/browser checks used loopback. No live model,
  Vultr, Google, deployment or spending operation was performed. Live provider qualification remains separate.

The butler's receipt proves the local checks, not a production launch.
