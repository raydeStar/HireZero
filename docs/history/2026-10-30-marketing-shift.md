# Chip on shift: morning rounds

Step 2 of three toward a Marketing employee that works like a marketing manager on shift. Step 1 gave it a plan (the
[goal ladder](2026-10-30-goal-ladder.md)) and evidence (weekly numbers). This step has it act on them unasked: each
morning it looks, decides what matters and comes back with work.
[ADR 0171](../adr/0171-an-owner-approved-shift.md) records the decisions.

## What the owner sees

- **Put Chip on shift.** Once a goal ladder is approved, Home offers a shift. The owner picks:
  - how many mornings: 1 to 31, 14 by default;
  - whether to start today or tomorrow;
  - the time, 8:00 by default.

  One consent starts it. The card first says in plain words how many private reports that is, that nothing is posted,
  sent or bought, that nothing is retried, and that the shift can be paused or ended at any time. The exact run terms
  are one click away.
- **The morning round.** Each morning Chip reads the ladder, the weekly numbers and where its recent work stands. It
  comes back with:
  - what changed;
  - today's focus;
  - up to three jobs, each with why it helps, how many minutes it needs from the owner and a self-contained request;
  - the first job already drafted;
  - at most one question.
- **On Home.** While a shift exists, its latest round leads Home, right after the greeting. Job 1 opens its draft.
  Jobs 2 and 3 start with "Do it": one click, one private draft, in a conversation named after the job. A started job
  shows "Started · open it". The question has "Answer in chat".
- **Stepping work forward.** Each round sees what was asked, approved and done since, without the drafts themselves.
  So the next morning can take approved work a step further instead of repeating it.
- **Control.** Pause, Resume and End shift sit on the card, and the shift also appears under Recurring work. If the
  ladder or context changes, the shift stops, and the card offers a new one that ends the old one first.
- **After a shift.** A shift that has ended or stopped keeps its last round leading Home for a day and a half, with
  "Start a new shift" underneath.

## Two live rounds

These rounds ran in the same local test company as the ladder run, on the pinned Luna connection. The company used
hirezero.app with the goal "I want more money", an approved ladder and no weekly numbers yet. The owner's walkthrough
company on 5081 was left untouched.

**Round 1** took 18 seconds and 7,833 reported tokens.
- **What worked:**
  - it parsed cleanly;
  - it said honestly that there were no numbers, and which ones to record;
  - it saw that the ladder was done and that a draft was waiting, and didn't repeat them;
  - it flagged Thanksgiving and Black Friday as possible, not confirmed, reasons to post.
- **What didn't:**
  - Job 1 was a tracking template, which duplicates the numbers form on Home.
  - Job 2 asked Chip to report where the "Get started" link goes, which only the owner can check.

  The instructions now say:
  - every job is written work the employee can do itself;
  - checks only the owner can do go in the question or under what changed;
  - the numbers are never a tracking sheet;
  - with no numbers, choose the work most likely to help the focus step and call it a best guess.

**Round 2** ran after that change, on a new one-morning shift. That also exercised the "context changed" path: the
first shift stopped because its approved instructions changed, and the card ended it when the new one started. Round 2
took 21 seconds and 8,687 reported tokens.
- It proposed real work: clearer homepage copy for small business owners, already drafted, using only the approved
  description and the site's existing "Get started" wording.
- It flagged Small Business Saturday on November 28.
- It asked one useful question: does "new paying customers a week" fit how HireZero earns money?
- It was thin: one job instead of up to three, and a short draft, because the brief holds little beyond one sentence
  about the business.

The live runs also showed that a one-morning shift dropped off Home as soon as its schedule completed, taking its jobs
with it. That led to the day-and-a-half rule above.

## Checks

- **xUnit:** a new fact runs a morning round on the scheduler. The round carries the weekly numbers, recent work, the
  date and the shift instructions; a starter-plan schedule in the same company carries none of them. The schedule
  test class passes, 27 of 27.
- **Frontend unit tests:** a new test covers parsing the morning report and its jobs, the question, the next-round
  label, the checks and the job title, which matches the server's shortening of long goals.
- **Browser smoke (new).** `marketing-shift-browser-smoke.mjs` uses fictional providers and a fictional clock. It
  covers:
  - the ladder and two weeks of numbers;
  - one consent for a three-morning shift, with nothing run before the first morning;
  - the shift leading Home;
  - a round that read both weeks and the recent work;
  - two jobs, the question, and a one-click job with its own draft;
  - pause, on desktop and phone.
- **Existing browser suites:** the goal-ladder and goal-start suites pass.
- **Existing suites:**
  - the goal-ladder, goal-start, measured and walkthrough browser suites pass;
  - the 54 frontend unit tests pass;
  - the full Company, Core and Host .NET test projects pass.

## Not done yet

Step 3 is still to come. It covers:
- checks during the day between rounds;
- weekday-only shifts;
- filling the numbers from connected analytics;
- measuring a change against a rule set in advance;
- end-of-shift learning and the weekly rhythm.

The quality of the rounds also needs the usual blind-graded iteration. Round 2 shows the shape is right, but one job
and a thin brief are not yet the useful morning the owner wants. The installed copies on 5065 and 5066 are not
updated.
