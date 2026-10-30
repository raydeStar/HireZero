# The channel count is a minimum: tally fix and retest

The [raw bookshop comparison](2026-10-30-prompt-usefulness-comparison.md) called visits where people said Facebook
prompted them "an upper bound, since other sources may have prompted some visits". That is backwards: many people
who came because of a post never say so, so the count is a minimum. The weekly-plan instructions had put both bounds
in one sentence: responses naming the channel as a minimum, and the week's total responses as an upper bound. Luna
attached the second half to the first count.

## Changes

- `StarterPlanInstructions` now treats only the responses that arrive elsewhere and mention the channel as a minimum,
  for the channel as a whole. Responses that don't mention the channel count as evidence neither for nor against the
  posts. The phrase "upper bound" is gone, and the final self-check asks the model to confirm the count is called a
  minimum. The owner's standalone-post and business-connection corrections from `db5c5da` are unchanged.
- The review guide's post count now recognizes a weekday or full date on a line of its own, as the draft view already
  does. The bookshop plan's three day labels had been reported as "0 day or numbered post headings detected".

## Retest

The owner approved the fix and the retest ("Yes, fix it"). Four runs on Luna were made through the private iteration
runner: the frozen bookshop request and the content-agency request, two of each, with only the starter text changed.
Four runs on the DeepSeek competition stand-in on OpenRouter covered the same cases. All eight completed on the first
attempt, with no retries, and the DeepSeek runs cost $0.0072 in total.

A fresh blind grader scored these drafts alongside six earlier drafts for comparison: the two raw Home bookshop drafts,
two Luna agency drafts from cycles 15 and 16, and two DeepSeek agency drafts from cycle 15. The grades are blind, but
they come from a single grader, with A = 4.0 and A- = 3.7.

| Drafts | Learning signal | Average of 8 criteria |
| --- | ---: | ---: |
| Luna, new, bookshop | 3.70 | 3.34 |
| Luna, raw Home bookshop drafts (earlier instructions) | 2.00 | 2.84 |
| Luna, new, agency | 3.15 | 3.38 |
| Luna, cycles 15–16 agency | 3.15 | 3.31 |
| DeepSeek, new, bookshop | 3.65 | 3.69 |
| DeepSeek, new, agency | 3.85 | 3.66 |
| DeepSeek, cycle 15 agency | 4.00 | 3.52 |

None of the eight new drafts calls a channel count an upper bound. Each one that counts responses arriving elsewhere
calls them a minimum for the channel as a whole. The Home comparison draft was the only draft in the batch with the
logic error, which capped its learning grade at D. Luna's lower agency learning grade came from soft next-week rules
("consider exploring"; "too few" without a threshold), not from wrong logic.

## Still open

- Luna's craft and distinct value stay near B-. Two of its bookshop drafts used the same method in two posts: recall
  what the recipient liked, then compare books.
- One DeepSeek bookshop draft ranks posts by in-shop mentions that it has just pooled for the whole channel.
- One DeepSeek agency draft repeats the same service sentence in all three posts.

These come from two businesses and two samples per model. They show that the fix removed the logic error, not that
first drafts are useful in general. The owner's verdicts on the raw comparisons remain the acceptance measure.

## Verification

- 60 Marketing, assignment and goal-preparation tests pass. The review-guide and section-copy checks pass, and the
  review guide now finds all three posts in the saved bookshop draft.
- CSharpier and the frontend formatter are clean.
- Requests, raw drafts, receipts and grades stay in the ignored private pilot directory.
- Both local installs now run this change, installed at the owner's "Yes, republish".
  - Each update first made a complete data backup and kept the original binaries.
  - Only the Marketing DLL and its symbols changed. All 198 staged files matched each install, and the 16 web files
    were updated without a restart.
  - All twenty companies kept their employees, threads, tasks, artifacts, knowledge, measured attempts and schedules.
    Every audit chain verified.
  - Home terms are unchanged and automatic execution stays disabled.
  - The installed bookshop draft's review guide now reports three post headings.
