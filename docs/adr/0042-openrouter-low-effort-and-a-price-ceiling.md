# 0042: OpenRouter calls use low effort and a price ceiling

Status: Accepted. Extends 0036.

## Context

The API model provider (0036) caps every job at its allowance in US cents. Before sending, it checks the worst case
against the route's reviewed price: every input byte counted as a token, plus the full output limit. That check is
honest only if the provider really bills at that price.

Two things weaken it through OpenRouter:
- **Routers price by request.** Some OpenRouter models are themselves routers (for example a "Jev"-style router)
  that pick an underlying model, and its price, for each request. No single reviewed price describes them.
- **Reasoning effort.** Reasoning tokens count as output. A model left to choose its own effort spends more of the
  output limit on thinking, and the owner asked that employees run at low effort, never extra-high.

## Decision

On the OpenRouter path, every measured request also carries:
- `reasoning.effort = "low"`;
- `provider.max_price`, set to the route's reviewed price (input and output, US dollars per million tokens).
  OpenRouter then serves the request only from an endpoint at or below that price, and refuses it otherwise.

The worst case is still computed from the reviewed price, so it becomes a ceiling OpenRouter enforces rather than
an assumption. A variably priced router can be used: the owner reviews the most they will pay per million tokens and
writes it in the model prices file, and nothing is billed above it. A refusal is a clear 4xx and is reported as not
sent, with no usage.

Vendor APIs (Anthropic, OpenAI) get neither field: they bill at their published price, and OpenAI rejects a
reasoning effort on models that do not reason.

## Consequences

- A price set too low makes OpenRouter refuse the job. The owner raises it after checking the route's listing.
- Low effort can shorten reasoning on hard tasks; measured jobs are single short turns, where that is the right
  trade.
