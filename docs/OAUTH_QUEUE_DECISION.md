# Decision: use measured admission for the OAuth employee queue

Use a bounded, explicitly authorized **measured OAuth** contract for native employee text work. Do not make an exact native tokenizer or a small upstream output cap a prerequisite that this protocol cannot satisfy. Keep the strict preflight profile for transports that can support it. Do not set the existing global `Qualified` flag to true or silently reinterpret that strict profile.

This is the recommended next queue contract, not activation or additional spending authorization. Automatic workers and real Developer execution remain off. The completed four-call authorization is exhausted.

## Requirements versus defaults

| Item | Origin and consequence |
| --- | --- |
| Included allowance first; at most 1,000 existing credits for the authorized four checks; no purchases, recharge or API-key fallback | Explicit owner spending constraints. The 1,000 figure was authorization for those checks, not a recurring company budget. No new authorization is implied here. |
| Pinned runtime/models, verified process-local MXC, restricted scratch reads, no tools/retrieval/network, isolated employee context, no automatic replay | Owner-approved isolation scope and application safety controls. Preserve them. |
| 4,000 input / 1,000 output / 5,000 total tokens and a 60-second deadline | Introduced in development commit `50182b8`; its handoff calls them proposed offline defaults. They were application design choices, not an owner demand that native OAuth expose hard token APIs. |
| An exact full-input counter and upstream output cap for every queue adapter | Our strict-adapter contract. It is useful where supported, but treating it as the only possible OAuth contract created the deadlock. |
| 4,355–4,400 observed native input tokens | Actual results from four tiny qualification calls. They include application and runtime input; they are not an exact isolated overhead measurement or a bound on future work. |

## API capability

The pinned `0.162.0-alpha.17.2` generated `TurnStartParams` supports effort, output schema, permissions and streamed usage, but contains no `max_output_tokens`, `max_completion_tokens` or equivalent per-turn token ceiling. Its configuration schema's token settings cover context compaction and tool/history outputs, not a small model-completion cap. The [app-server documentation](https://learn.chatgpt.com/docs/app-server) likewise describes usage notifications and turn controls without exposing that cap. We found no supported small hard output bound through this installed native interface.

The direct Responses API does provide `max_output_tokens`, covering generated reasoning, visible output and formatting tokens. That is a different interface; it is not a field we can add to native `turn/start`, and using it would require a separately authorized integration. See [official reasoning cost controls](https://developers.openai.com/api/docs/guides/reasoning#controlling-costs). No API-key fallback is proposed.

An output schema constrains the final data shape; low effort, word requests and local character checks do not become a full-generation token ceiling. A local timeout/interrupt also does not prove an instantaneous end to remote generation or billing.

## Smallest implementable queue change

Reuse the existing task/context fence, shared account gate, cancellation drain, usage receipts and artifact-review path. Add one explicit per-attempt admission kind, `measured-oauth`, plus a manually armed request allowance. Bind that policy, task version and reviewed context digest before dispatch. Do not introduce another background worker or pretend that an observed count is an exact preflight quote.

For the first version:

- **Hard application controls:** one allowed native `turn/start`, fresh ephemeral thread/process, pinned route/identity, at most 4 KiB combined UTF-8 role instructions and serialized context, one call in flight, no application retry, unchanged MXC/tool restrictions. A native turn is not a promise of one billable HTTP attempt; internal retry behavior must be inspected/pinned separately.
- **Planning allowance:** 8,000 provider tokens per attempt. Treat 4,500 tokens as a rounded baseline-input allowance based on the entire observed minimal input, 2,500 as packet/headroom allowance and 1,000 as output target. This decomposition is planning, not exact tokenization or an upper bound; it deliberately does not subtract runtime input from the bill. Reserve it explicitly in the module ledger rather than silently changing the old 5,000 default.
- **Soft controls:** 60-second local deadline, short-draft instructions, 2,000 visible characters and an 8,000 observed-total tripwire. These stop/withhold locally after detection and can be exceeded upstream. Label them accordingly.
- **Settlement:** retain and account for the complete actual input/output/reasoning usage once, including runtime input. A complete ordinary result within policy becomes an owner-review artifact. Missing/inconsistent usage, a tripwire breach, tool request, credit/quota anomaly or unconfirmed termination withholds content and blocks further dispatch. Preserve known overage explicitly; never clamp the receipt, charge only application tokens, or replay an uncertain call.

The measured branch should bypass only the strict counter requirement, not spending permission, context review, isolation, request-count authority or receipt validation. Admission-kind metadata makes that difference reviewable. An absolute provider-enforced credit guarantee remains unavailable through this native interface; if the owner requires one, this OAuth design cannot promise it and must remain stopped pending another explicitly approved control.

## What 150 credits actually means

The old qualification reserve assumed `(1.05M input × 2.5 + 0.128M output × 12.5) × 5 × 6 = 126.75 credits`, then rounded to 150. The rates are Standard Codex credit rates: 2.5 per million uncached input and 12.5 per million output; cached input is 0.25. They are credit rates, not dollar API prices or a prediction of included subscription usage. See [Codex pricing](https://learn.chatgpt.com/docs/pricing#token-rates).

The arithmetic is reproducible; its claimed precision is not justified. It used the full context window as input, although the current [Luna model reference](https://developers.openai.com/api/docs/models/gpt-6-luna) lists 922,000 maximum input and 128,000 maximum output within a 1,050,000 context window. It also assumed five HTTP attempts times six stream attempts were thirty independently billable full generations. The installed provider schema exposes retry settings, but neither that schema nor the fetched docs establish that billing multiplication. Native limits may differ from the general API catalog, and included usage is separate from credit billing.

Therefore 150 is a historical planning buffer for a qualification request, **not a proven maximum charge, minimum cost, or ordinary queue price**. For scale only, 5,000 uncached input plus 250 output at those Standard credit rates would project about 0.0156 credits if metered that way. This is not an invoice or a hard ceiling. The next single-call test can retain the existing 150 planning buffer provisionally without increasing the owner's allowance; a repeatable queue should settle actual usage and revise its reserve policy from verified retry/rate evidence rather than permanently consuming 150 per tiny task. Unknown exposure stays reserved.

## Proposed next test — no approval requested or run

One manually armed `marketing-draft` task, one fictional employee in a fresh company root, a reviewed short campaign brief and minimal fictional approved facts, no imported history, no handoff and no publication. Use the native Luna route and the same verified MXC profile. Request only a headline, short paragraph, claim sources and missing-fact questions. Run through the real queue fence into one review-required artifact, then stop.

Expected usage is roughly 5,000–6,000 provider tokens, based on the 4,400-token minimal-call baseline plus a short Marketing packet/draft; this is a planning estimate. Give the test an explicit 8,000-token planning reservation and post-result stop rule. Its future authorization must name the exact packet/policy, one native request and any existing-credit fallback allowance, acknowledge the soft controls/no provider-enforced credit cap, and retain no purchase/recharge/API fallback. Do not reuse the four-call approval or change account spending/security settings. No new approval is sought in this code-only phase.

Local follow-through here is deliberately small: native anomalies now retain completed usage/billing metadata while withholding content, and failed final account reconciliation carries known usage forward. The new measured queue admission branch remains the next implementation step; it has not been enabled implicitly.
