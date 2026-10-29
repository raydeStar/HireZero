# 0166 - OpenRouter deliverables get their output target

Status: accepted, 2026-10-29. Amends [ADR 0134](0134-high-effort-deliverables.md) and
[ADR 0165](0165-room-for-high-effort-reasoning.md) for OpenRouter routes.

## Context

ADR 0134 kept API deliverables at low effort with a 1,800-token output limit, assuming low effort leaves room for
the answer. ADR 0165 kept that limit for API routes when the Codex path moved to 6,000.

The owner's Vultr competition build will use DeepSeek V4.1 Flash, tested through OpenRouter until then. One run of the frozen
photography weekly-plan request used the company's own API transport
([note](../history/2026-10-29-weekly-plan-rubric-iteration.md#model-checks)). At low effort the model spent all
1,800 output tokens on reasoning and returned no text. The transport withheld the reply as truncated, and
OpenRouter billed $0.0010.

Reasoning counts as output, and each model decides how long low effort reasons, so the limit's assumption does not
hold for every route. As in ADR 0165, a withheld draft spends the tokens and returns nothing. The owner's job
reviews already show the policy's output target (6,000 under v3) for API jobs, so the old limit was narrower
than what the owner reviewed.

## Decision

- **Output limit:** through OpenRouter, a measured call without its own output limit gets its policy's output
  target:
  - 6,000 tokens for `measured-deliverable-v3`;
  - 3,000 for v2;
  - 1,800 for v1.

  Other policies are unchanged, because their target already was the limit.
- **Effort and price:** reasoning stays at low effort, with the reviewed price as OpenRouter's ceiling
  ([ADR 0042](0042-openrouter-low-effort-and-a-price-ceiling.md)).
- **Spending cap:** the worst-case check uses the same limit, so the job's allowance in US cents is still the
  spending cap. A job whose allowance does not cover its full output target is refused before anything is sent.
- **Vendor APIs:** Anthropic and OpenAI keep the 1,800-token deliverable limit. No evidence yet shows them running
  out.
- **Policy versions:** no version changes. The limit now matches the target each approval already records.

## Consequences

- **Cost:** on the photography request the worst case rises from about 0.14 to 0.35 cents on DeepSeek V4.1 Flash,
  at its listed price on 2026-10-29. On Luna through OpenRouter it rises from about 0.20 to 0.41 cents. Both stay
  within the default 1-cent allowance.
  - Routes with higher output prices may need a larger allowance, and the refusal says so.
  - Existing v2 and v3 approvals on OpenRouter may now spend up to their recorded target where they used to stop
    at 1,800, still within their unchanged allowance.
- **Unverified:** whether DeepSeek V4.1 Flash finishes a weekly plan within 6,000 tokens, and how good that plan
  is. While testing, the owner allows one OpenRouter run a day.
- **Untested risks:**
  - Conversation replies keep their 1,000-token target, which DeepSeek's reasoning might also use up.
  - OpenAI's own API is sent no effort, so its reasoning models might run out of 1,800 tokens.
- **Timeout:** the API client's 55-second timeout still applies. A slow route could reach it before 6,000 tokens.
