# 0169 - OpenRouter reasoning per model

Status: accepted, 2026-10-29. Amends [ADR 0042](0042-openrouter-low-effort-and-a-price-ceiling.md) and follows
[ADR 0166](0166-openrouter-deliverables-get-their-output-target.md).

## Context

The owner's build for the Vultr Agent Rush competition will use DeepSeek V4.1 Flash, served by Vultr. Until the
owner has a Vultr key, "production" tests of that model run through OpenRouter; normal testing stays on Luna. Every
OpenRouter request was sent with `reasoning.effort = "low"`.

Three owner-approved runs of the frozen photography weekly-plan request used the company's own API transport:

| Run | Output limit | Reasoning effort | Reasoning tokens | Result | Billed |
| --- | --- | --- | --- | --- | --- |
| 1 | 1,800 | low | 1,800 | withheld: truncated | $0.0010 |
| 2 | 6,000 | low | 5,533 | withheld: truncated | $0.0031 |
| 3 | 6,000 | none | 0 | complete, 1,166 output tokens in 11.5 s | $0.00068 |

OpenRouter's listing for this model supports `max`, `high`, `low` and `none` and defaults to high. Its reasoning
is not mandatory. At low it still reasoned through the whole limit.

The [same strict blind grader](../history/2026-10-29-weekly-plan-rubric-iteration.md#model-checks) that scored
Luna's final photography drafts scored run 3. It averaged about B, against about A- for both Luna drafts:
- **Better than Luna:** the posts. Craft scored A-, distinct value A and the offer A-.
- **Worse than Luna:**
  - the routine scored C, because its time parts left out posting;
  - the learning plan scored D+, because it counted enquiries per post that no post asked readers to attribute;
  - the review notes treated the settled contact form as a decision.

## Decision

- **Per-model setting:** the model routes catalog gains a reviewed `reasoning` section. It sets the effort
  OpenRouter is asked for, model by model, and allows only "none" or "low".
- **Default:** an unlisted model keeps low effort, as before.
- **Overrides:** the owner's model-routes file can override an entry, as it can a price.
- **DeepSeek:** `deepseek/deepseek-v4.1-flash` is set to "none" in the repository's catalog.
- **Vendor APIs:** Anthropic and OpenAI requests are unchanged.

## Consequences

- **Completion:** without reasoning, this model completes a weekly plan in about 1,200 output tokens, well inside
  the deliverable limit.
- **Quality:** on one sample, it writes stronger posts but follows the plan's self-checks less well than Luna at
  high effort. Those self-checks cover time parts that add up, a tally limited to what the posts ask for, and
  settled routes left out of decisions. Instruction changes or a different effort may close that gap. Each further
  production test is one OpenRouter request a day unless the owner approves more.
- **Not a competition setting:** Vultr serves the `deepseek-v4.1-flash-1017` snapshot, with efforts from minimal
  to ultra and no "none". So reasoning off is not available there.

## Subsequent evidence

The owner judged the reasoning-off draft unshippable. A
[competition stand-in](../history/2026-10-29-weekly-plan-rubric-iteration.md#competition-stand-in) ran the 1017
snapshot at low effort with room for 16,000 output tokens. Two samples per case all completed:
- photography drafts used 6,200–7,200 output tokens;
- agency drafts used about 10,500, up to 9,416 of them reasoning.

Blind-graded, the four averaged about A-, level with Luna's final samples.

So reasoning off trades most of the plan's quality for fitting the 6,000-token deliverable limit. Using this model
in HireZero with reasoning on would need a deliverable target above 6,000 tokens. That would be a further reviewed
policy version. HireZero does not use this model today.
- **Not yet usable in HireZero:** the owner's model-routes override has no price for this model, so the app refuses
  it until one is added.
