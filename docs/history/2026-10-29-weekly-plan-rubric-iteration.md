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

## Model checks

The owner approved two runs of the final text on Sol 6.1 to see whether the remaining gaps come from the model.
The owner then said their Vultr Agent Rush competition build will use DeepSeek V4.1 Flash, served by Vultr. Until
they have a Vultr key, it is tested through OpenRouter. They asked for one run there,
at most one a day while testing, because it costs money. Both checks used the frozen photography request and the
same one-shot runner, with no retry. Neither returned a draft, so nothing was graded.

- **Sol 6.1 at high effort:** Home's 90-second deadline cancelled the run before any reply or usage was
  reported, so its usage is unknown. Earlier, Sol at low effort took 52–60 seconds on similar requests. The
  second approved run was left unused, because it would most likely hit the same deadline. Under the v3
  deliverable terms, Sol at high effort cannot show whether the gaps come from the model.
- **DeepSeek V4.1 Flash through OpenRouter:** the run used the company's own API transport. Its terms:
  - low reasoning effort;
  - the listed price as OpenRouter's price ceiling: $0.045 input and $0.50 output per million tokens, checked
    2026-10-29;
  - a 1-cent cap.

  The model spent all 1,800 output tokens on reasoning and returned no text, so after 8.5 seconds the transport
  withheld the reply as truncated. OpenRouter billed $0.0010 for 2,231 input and 1,800 output tokens.

The API path limits a deliverable to the low-effort output target of 1,800 tokens
([ADR 0134](../adr/0134-high-effort-deliverables.md)). That target assumes low effort leaves room for the answer,
and DeepSeek V4.1 Flash did not leave it on this request.
[ADR 0166](../adr/0166-openrouter-deliverables-get-their-output-target.md) now gives OpenRouter deliverables their
policy's output target, 6,000 tokens under v3.

The owner approved two more DeepSeek requests that day:
- **Second run, 6,000-token limit:** still withheld as truncated. The model reasoned through 5,533 of the 6,000
  tokens and was billed $0.0031.
- **Third run, reasoning off:** the owner chose to turn reasoning off.
  [ADR 0169](../adr/0169-openrouter-reasoning-per-model.md) sets that per model, and the run completed in 11.5
  seconds with 1,166 output tokens and no reasoning, billed $0.00068.

The strict blind grader that scored Luna's final photography drafts (samples 1 and 2 above) graded that draft on
the same scale, without knowing which model wrote it:

| Draft | Grounding | Craft | Distinct | Offer | Clean | Routine | Learning | Voice |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DeepSeek V4.1 Flash, reasoning off | B | A- | A | A- | A | C | D+ | C+ |

- **Ranking:** the grader ranked both Luna drafts above it.
- **Posts:** its posts were the strongest of the three, with a bounce card for dark glazes, a clear choice between
  white card and a surface, and an offer tied to the post's problem.
- **Plan:** it missed the plan's self-checks.
  - Its time estimate left out posting.
  - Its tally counted enquiries per post that no post asked readers to attribute.
  - Its review notes asked the owner to confirm the contact form the brief already settles.
  - Two posts were single dense paragraphs.
  - Part of the grounding mark was for the app's own `[connect: …]` request line, which the app turns into a
    Connect card.
- **Scope:** this is one sample. The owner judged it unshippable.

### Competition stand-in

Vultr cannot turn this model's reasoning off. It serves the dated `deepseek-v4.1-flash-1017` snapshot at NVFP4,
with reasoning efforts from minimal to ultra and output up to the full context. So the owner chose reasoning with
more room, through OpenRouter until they have a Vultr key, copying Vultr as closely as possible. One direct
OpenAI-style request, never retried, used:
- the 1017 snapshot;
- low effort;
- room for 16,000 output tokens;
- 4-bit hosts only, at no more than the listed $0.15 and $0.60 per million tokens;
- the same frozen messages.

The owner then set a $10 daily budget for these tests, and two samples per case were run. Every run completed with
`stop`:

| Run | Host | Output tokens | Reasoning | Seconds | Billed |
| --- | --- | --- | --- | --- | --- |
| Photo 1 | Wafer (MXFP4) | 6,227 | 4,710 | 40 | $0.0032 |
| Photo 2 | Wafer | 7,150 | 5,594 | 27 | $0.0036 |
| Agency 1 | Sail Research (FP4) | 10,265 | 8,937 | 139 | $0.0016 |
| Agency 2 | Sail Research | 10,534 | 9,416 | 143 | $0.0016 |

Two earlier agency requests reached OpenRouter but hit the runner's 180-second client timeout. Their outcome is
unknown, they were not replayed, and the timeout was raised to six minutes. OpenRouter reported $0.0174 used that
day for all the DeepSeek runs.

The same strict grader scored the four drafts blind, mixed with Luna's:

| Draft | Grounding | Craft | Distinct | Offer | Clean | Routine | Learning | Voice |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DeepSeek photo 1 | A- | B+ | B | A- | A | A | A- | B+ |
| DeepSeek photo 2 | A | A | B+ | C+ | A | A- | A- | B+ |
| DeepSeek agency 1 | A | A | A- | B- | A | A | A | B+ |
| DeepSeek agency 2 | A | A | B+ | B | A | A | A | A- |

- **Overall:** the four averaged about A-, level with Luna's four final samples. The grader put photo 1 level with
  Luna's best photography draft and agency 2 first of all the agency drafts.
- **Better than Luna:** craft (A in three of four) and the learning plan (A- to A).
  - The tallies count only what the posts ask for, once per person.
  - The next-week rules are concrete.
- **Same as Luna:** grounding, clean copy and routine, at A- to A. Every time estimate adds up and includes
  posting.
- **Weaker:**
  - **Offer:** varied from C+ to A-, as Luna's does. The model often pastes the brief's company sentence or
    drops it in without tying it to the post.
  - **Distinct value:** B to A-, because the third post re-runs the first post's setup.
  - **Voice:** B+ in three drafts, from long review notes, mixed "I" and "we", and re-asking about settled
    details.
  - **One factual slip:** black card behind a dark glaze.
- **For the competition engine:**
  - A deliverable used 6,200–7,200 output tokens on the photography case and about 10,500 on the agency case, at
    low effort.
  - It took up to 2.5 minutes on a slow host.
  - The client therefore needs room for at least 16,000 output tokens and a timeout of several minutes, or
    streaming.
  - Vultr's own throughput is untested.

### Iterating for both models

The owner then asked to iterate on the offer line and on repeated third posts. Each cycle ran two samples per case
on the stand-in and on Luna, within the $10 daily budget. The changes:
- **Cycle 11:** a worked example names an earlier tip instead of repeating its steps. The business is introduced
  once, near the end of a post, naming that post's problem. It reuses no run of more than four words from the
  company sentence, and the self-check covers both rules.
- **Cycle 13:** the offer must say which part of the business's supplied work removes the problem, and how. One
  example from an unrelated business shows the shape.
- **Cycle 15:**
  - Responses that mention the channel are a minimum, and the week's total off-post responses only an upper
    bound. The earlier wording had led models to call the total a minimum.
  - Posts keep one grammatical person and use short paragraphs.
  - Each post carries enough concrete steps to act on.

**Grading:** the long-running grader drifted stricter. On its scale, Luna seemed to drop after cycle 11. A fresh
grader then scored the committed and cycle 13 drafts together, and both models had improved. From then on, each
cycle got a fresh grader, with the previous cycle's drafts mixed in as anchors.

| Fresh grader, same batch | DeepSeek average | Luna average |
| --- | --- | --- |
| Committed text | 3.58 | 3.33 |
| Cycle 13 | 3.79 | 3.51 |
| Cycle 13 (second batch) | 3.76 | 3.38 |
| Cycle 15 | 3.79 | 3.41 |

Cycle 15 by criterion, on the second batch's scale:

| Model | Grounding | Craft | Distinct | Offer | Clean | Routine | Learning | Voice |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DeepSeek | 4.00 | 3.58 | 3.75 | 3.85 | 3.58 | 4.00 | 3.85 | 3.70 |
| Luna | 4.00 | 2.60 | 3.10 | 3.33 | 3.92 | 3.92 | 3.00 | 3.40 |

- **Offer:** across the cycles, the offer criterion rose from about B to A- for both models.
- **DeepSeek:** reaches A- on six of eight criteria. Craft and clean copy are at the B+/A- border; two drafts let
  the instructions' own wording into a post ("without repeating its steps").
- **Luna:** improved on the offer and the tally, but its craft stayed around B- in every cycle. Its posts are
  short and generic, and three rule changes did not fix that, which looks like a model limit.
- **Stopping:** averages moved only 0.03 from cycle 13 to cycle 15, so the iteration stopped there, as the owner
  had specified.

**Cost and time:** DeepSeek reasoned longer under the stricter self-checks.
- Cycle 13 and 15 drafts used 8,200–15,300 output tokens and took 100–290 seconds on the slow 4-bit host.
- One draft reached 15,256 of its 16,000 tokens, so the room was raised to 24,000.
- One request ended with a provider error after 797 tokens. It was billed $0 and replaced by a new sample.

## Usage

Twenty runs, 164,063 reported input/output tokens, 24–43 seconds each, with 596–2,292 reasoning tokens.
None was withheld.
- **The 31-run allowance:** the first six runs used its last six, totalling 220,397 reported tokens.
- **The owner's further instruction:** the other fourteen ran under the owner's instruction to cycle until
  confident, after the owner said heavy Luna use was fine.

The model checks and the later iteration added:
- one Sol run, whose usage is unknown;
- twelve more Luna runs, under the owner's "Yes, iterate";
- 22 OpenRouter requests. Two agency requests timed out with unknown outcomes, and one ended with a provider error
  billed $0. OpenRouter reported $0.040 used that day.
Actual credit charges for the Luna and Sol runs are unknown. Inputs, raw outputs, receipts, rubric and grades stay in the
ignored private pilot directory.

## Verification

- 56 Marketing, assignment and progress tests pass, including a new focused fact for the instruction changes.
- CSharpier is clean.
- No browser check or fixture depends on the instruction wording.
- The primary company install was republished at ff69ecf, which carries the final instructions and ADR 0166.
  - It was installed after a data backup.
  - Saved work, schedules, the model connection and the audit chain were unchanged afterwards.
  - The pilot install still runs the previous instructions.
- On 2026-10-29 the primary host stopped twice, once before the republish and once after it. Each time a
  "database is locked" error in the scheduler ended the host, and the startup task did not restart it.
  [ADR 0167](../adr/0167-a-locked-database-skips-a-scheduler-round.md) now logs such a round and tries it again;
  the primary install runs it from 655196a. [ADR 0168](../adr/0168-company-writers-queue-for-the-database.md)
  then measured the cause: the thirteen company schedulers sharing one SQLite file. It queued their writes and
  turned on SQLite's busy handler.
