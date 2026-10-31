# The shift's weekly rhythm, kept lessons and measured changes

Step 3 of the Marketing shift. Step 1 gave Marketing a [goal ladder](2026-10-30-goal-ladder.md) and weekly numbers;
step 2 put it on a [shift of morning rounds](2026-10-30-marketing-shift.md). This step closes the owner's research
loop: measure, decide, learn, inside a weekly rhythm.
[ADR 0172](../adr/0172-shift-rhythm-lessons-and-measured-changes.md) records the decisions.

## What the owner sees

- **Weekday shifts.** The shift form has "Weekdays only", on by default. Rounds run Monday to Friday, and a weekend
  start waits for Monday. Recurring work offers the same cadence.
- **A weekly rhythm.** Each round follows its weekday:
  - Monday: review and priorities;
  - Tuesday: customers and creative;
  - Wednesday: growth ideas and tests;
  - Thursday: finishing the week's content;
  - Friday: results, lessons and next week, asking for the week's numbers if they are missing.
- **Lessons.** A Friday round, or any round that learned something, ends with up to three one-sentence lessons. Each
  has Keep. A kept lesson becomes approved memory addressed to the employee and reaches every later round. It is
  editable under Memory, and keeping it does not stop the shift.
- **Changes being measured.** On the ladder card, each Marketing draft the owner marked done is a change from that
  week, judged by code at the share of people reaching the focus step from the step before it:
  - **Working:** the share rose by a fifth or more.
  - **Not working:** it fell by a fifth or more, with a suggestion to change it back.
  - **No clear change:** it moved by less than that.
  - **Too early:** until two weeks of numbers exist on each side, with the week from which it can be judged.

  The round's instructions carry the same rule, so the employee explains the verdict rather than inventing one.
- **Rounds are read, not approved.** A morning round no longer sits under Needs you or in Your results. It is read on
  its card and counted with the drafts ready to read. Only its jobs need the owner.

## A live Friday round

One round ran on a one-morning weekday shift in the same local test company: hirezero.app, the goal "I want more
money", an approved ladder and no numbers yet. It took 19 seconds and 8,770 reported tokens.

- **It followed Friday's role.** It asked for the week's five counts as its one question. It said no change had been
  marked done, so there was nothing to judge. It ended with a lesson grounded in what it saw: the ladder is complete,
  but without weekly counts the homepage step remains a best guess.
- **It kept to written work.** Its one job was homepage introduction copy, drafted from the approved description, with
  no tracking sheet and no owner-only checks.
- **It missed a date.** It said no customer-relevant dates had been "supplied" and left out Small Business Saturday,
  which round 2 of step 2 had flagged. The instruction asks for dates that matter in the next 30 days, but the model
  read it as dates someone supplied. Wording for the next quality pass.

In the real app the card showed the lesson with Keep, the question and Start a new shift. That run is also where the
rounds were seen piling up under Needs you, which led to the change above.

## Checks

- **xUnit:**
  - a weekday-cadence fact: a Friday start, Monday to Friday, then the next Monday, a weekend start, and an unknown
    cadence refused;
  - a lessons fact: two kept records reach a weekday round in the right form, while an unapproved one and a project
    note do not, and keeping them leaves the shift current.
- **Frontend unit tests:** lessons parsing, and the change rule for working, not working, unclear, too early, and a
  first step with no step before it.
- **The shift browser smoke, extended.** It now covers a weekday cadence by default and keeping a lesson. It then
  checks that the next weekday round reads the lesson, and that a finished job appears as a change, too early to
  judge.
- **Existing suites:**
  - the goal-ladder, goal-start, measured and walkthrough browser suites pass;
  - the 54 frontend unit tests pass;
  - the Company, Core and Host .NET projects pass, with 622, 43 and 13 tests.
- **One stale suite:** the schedules browser suite stops at "Split chat remains an explicit choice". Since the studio
  round ([61b6fbc](2026-10-30-studio-ux.md)), chat and work split side by side only on windows at least 1,600 pixels
  wide, and this suite's window is narrower. The failure is in a chat step, before any scheduling this step changed.
  It joins the stale suites listed for repair.

## Not done yet

- **Checks during the day between rounds.** They would add model work beyond the shift's approval.
- **Filling the numbers from connected analytics.** No company has analytics connected yet.
- **Tuning the rounds' quality.** One job and thin drafts come from a one-sentence brief. The upcoming-dates wording
  needs fixing, and the usual blind-graded iteration still has to happen.

## Installed

At the owner's "Alright let's do it" on 2026-10-31, both local installs moved to 2e9c54c through the usual backup
routine:
- Only Core, the Marketing role and their symbols changed, and the web app was replaced whole. The host and every
  other assembly kept their installed files. Each install verified 181 files.
- All 20 companies kept their work, schedules and model connections, and every audit chain is intact. Home terms are
  unchanged. The weekly numbers table was added, empty, on startup.
- Marketing now offers the goal ladder and the morning round, and installed Home shows the ladder offer. No ladder is
  approved and no shift is on, so the November pilot schedule still matches its approval.
- No model calls were made. Backups, receipts and the old apps are kept privately. Going back to the old app would
  also need the data backup restored, because the new table is created on startup.
