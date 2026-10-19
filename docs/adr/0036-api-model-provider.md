# 0036: API model provider

Status: Accepted.

## Context

The only real model connection was the pinned Codex CLI ([ADR 0014](0014-pinned-codex-runtime.md)), signed in on the
owner's own machine. Hosted mode and the container image ([ADR 0026](0026-container-and-releases.md)) therefore had no
model: employees there could not think. Codex is also deliberately unqualified for automatic work. Every real call,
conversation replies included, is one job the owner authorizes, with an allowance in Codex credits that Codex cannot
enforce.

The owner wants local testing to keep using the Codex CLI. There is no production roadmap yet.

## Decision

- **An API provider beside Codex, not instead of it.** `HireZero.Integrations.ModelApis` adds `ApiModelProvider`:
  - Anthropic's Messages API serves `anthropic/*` routes;
  - OpenAI's Chat Completions API serves `openai/*` routes;
  - OpenRouter serves any route (it takes `vendor/model` ids) when its key is set and the vendor's own is not.

  Keys are secrets read at use ([ADR 0027](0027-secrets-from-files.md)): `HIREZERO_ANTHROPIC_API_KEY`,
  `HIREZERO_OPENAI_API_KEY` and `HIREZERO_OPENROUTER_API_KEY`.
- **Codex first; the API only on opt-in.** The host uses:
  1. the Codex connection when it is selected (`--company-codex`);
  2. otherwise the API provider, only with `HIREZERO_API_MODELS=1`;
  3. otherwise no model.

  An API key in the environment never turns spending on by itself.
- **The same one-job path.** Like Codex, the provider is unqualified for automatic work. It runs only measured jobs
  the owner authorizes, and checks them exactly: state, runtime identity, policy, route, context and instruction
  digests, and size. Nothing is retried.
- **Money, enforced.** For an API model, the job's allowance is a cap in US cents.
  - Before sending, the provider computes the worst case and refuses if it exceeds the allowance. The worst case
    counts every application byte (at most 4 KiB) as a token, adds a 256-token framing bound and the full output
    limit, and prices them at the route's price.
  - Prices are a reviewed `prices` table in `config/model-routes.json`, in US dollars per million input and output
    tokens, and can be overridden like routes. A route without a price is refused, since its spend cannot be
    capped.
  - A provider that reports more input than the bound is an accounting fault, recorded as such.
  - `ModelRoutes.Cost` turns any recorded usage into dollars.
  - The UI shows the allowance as a spend cap in US cents for an API model. For Codex it keeps its wording as an
    intended credit allowance.
- **Outcomes like every provider.**
  - A complete reply returns with its usage and a receipt (`anthropic:<id>`, `openai:<id>`, `openrouter:<id>`).
  - A truncated reply, a request for a tool, or a filtered reply is withheld, with its usage recorded.
  - A 4xx refusal and a pre-send refusal (cap, price, key) carry zero usage.
  - A server error, or no answer, stays unknown and is never retried.

## Consequences

Hosted mode and the container can run employees with an API key, at a spending cap the owner sets per job. Local
testing is unchanged and keeps using Codex. Automatic (unattended) model work stays off for every provider. Turning it
on, with company-level dollar budgets, is a separate decision. The shipped price table is empty on purpose:
prices change, and the owner adds the models they use.
