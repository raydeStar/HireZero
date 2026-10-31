# Improving the morning round's quality

The owner asked to improve the Marketing shift's morning rounds with their method:
- score drafts on a rubric;
- change the instructions;
- rerun;
- have a fresh blind grader regrade;
- cycle until every criterion reaches at least A-.

This ran six cycles on the pinned Luna connection. The runs went through a private runner, which sends a real captured
morning-round request with its context replaced by a fixed scenario and the working tree's instructions. Every run was
marked before dispatch and none was replayed. The owner authorized 36 runs; all 36 were used, and one of them was
refused by Codex at its usage-limit check before anything was sent.

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

## Result

The committed instructions are cycle 5's, unchanged: the working text's digest matches the cycle 5 requests. In the
final batch, cycle 5 averaged:

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
- **Draft quality.** One request writes the whole report and the finished draft for job 1. The draft is consistently
  the weakest part, and cycle 6 showed that piling on rules makes it worse.
- **Friday's verdict.** One of the two cycle 5 Friday drafts never judged the done change. Cycle 6's "Change check:"
  line fixed that; it is the next change to test on its own.
- **Overlap with waiting work.** New work still sometimes repeats approved work that is waiting to be posted.

No customer validation, and nothing about real usefulness, follows from these fictional scenarios.
