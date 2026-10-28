# 2026-10-28 - The pinned runtime ignored the turn's reasoning effort

After the runtime pin moved from `codex-cli 0.164.0-alpha.11.1` to `0.165.0-alpha.1.2`
([ADR 0155](../adr/0155-durable-local-codex-runtime.md)), measured deliverables still recorded
`measured-deliverable-v2` with high effort ([ADR 0134](../adr/0134-high-effort-deliverables.md)), but Luna reported
zero reasoning tokens on 21 of 22 private pilot drafts. On the earlier runtime, every high-effort replay of the same
revision reasoned.

## Cause

The new runtime still accepts `effort` on `turn/start`, but a turn sent with it alone ran without reasoning. A fresh
thread reports no `reasoningEffort` unless `thread/start` sets `config.model_reasoning_effort`, and the model then
falls back to its catalog default (medium for Luna, low for Sol). Schema agreement during the runtime review confirmed
the field existed, not that it took effect.

A private replay of one pilot revision, with identical instructions and context bytes:

| Runtime and effort setting | Reasoning tokens | Output tokens | Seconds |
| --- | ---: | ---: | ---: |
| 0.164, turn effort high (six earlier runs) | 479-1,377 | 1,082-1,955 | 12-20 |
| 0.165, turn effort high | 0 | 614 | 8 |
| 0.165, thread and turn effort high | 1,447 | 2,093 | 21 |

Pilot comparisons between Luna and Sol run on 0.165 therefore compared Luna at its default with Sol at low effort.

## Fix

Text and coding threads on the pinned runtime set `config.model_reasoning_effort` to the reviewed effort at
`thread/start` and keep it on `turn/start`. Before any turn is sent, the thread must report that same
`reasoningEffort`; otherwise the request is refused with no model turn, like an unconfirmed isolation check. The fake
runtime applies only the thread's effort and has modes that drop it, so a future runtime that ignores the setting is
refused rather than silently downgraded.

Drafts produced on 0.165 before this fix ran without the reviewed effort. Comparisons that depend on effort should be
repeated.
