# Hosted HireZero on Vultr: build briefs · November 5, 2026

The decision is [ADR 0179](../adr/0179-hosted-hirezero-on-vultr.md). Each brief below is self-contained: paste one
into an agent as its prompt. Briefs A and B touch different code and can run at the same time, each in its own
branch or worktree. The deadline is Sun Nov 8, 3 PM MT; the owner wants it working Saturday.

## Rules for every brief

These apply on top of `AGENTS.md`:

- Branch off `main` (`vultr/provider` for A, `vultr/signup` for B). Commit there. Never push, merge or open a PR;
  the owner and Claude review first.
- Offline only: no live model calls, no Vultr or Google calls, no spending. Use fakes like the existing
  `ApiModelProvider` and hosting tests.
- Format (`dotnet csharpier format .`, `npm run format --prefix web`) and run the smallest checks that cover the
  change (see README). New tests are focused xUnit facts.
- Everything new is off by default. A local install with no new settings must behave exactly as before.
- Never weaken an existing guarantee: no retries of uncertain outcomes, no sending without a worst-case reservation,
  no tool calls returned, no fallback to another model.
- The repository is about to become public: fixtures are fictional, no paths under a user profile, no real data.

---

## A. The Vultr vendor and a Vultr-only install

**Why:** on the hosted install every model call must go to Vultr Serverless Inference.

**Read first:** `docs/ADDING_A_MODEL_PROVIDER.md`, ADRs 0010, 0036, 0042, 0127, 0169 and 0177,
`src/HireZero.Integrations.ModelApis/ApiModelProvider.cs`, `config/model-routes.json`, and where
`CompanyWorkspace` chooses providers (`src/HireZero.Host/CompanyWorkspace.cs`, `CompanySettings.cs`).

**Facts about Vultr** (verified on its public `/v1/models` and with live calls on Nov 5):

- Base URL `https://api.vultrinference.com/v1`, path `chat/completions`, `Authorization: Bearer <key>`.
- Model id `deepseek-v4.1-flash-1017`. Price $0.15 per million input tokens, $0.60 per million output.
- OpenAI-compatible request: `model`, `messages`, `max_tokens`, and `reasoning_effort` (one of minimal, low,
  medium, high, xhigh, ultra; there is no "none", and reasoning cannot be turned off). Use `low`.
- The reply carries `usage.prompt_tokens`, `usage.completion_tokens` and
  `usage.completion_tokens_details.reasoning_tokens`, plus an `id`.
- At low effort this model used 8,000–15,000 output tokens on a weekly marketing plan, so deliverables need an output
  limit of at least 24,000 and a timeout of at least 5 minutes.

**Do:**

1. Add `vultr` as a vendor in `ApiModelProvider`: route `vultr/deepseek-v4.1-flash-1017`, the key from the secret
   `VULTR_INFERENCE_API_KEY` through the existing secret source, and an optional `HIREZERO_VULTR_BASE_URL` (https only;
   http only on loopback for tests). Serve both `CompleteAsync` and `CompleteMeasuredAsync` with the same checks as
   the other vendors: price required, worst case within the allowance, no redirects, no retry, tool calls and
   truncated or usage-less replies withheld, cancellation honoured.
2. Add the route's price and display name to `config/model-routes.json`.
3. Add a setting, `HIREZERO_MODEL_VENDOR=vultr` (document it in `docs/SETTINGS.md`). When set:
   - every model purpose (text, analysis, decisions, deliverables, schedules, memory checks, business drafts) routes to
     the Vultr route;
   - Codex, OpenRouter (including its tool provider and Jev), Anthropic and OpenAI are refused at construction, with
     a message naming the setting;
   - roles or purposes that need another runtime (coding runs, the Developer role) report themselves unavailable
     rather than failing later.
4. Tests with a fake HTTP handler: the request shape, usage settlement, a refused 4xx with no usage, an uncertain
   outcome held and not retried, a tool-call reply withheld, the worst-case refusal, and the Vultr-only setting
   refusing every other provider.

**Done:** the tests pass, `docs/SETTINGS.md` and the provider guide mention Vultr, and a local install without the
new settings is unchanged.

## B. Open sign-up with spending caps

**Why:** customers sign in with Google and get their own HireZero company, paid for by the owner's Vultr credits.

**Read first:** ADRs 0020, 0021, 0036, 0058, 0081, 0082 and 0177, `src/HireZero.Host/CompanyHosting.cs` (the OIDC
`OnTokenValidated` sign-in), `CompanyMembership`, the company directory (`co_company`) and how budgets and
reservations are settled for API models.

**Do:**

1. Behind `HIREZERO_OPEN_SIGNUP=1` (hosted mode only; refused in local mode), a signed-in user with a verified email
   and no membership is offered "Create my company". It creates one company with that user as owner. At most one
   company per sign-in subject, and at most `HIREZERO_SIGNUP_MAX_COMPANIES` (default 200) companies created this way.
   Without the setting, behaviour is exactly as today: no membership, no access.
2. A lifetime model-spend cap per signed-up company, `HIREZERO_SIGNUP_COMPANY_CAP_CENTS` (default 50), and a global
   daily cap across the install, `HIREZERO_DAILY_CAP_CENTS` (default 500). Both are checked against the worst-case
   reservation before a request is sent, and settled with the real usage afterwards. An uncertain outcome keeps its
   reservation. The refusal tells the owner plainly that the free allowance is used up.
3. The UI shows the remaining allowance in dollars, using the existing allowance component (ADR 0177).
4. Signed-up companies can hire the roles that run on the configured model (Marketing first). Data-root integrations
   are not offered to them.
5. Tests: sign-up off by default; one company per subject; the install-wide maximum; an unverified email refused;
   both caps refusing before sending; settlement after success; an uncertain outcome holding its reservation;
   companies cannot see each other.

**Done:** the tests pass, and the settings are documented in `docs/SETTINGS.md`.

## C. Deployment (Claude, on the VM)

Not for Codex. After A and B are reviewed and merged, Claude publishes hosted HireZero on the existing Vultr VM, under
its own subdomain and with a fresh data root, behind nginx and TLS. It also qualifies the browser runtime and runs one
real marketing job end to end. That last step needs the owner's Google OAuth client, the DNS record and the go-ahead
before the port opens.
