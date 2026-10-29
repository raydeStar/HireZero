# 0165 - Room for high-effort reasoning in deliverables

Status: accepted, 2026-10-28. Amends [ADR 0134](0134-high-effort-deliverables.md).

`measured-deliverable-v2` sized its 3,000-token output target for the runtime pinned at the time, where high effort
reasoned 287-1,445 tokens. Reasoning counts as output. On the current pin the effort only took hold once it was set
at thread start ([note](../history/2026-10-28-runtime-ignored-turn-effort.md)). Two private pilot cases were then
rerun on Luna at high effort with their earlier inputs unchanged. One reasoned 1,616 tokens and completed at 2,312
output tokens. The other reasoned 2,637 tokens, passed 3,000 output tokens before its answer was complete, and was
stopped and withheld with no draft. A withheld draft spends the tokens and returns nothing. The owner prefers to
spend more Luna tokens over losing drafts.

## Decision

- `measured-deliverable-v3` is the deliverable policy: high effort with a 6,000-token output target, about twice
  the highest observed reasoning. Planning tokens (22,000), the 90-second deadline and the 9,000-character visible
  stop are unchanged.
- `measured-deliverable-v2` stays known with its digest. A recurring schedule approved under it keeps it, and its
  3,000-token limit, until the owner reviews the schedule again; a raised limit is not granted without review. A
  prepared, undispatched v2 job is stale and is prepared again under v3.
- API routes keep ADR 0042: low effort and the 1,800-token deliverable limit. Since
  [ADR 0166](0166-openrouter-deliverables-get-their-output-target.md), OpenRouter routes get the policy's own
  target.

## Consequences

- A deliverable may use up to twice the output tokens v2 allowed before the tripwire stops it.
- High-effort Luna can still exceed 6,000 output tokens. A larger target needs a further reviewed version.
- Existing v2 schedules may still be stopped by their approved limit until reviewed again.
