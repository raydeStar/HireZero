# Revision requirements evaluation

The pilot revision dropped a requested channel hypothesis and learning goal (ADR 0133). A private harness replayed
that revision with the same pinned Codex runtime, OAuth profile, model, low effort, developer instructions and
context bytes as the measured call; the replayed context and instruction digests matched the saved packet. Only
the role and revision instructions changed between variants. A separate high-effort model call graded each
output against the original request and the feedback, one pass/fail per criterion; word counts were computed in
code. The harness, inputs and outputs stay with the private pilot data and are not committed.

Pilot revision, share of samples passing:

| Criterion | Previous prompts (6) | First fix, 5638221 (6) | Final (10) |
| --- | --- | --- | --- |
| Named channel hypothesis | 1 | 2 | 10 |
| Explicit learning goal | 4 | 1 | 10 |
| No feedback credited to a brief field | 1 | 2 | 9 |
| At most three unknowns | 6 | 4 | 9 |
| No replies or low reach is inconclusive | 2 | 4 | 9 |
| Word limits (500 total, 40-65 per post) | 3 | 6 | 8 |
| Channel uncertainty raised only once | 0 | 1 | 2 |

Feedback that explicitly removed the channel and schedule was honoured in 4 of 4 final samples (previous: 3 of 4),
with everything else retained. A fictional single-post revision whose short feedback never mentioned the
request's length kept it in 7 of 8 final samples (previous: 3 of 6, counted in code). First runs, which do not
receive revision instructions, kept the channel and learning goal in 4 of 4. Identical first-run instructions
scored 4 of 4 and 2 of 4 on one criterion in separate batches, so differences of that size are noise.

Not fixed: posts often carry more than one call to action, the five-line brief is often written inline, and the
channel is usually both labeled unconfirmed and listed as an owner decision, which the request's own wording
invites. These also occur with the previous prompts and in the owner's editorial correction. No owner acceptance
of a new native revision is implied.
