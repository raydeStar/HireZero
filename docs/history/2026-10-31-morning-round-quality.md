# Improving the morning round's quality

The owner asked to improve the Marketing shift's morning rounds with their method:
- score drafts on a rubric;
- change the instructions;
- rerun;
- have a fresh blind grader regrade;
- cycle until every criterion reaches at least A-.

This ran eight cycles on the pinned Luna connection. The runs went through a private runner, which sends a real captured
morning-round request with its context replaced by a fixed scenario and the working tree's instructions. Every run was
marked before dispatch and none was replayed. The owner authorized 36 runs for cycles 1 to 6, then 18 more to try
drafting job 1 in its own request, then 6 for a full-brief test. All 60 were used. One was refused by Codex at its usage-limit check before anything
was sent.

## Rubric and scenarios

The rubric follows the owner's research on strong marketing managers. It has seven criteria:
- **evidence and accuracy:** reading the numbers right, inventing nothing;
- **prioritization:** the right focus and the weekday's role;
- **jobs:** written work aimed at the focus step;
- **draft quality;**
- **continuity and judgment:** moving waiting work forward and judging done changes by the fixed rule;
- **timeliness:** the dates that matter in the next 30 days;
- **clarity** for a non-marketer.

There were three scenarios, two samples each per cycle:
- **HireZero on a Monday.** A one-sentence brief, an approved ladder and no numbers.
- **A fictional bookshop on a Wednesday.** Three weeks of numbers, with comments falling from 34 of 900 people
  reached to 12 of 880. An approved gift guide was not yet posted, and a lesson had been kept.
- **A fictional photography studio on a Friday.** A homepage change done three weeks earlier that the fixed rule
  judges as working: 5.0% of visitors opened the contact page before the change and 7.7% after. This week's numbers
  were missing, and there was one kept lesson and one owner decision.

Each grader was a fresh agent, who saw shuffled, relabelled drafts and the previous cycle's drafts beside the new
ones. Grades are averaged with A = 4.0, A- = 3.7, B+ = 3.3, B = 3.0, B- = 2.7, C+ = 2.3. They are compared only within
the same batch.

## Cycles

| Batch | Compared | Overall | Weakest criterion |
| --- | --- | --- | --- |
| 2 | baseline vs cycle 2 | 2.79 → 3.40 | upcoming dates rose from 1.77 to 3.52 |
| 3 | cycle 2 vs cycle 3 | 3.23 → 3.03 | evidence and continuity fell |
| 4 | cycle 2 vs cycle 4 | 3.26 → 3.20 | no real change |
| 5 | cycle 2 vs cycle 5 | 3.13 → 3.34 | drafts still lowest, at 3.08 |
| 6 | cycle 5 vs cycle 6 | 3.40 → 3.27 | drafts fell from 3.27 to 2.60 |
| 7 | cycle 5 vs cycle 7 (split draft) | 3.32 → 3.14 | drafts fell from 2.88 to 2.65 |
| 8 | cycle 5 vs cycle 8 | 3.26 → 3.32 | continuity rose from 3.00 to 3.35 |

What each cycle changed and found:
1. **Cycle 2** was the biggest gain. Rounds now:
   - work out holidays and shopping days themselves instead of waiting to be told;
   - give counts with every share;
   - exclude the change week when judging a change;
   - aim jobs at what moves people to the focus step;
   - give the reader of the draft a concrete reason to act.
2. **Cycle 3** surfaced a definition bug from step 3. The rule said to judge the share moving on *from* the focus
   step, while every ladder, including the employee's own, names as focus the step too few people *reach*. The
   convention is now the same everywhere: the ladder's instructions and field help, the morning round's rule, and the
   Home card. The focus is the step too few people reach, judged by its share of the step before it. The card now
   finds the focus by the step it names first, not by any word inside it: "Order: few visitors order" no longer
   matches "Visit". The fixture and both browser smokes follow.
3. **Cycle 4** made shares easier to read and stopped new work duplicating approved work that was waiting. It
   measured no better than cycle 2.
4. **Cycle 5** moved the weekday duties out of prose and into the report's required format:
   - a "This week:" list of priorities on Monday;
   - a "Test:" line on Wednesday's first job;
   - on Friday, a question that asks for the missing numbers;
   - shares written as percentages, rising or falling by the share itself;
   - a self-check of the draft against its Ask.

   Monday and Wednesday now reliably do their jobs.
5. **Cycle 6** added more rules for the draft, a Friday "Change check:" line and stronger use of the season. The
   Friday verdict improved: both Friday drafts gave one, against one of two before. The drafts got worse, though, with
   businesses introducing themselves like ads and posts running over their length. Cycle 6 was reverted.

6. **Cycle 7** tested the owner's question of whether the draft would improve in its own request. The round left the
   draft out and wrote a richer Ask for job 1, and Core drafted job 1 in a second request with the Marketing draft
   skill, under the same shift approval. The drafts came back in 5 to 9 seconds but "safe but flat". They pasted the
   business's self-description into customer copy and scored lower than cycle 5's single request. A second request a
   morning also doubles the shift's use, so the split was reverted. The patch is kept with the private runner.
7. **Cycle 8** was cycle 5 plus only the Friday "Change check:" line from cycle 6. Every Friday draft that had the line
   gave a verdict on the done change, in cycle 6 and cycle 8 alike, against one of two without it. Continuity rose
   from 3.00 to 3.35, and nothing else fell beyond noise. It is kept.

## Result

The committed instructions are cycle 8's: cycle 5 plus the Friday "Change check:" line. In batch 8, cycle 8 averaged:

| Criterion | Average |
| --- | --- |
| Evidence and accuracy | 3.48 |
| Prioritization | 3.62 |
| Jobs | 3.22 |
| Draft quality | 2.88 |
| Continuity and judgment | 3.35 |
| Timeliness | 3.43 |
| Clarity | 3.28 |
| Overall | 3.32, about B+ |

Cycle 5, before the Friday line, averaged this in batch 6:

| Criterion | Average |
| --- | --- |
| Evidence and accuracy | 3.62 |
| Prioritization | 3.47 |
| Jobs | 3.63 |
| Draft quality | 3.27 |
| Continuity and judgment | 2.95 |
| Timeliness | 3.38 |
| Clarity | 3.52 |
| Overall | 3.40, about B+ |

The target was A- (3.7) on every criterion, and it is not met. Graders also differed by about a fifth of a grade
between batches for the same drafts, so single cycles near each other are within noise.

What is still holding it back:
- **Draft quality.** The draft stays around 2.6 to 2.9 whatever the structure: inline, with more rules, or in its own
  request. The graders' complaints are the same each time. Calls to action hedge or point at the page the reader is
  already on, copy restates a one-sentence brief, and lengths miss the Ask. The likeliest limit is the thin briefs in
  these scenarios. The next useful test is a round on a company with a full brief, such as the confirmed HireZero
  context, rather than more instruction wording.
- **Overlap with waiting work.** New work still sometimes repeats approved work that is waiting to be posted.
- **Business results on Friday.** Friday reports judge the share but rarely say the goal number itself moved, for
  example that enquiries doubled from 3 to 6 a week.

## A full brief (cycle 9)

The owner then asked whether a full brief lifts the drafts. Six more Luna runs used the committed cycle 8 text with
HireZero's full approved Marketing department context: product facts, audience, positioning, voice, approved claims
and the start route, with the goal ladder added. That context is about 3,900 characters, against one sentence before.
There were three rounds on a Monday and three on a Wednesday.

One fresh grader saw these next to the four thin-brief Monday rounds from cycles 5 and 8:

| Criterion | Monday, one-sentence brief (4) | Monday, full context (3) | Wednesday, full context (3) |
| --- | --- | --- | --- |
| Evidence and accuracy | 3.53 | 3.80 | 3.47 |
| Prioritization | 3.42 | 3.57 | 3.10 |
| Jobs | 2.90 | 3.33 | 3.10 |
| Draft quality | 2.72 | 3.13 | 3.00 |
| Continuity and judgment | 2.67 | 3.00 | 2.67 |
| Timeliness | 3.08 | 3.30 | 3.00 |
| Clarity | 3.08 | 3.33 | 3.03 |
| Overall | 3.06 | 3.35 | 3.05 |

The full brief lifts the same Monday round by about a third of a grade, the draft by about four tenths and the jobs by
the same. That supports the view that the drafts were held back by thin facts. It is still short of A-, and one of the
three Wednesday rounds was weak (C+).

The grader's remaining complaints are about what the round does, not the facts it has:
- **The waiting draft.** Every round says "review it" again. It does not say what the draft is, recommend approving,
  changing or holding it, or note how long it has waited.
- **Asking for numbers.** The ask is boilerplate: five counts, including people who heard of the business. It should
  ask for the two counts the focus step needs, say they come from website analytics, use the question to find out
  whether analytics can be connected, and say plainly that a test cannot be judged until those counts exist.
- **Placing the copy.** Homepage copy never says where it goes, what it replaces, or what the headline and button
  say. Some drafts also repeat claims the context says the homepage already carries.

These are the next changes to test.

No customer validation, and nothing about real usefulness, follows from these fictional scenarios.
