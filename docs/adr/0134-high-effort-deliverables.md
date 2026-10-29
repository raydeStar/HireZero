# 0134 - High reasoning effort for measured deliverables

Status: accepted, 2026-10-27.

Measured text turns on the Codex subscription runtime were always sent at low effort, in line with the owner's
request in ADR 0042 that employees run at low effort, never extra-high. A private replay of the pilot weekly-plan
revision (same runtime, model, instructions and context bytes; see
[the evaluation note](../history/2026-10-27-revision-requirements-eval.md)) compared efforts. Medium spent no
reasoning tokens, so its 7 of 10 against low's 5 is sampling noise. High passed every must-have check in 10 of 10 runs against 5 of 10 at low,
with 287-1,445 reasoning tokens, 896-2,096 output tokens and 12-20 seconds. Extra-high used about 10,000 output
tokens and up to 98 seconds without a clear gain. Reasoning counts as output, so 3 of 22 high runs would have
crossed the 1,800-token output target and been discarded.

## Decision

- `CompanyMeasuredPolicy` gains an optional `Effort`, sent as the Codex turn effort. Absent means low and is omitted
  from JSON, so stored policies, jobs and schedule approvals keep their digests. Qualification turns stay low.
  Since 2026-10-28 it is also set at thread start, and the thread must confirm it before any turn is sent; the
  pinned runtime had ignored the turn's own effort
  ([note](../history/2026-10-28-runtime-ignored-turn-effort.md)).
- `measured-deliverable-v2` runs at high effort with a 3,000-token output target. Planning tokens (22,000), the
  90-second deadline and the 9,000-character visible stop are unchanged. It covers every measured deliverable:
  Marketing, CEO, Developer and plan work.
- `measured-deliverable-v1` stays known. A recurring schedule approved under it keeps it, both for its occurrences
  and for its approval check; a new or re-reviewed draft gets v2. A prepared, undispatched v1 job is stale and is
  prepared again.
- API routes keep ADR 0042: low effort, and a deliverable keeps the 1,800-token output limit and the same worst-case
  price check.
- The owner's job and schedule reviews show the effort beside the soft limits.

## Consequences

- A typical deliverable uses about 11% more total tokens, roughly twice the output tokens and 7 more seconds.
- Only Marketing deliverables were evaluated; other roles get the same policy untested.
- This narrows ADR 0042's low-effort preference to API routes and qualification. Extra-high stays excluded.

## Subsequent evidence

[ADR 0147](0147-usable-specialist-first-assignments.md) records one baseline and one revised starter request
for each of CEO and Developer, all at high effort. Both revised outputs include usable small artifacts, while
the CEO sample still misses explicit brief-field citations. This closes the absence of any installed specialist
sample; repeatable quality, other specialist tasks and owner usefulness acceptance remain unverified. It is
not an effort comparison and does not change this policy.

Once the pinned runtime applied the effort, reasoning reached 2,637 tokens and stopped a draft at the 3,000-token
target. [ADR 0165](0165-room-for-high-effort-reasoning.md) raises the target to 6,000 in `measured-deliverable-v3`.

On OpenRouter, DeepSeek V4.1 Flash spent the whole 1,800-token API limit on reasoning at low effort.
[ADR 0166](0166-openrouter-deliverables-get-their-output-target.md) gives OpenRouter deliverables their policy's own
output target.
