# 0088: Capture reviewed feedback from a work result

Date: 2026-10-26

Status: Accepted; fictional lifecycle, owner journey and desktop/mobile layout verified locally.

## Context

Structured memory already supported owner feedback and exact citations to approved artifacts, but owners had
to leave a result, open Memory and find the result again. The source catalog also offered approved artifacts
whose supporting context had become stale, even though saving a memory from those artifacts was correctly
refused. This made the proposed learning loop unnecessarily awkward.

## Decision

- Add an owner action beside an approved result, including the focused Work window, that opens a company-scoped
  memory source route. Canceled and superseded tasks do not offer this action. Server checks remain authoritative.
- The owner starts an editable feedback draft from that result. Default its audience to the originating employee,
  bind its citation to the selected artifact and prefill at most 600 characters of its opening excerpt. Leave the
  actual feedback statement blank for the owner. Explain that the excerpt may need replacing with the relevant
  passage. This is a form shortcut, not a generated assessment of the result's quality.
- Preserve unrelated unfinished memory drafts and pending receipts. Require the owner to finish the current
  draft before starting this one. Keep the feedback draft, citation and employee audience across reloads.
- Reuse the existing separate save and exact approval steps. Only approved, current memory enters bounded
  retrieval; changing its audience requires that same explicit review. Source revocation, expiry and existing
  context lineage checks continue to fence future work. No result is silently promoted into company facts.
- Apply the same current-source checks when listing or inspecting work for memory as when saving the citation.
  Exclude revoked artifacts, canceled/superseded tasks and work whose supporting context is no longer current.
- Keep a saved source selection visible while loading. If a source read fails, retain draft text, explain the
  failure and offer an explicit read retry. Do not enable saving a cited draft until the selected source has
  loaded successfully; save and approval still revalidate authority on the server.
- Distinguish read failures from uncertain writes in the shared API client. A failed GET reports unavailable
  information. A failed or malformed write still has an unknown outcome and requires receipt recovery. Neither
  is automatically replayed.

No schema change, model call, runtime upgrade or new external permission is needed. Semantic contradiction
detection, model-generated memory proposals, memory deletion/retention and measured retrieval usefulness remain
separate work. This decision extends [ADR 0060](0060-structured-organizational-memory.md).

Verification is recorded in [the feedback-memory receipt](../history/2026-10-26-result-feedback-memory.md).
