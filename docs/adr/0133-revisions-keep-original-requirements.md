# 0133 - Revisions keep the original requirements

Status: accepted, 2026-10-27.

The first native weekly-plan revision addressed its feedback but removed two items the original request required
and the previous draft already contained: a channel recommendation labeled as a hypothesis and a measurable
learning goal. It also cited an audience from the feedback as an approved brief field. The original request was
in the revision context; the instructions gave no precedence between it and the feedback. The feedback listed
most deliverables and was read as the complete scope. The Marketing role's "flag channel selection once" rule,
repeated in the feedback, was read as permission to leave the channel to the owner.

Text turns run at low effort with no reasoning tokens, so instructions to check work before returning had little
effect. Structure did: revision instructions now ask the model to cover every deliverable and constraint the
original request names (parts, lengths, exclusions) in its order, apply feedback to each, then add new requests.
An item is dropped only when the feedback explicitly says so; when feedback and an item seem to conflict, both
are satisfied. Feedback is owner direction for that draft, never attributed to a business brief field. This text
is role-neutral and lives in core.

Marketing starter plans write "Channel hypothesis: <one named channel>; fit unconfirmed" when the owner asks for a
channel; portable copy and flagging the decision accompany the recommendation rather than replacing it. The
learning goal is one sentence separate from the tally, observations restate only their cited brief field, assets
are written once, and missing facts and owner decisions share one list of at most three. This refines ADR 0129's
distribution note; ADR 0128's revision context is unchanged.

Evidence is a private replay of the pilot revision through the same runtime, model and effort, graded per
criterion; see [the evaluation note](../history/2026-10-27-revision-requirements-eval.md). Live owner acceptance
of a new native revision remains separate.

Recurring schedule approvals include the role's instructions, so Marketing schedules approved before this change
are no longer current and must be reviewed again, as for any instruction change.
