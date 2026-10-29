# Weekly-plan rubric iteration

The owner assessed the fresh photography-studio draft: its practical tips and company claims were useful, but
the routine undercounted reply time and the learning plan overstated how precisely the posts track enquiries.
The owner then asked for a scored loop: grade raw drafts on a rubric, research, revise Marketing's starter-plan
instructions and repeat until every criterion reaches at least A-.

## Method

- Both frozen Home requests from the [previous cycle](2026-10-29-channel-aware-copy-and-complete-sections.md)
  (the solo photography studio and the two-person content agency) were rerun with only the starter-plan text
  replaced. A one-shot private runner checked the frozen instruction digests and the pinned runtime, marked
  each run before dispatch and never retried. Terms were unchanged: Codex OAuth, Luna, confirmed high effort,
  v3/6,000. No output was edited or published.
- Eight criteria were graded A+ to F: grounding, craft, distinct value per post, offer connection, clean
  copy, routine realism, learning-signal accuracy, and voice and format.
- The assistant graded each cycle. Fresh grading agents then graded anonymized, shuffled drafts that they
  could not trace to a cycle. Rerun anchors showed the second blind grader runs about half to a whole grade
  stricter than the first. Its grades are the ones reported for the final text.
- Research covered phone product photography, social response norms, lightweight attribution and OpenAI's
  prompting guidance. Findings that shaped the instructions:
  - **Tracking:** there is no evidence that per-post keywords or self-reported sources are accurate, so the
    honest default is a channel-level count, read as a minimum.
  - **Replies:** most people expect a reply within a day.
  - **Prompting:** conflicting instructions degrade the model. Saying what to do works better than saying what
    to avoid, and the model should check its draft against the requirements before returning.

## Changes to the starter-plan instructions

- **Routine:** the time estimate covers posting, replies within a day of each post, and the tally, instead of
  "batch" time. The self-check confirms that its parts add up to the total.
- **Tracking:** off-post responses are counted for the channel as a whole, as a minimum. Any request to say
  where a reader saw a post is a brief aside that doesn't explain the tracking. The plan describes attribution
  exactly as the posts request it, and its tally counts only what the posts ask for.
- **Examples:** a how-to example is written as steps to try, with no disclaimer about results. A worked
  example refers to an earlier tip in a few words and spends its space on new steps. Each tip is checked as
  an experienced practitioner would check it.
- **Format:** posts are plain text. Facebook and LinkedIn show Markdown symbols such as `**` literally, and
  section Copy keeps the exact source text, so pasted posts would show them.
- **Offer:** posts use the business's own first-person voice. The business is described once, following on
  from that post's problem, using the one or two facts that matter to the reader. The brief's company
  sentence is a source, not copy. One or two posts invite enquiries, and the rest ask a question readers can
  answer under the post.
- **Review notes:** a channel or contact route given in the brief or goal is settled, not a decision for the
  review notes.
- **Next week:** the plan gives a clear rule for choosing next week's posts.

## Results

Assistant grades for each case, as photo / agency:

| Instructions | Grounding | Craft | Distinct | Offer | Clean | Routine | Learning | Voice |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Committed (frozen run) | A / A | B+ / B+ | B / A- | B / B+ | B- / B+ | C+ / C | C / A- | A- / A- |
| Cycle 1 | A / A | A- / A | A- / A | B+ / B+ | A- / B+ | A / A | A- / A- | A / A |
| Cycle 2 | A- / A | A- / A | B+ / A | B+ / A- | B / B | B+ / A | A / A | B+ / A |
| Cycle 3 | A / A | A / A- | A- / A | A- / A- | A / A | A / A | A- / A | A / A- |
| Cycle 4, same text | A / A | A / A- | A / A | B+ / A- | B+ / A | A / A | A / A | A / A |
| Cycle 5 | A / A | A / A | A- / A | A / A- | A- / A | A / A | A / A | A / A |
| Cycle 6, same text | A / A | A / A- | A / A | A- / A- | A- / A | A / A | A / A | A / A |

- **Cycle 2:** introduced a "short name to quote" option and found the Markdown problem. Both clean-copy
  grades dropped, and cycle 1's agency draft was rescored for its bold text.
- **Cycle 4:** the photo draft ended every post with "mention X" and once told readers "so the post can be
  identified". Removing the per-post name option fixed this in cycles 5 and 6.

Blind grading did not confirm the assistant's view of cycles 5 and 6:
- **First blind grader:** all four final drafts ranked above both committed-text drafts. The committed
  photo draft scored D on routine and D+ on learning; the committed agency draft scored D on routine and C on
  clean copy. The final drafts still lost marks on two criteria:
  - **Offer:** pasted company sentences, graded B- to B+.
  - **Clean copy:** tracking explanations such as "so we can connect your enquiry", graded B- to B+.
- **Cycles 7 and 9:** two more samples per case each, graded by the stricter grader. Cycle 7 cleaned up
  the copy but introduced two problems:
  - time totals that did not add up;
  - a forced "we" that clashed with a solo studio.
  
  Cycle 9 fixed both.

Strict blind grades for the final (cycle 9) text, two samples per case:

| Draft | Grounding | Craft | Distinct | Offer | Clean | Routine | Learning | Voice |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Photo, sample 1 | A | B+ | A- | B- | A | A | A- | A |
| Photo, sample 2 | A | B+ | A | B+ | A | A | B | A- |
| Agency, sample 1 | A | B | B- | B | A | A | B | A- |
| Agency, sample 2 | A | A- | B+ | B+ | A | A- | A- | A- |

- **Criteria that now hold:** grounding, clean copy, routine realism, and voice and format reach at least A-
  in every final draft. This covers the owner's two complaints and the disclaimer.
- **Comparison with the cycle 5–6 text:** on the same grader, the final text scores higher on offer
  connection (B- to B+, against C+) and clean copy (A, against C+ to B+).

## Remaining gaps

The target was not met on four criteria. Each varies between B- and A from run to run:
- **Craft depth:** missing points such as anonymizing client details or steadying the phone.
- **Distinct value:** a later post re-applies an earlier framework.
- **Offer line:** it still leans on the brief's wording. Strict grounding pulls the model toward the safest
  phrasing.
- **Next-week rule:** some rules say only "consider", have no tie-break, or tally answers to a question a post
  never asked.

Further instruction changes showed small, noisy effects. These two fictional businesses and four final samples
support a narrower claim, not repeatable unedited quality across the market. Owner acceptance remains open.

## Usage

Twenty runs, 164,063 reported input/output tokens, 24–43 seconds each, with 596–2,292 reasoning tokens.
None was withheld.
- **The 31-run allowance:** the first six runs used its last six, totalling 220,397 reported tokens.
- **The owner's further instruction:** the other fourteen ran under the owner's instruction to cycle until
  confident, after the owner said heavy Luna use was fine.

Actual credit charges are unknown. Inputs, raw outputs, receipts, rubric and grades stay in the ignored private
pilot directory.

## Verification

- 56 Marketing, assignment and progress tests pass, including a new focused fact for the instruction changes.
- CSharpier is clean.
- No browser check or fixture depends on the instruction wording.
- The installed apps still run the previous instructions until they are republished.
