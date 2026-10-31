# 0172 - A shift's weekly rhythm, kept lessons and measured changes

Status: accepted, 2026-10-30. Builds on [ADR 0170](0170-goal-ladder-and-weekly-numbers.md) and
[ADR 0171](0171-an-owner-approved-shift.md).

## Context

Step 3 of the Marketing shift. The owner's research describes the loop sense, prioritize, create, align, launch,
measure, decide, learn. It describes a weekly rhythm, from Monday's review and priorities to Friday's results and
next week's plan. And it says decisions about a change should follow a rule set before the change. Steps 1 and 2 gave
Marketing a plan, weekly numbers and morning rounds. Three things were still missing: the rounds did not follow a
week, nothing the owner learned carried forward, and nothing judged whether a change worked.

## Decision

**Weekday shifts.** Recurring schedules gain a role-neutral `weekdays` cadence: Monday to Friday, with a weekend start
waiting for Monday. The shift card offers it by default ("Weekdays only"), and Recurring work offers it too. Daily and
weekly schedules are unchanged.

**A weekly rhythm.** The morning round follows the weekday in its scheduled time:
- Monday: review last week's numbers and set the week's priorities.
- Tuesday: customers and new creative.
- Wednesday: growth ideas and changes worth testing.
- Thursday: finish and prepare the week's content.
- Friday: results and lessons; ask for this week's numbers if they are missing, judge changes, plan next week.
- A weekend round, on a daily shift, stays light.

**Kept lessons.** On Fridays, and on other days only when the week taught something, a round adds '## Lessons'. That is
at most three one-sentence lessons, grounded in the numbers, recent work or the owner's answers.
- The card shows each one with Keep. Keeping one saves and approves one feedback memory record addressed to the
  employee. These are the existing owner-only memory commands.
- `EmployeeSkill.ShiftContext` requests now also carry `lessons`: the employee's eight newest current, approved
  feedback and decision records addressed to it. Keyword retrieval may miss them; a round should not. One Core
  instruction says to follow them as the owner's settled direction, not as proof of results.
- Lessons stay editable and revocable under Memory. Because schedules bind only the memory retrieval scope, keeping a
  lesson does not stop the shift.

**Changes judged by a fixed rule.** A Marketing draft the owner marks done counts as a change from that week. The
ladder card measures it by code, at the share of people who reach the focus step (the step too few people reach)
from the step before it:
- **Working:** the share rose by a fifth or more.
- **Not working:** the share fell by a fifth or more. The card suggests changing it back.
- **Unclear:** the share moved by less than that.
- **Too early:** there are not yet two weeks of numbers on each side of the change week. The card says from which week
  it can be judged.

The round's instructions state the same rule, so the employee explains the verdict instead of making its own. One week
of numbers never counts as working.

**Rounds are read, not approved.** A round's report is read on its own card, and only its jobs need the owner. The
snapshot now lists each role's shift skills, and a scheduled result of one counts as a ready draft, the same
presentation as other private drafts, instead of an item under Needs you. Home's results list leaves rounds out. The
approval controls still exist in the work view; nothing in the approval ledger changes. Other recurring results still
need review as before.

## Consequences

- No new table, migration or authority. Lessons are ordinary memory; the weekday cadence is a calendar rule inside the
  existing schedule bounds.
- A change is only as good as the owner's "done": a draft marked done but never used will be judged as if it went live.
  The card names the change and its week, so the owner can see what is being judged.
- Not yet:
  - checks during the day between rounds, which would need model work outside the shift's approval;
  - filling the numbers from connected analytics;
  - lessons drawn from an approved round report with an exact citation.
