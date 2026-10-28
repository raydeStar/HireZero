# ADR 0149: Separate company-bound Jev decisions from text routing

Status: transport and company factory implemented; application admission and UI remain pending.

## Context

The owner connected OpenRouter for Jev while choosing Codex OAuth for Luna. The legacy Jev tool reads a
machine-level TypeSafe key; the OpenRouter choice tool uses chat completions. Neither is the required
company-scoped, metered Jev decision workflow. Ordinary employee actions cannot serve as its spending approval.

OpenRouter's [Jev guide](https://openrouter.ai/docs/guides/community/jev) distinguishes typed decisions from
the router that selects another text model. The [Decisions API](https://openrouter.ai/docs/api/api-reference/alphadecisions/submit-a-decisions-request)
accepts state and typed questions and returns decisions, a resolved model and usage. Its documented contract
is not a reason to assume the chat endpoint's output-token or price-limit behavior applies here.

## Decision

- Add a transport in the Jev integration, with a required company identity and explicit company credential
  reader. The host factory uses that company's existing protected OpenRouter secret. Only the original local
  company retains its existing local key alias. No credential is placed in a request snapshot or result.
- Support one bounded choice question, two to eight named choices, text/JSON state and an explicit versioned
  Jev model. Router IDs, latest aliases and chat models are refused. The request is immutable, reviewable as exact
  JSON, and digested together with company identity. State is at most 8 KiB and the serialized request 16 KiB.
- Send only to the fixed Decisions endpoint, with provider fallback disabled and no redirect or automatic
  retry. A 30-second local deadline covers headers and body, and the body is read only up to 32 KiB plus the
  overflow byte. These local limits do not prove the provider stopped or cap its charge.
- Require a valid choice, confidence, complete normalized probabilities, compatible resolved model, receipt
  and reported usage/cost before returning advice. Preserve valid accounting when advice is withheld. Partial
  accounting or a lost response remains unknown; missing usage is never recorded as zero.
- A request object cannot dispatch twice. That latch is supplemental only: the host still needs durable
  admission before a send and durable reconciliation after an uncertain outcome, including after restart.
  Revocation or cancellation on a repeated call cannot relabel the earlier dispatch as unsent.
- Keep this transport out of the ordinary tool registry and HTTP execution routes. No settings migration,
  credential read at construction, automatic model selection or fallback to an owner's text provider occurs.

## Remaining delivery

The next step is a company-owned decision record and reviewed one-shot admission: exact immutable packet,
owner identity, credential/configuration revision, expiry, intended spending allowance, monthly reservation,
atomic dispatch claim, durable outcome/usage and explicit reconciliation. UI must display the actual external
data and soft-limit terms. A returned recommendation is advisory and cannot approve or execute downstream work.
Only after those mechanics have fictional lifecycle/browser proof should an owner-approved live decision be
used to assess quality and provider accounting. This ADR does not claim that application workflow is delivered.

See [transport and company-isolation evidence](../history/2026-10-27-jev-decision-transport.md).
