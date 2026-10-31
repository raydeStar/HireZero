# 0171 - An owner-approved shift

Status: accepted, 2026-10-30. Builds on [ADR 0058](0058-bounded-recurring-work.md),
[ADR 0093](0093-recurring-measured-deliverables.md), [ADR 0153](0153-reviewed-scheduled-time-context.md) and
[ADR 0170](0170-goal-ladder-and-weekly-numbers.md).

## Context

The owner wants the Marketing employee to be on shift: while it is on, it looks for work and comes back with it,
for an owner who knows nothing about marketing. The owner's research on strong marketing managers describes the
start of their day as a scan of the signals, a choice of the day's one to three decisions, and then the most useful
work. ADR 0170 gave Marketing a plan (the goal ladder) and evidence (weekly numbers). Nothing yet acted on them
unasked.

HireZero already runs finite recurring work under one exact owner approval. A schedule holds 1 to 31 occurrences,
binds its instructions, context, model and allowance, and stops when any of them change. Each occurrence is one
private model request. A shift fits that frame, so it adds no new kind of standing authority.

## Decision

**A shift is a recurring schedule of morning rounds.**
- Starting a shift saves and approves one daily schedule of Marketing's new measured skill `marketing-morning`. It
  runs at a local time the owner picks, for 1 to 31 mornings (14 by default), with one round a morning and the
  occurrence time included.
- The Home card shows this in plain words before the one consent: how many private reports in all, nothing posted,
  sent or bought, no automatic retry, pause or end at any time. The exact run terms stay one click away.
- Pause, resume and end are the schedule's own owner commands. The schedule also appears under Recurring work.
- A changed ladder or department context stops future rounds, as it does for any schedule. The card then offers a
  new shift, ending the old one first.

**A round reads the plan, the numbers and recent work.**
- `EmployeeSkill.ShiftContext` is a role-neutral flag. A request for such a skill also carries `scorecard`, the
  employee's latest eight weeks of recorded numbers, oldest first. It also carries `recentWork`: the employee's twelve
  most recent requests, each with its title, the kind of work, where it stands and when it last moved.
- Core adds one instruction for these fields: a missing number is unknown, a cause is only a guess, and unfinished
  work should move forward rather than repeat.
- `recentWork` carries no result bodies. ADR 0093 keeps earlier recurring results out of later requests, and that
  still holds. Each round knows what was asked, approved and done, so the next step follows: for example, once
  headline options are approved, the next round can propose putting one live.
- Other skills' requests are unchanged.

**A round comes back with jobs.**
- `marketing-morning` returns Markdown under six fixed headings: what I looked at, what changed, today's focus, jobs
  for today, a draft for job 1, and one question.
- There are one to three jobs. Each has a reason tied to the focus step, the owner's time in minutes, and a
  self-contained request. The first job comes back already drafted in the same request.
- The round may suggest moving the ladder's focus when the numbers point elsewhere. It does not change the plan.
- Local checks look for the sections and for complete jobs.

**The next job starts with one click.**
- "Do it" on job 2 or 3 is the owner's consent to one private draft. It runs through the same one-consent goal path
  as Home, as an ordinary Marketing draft in its own conversation named after the job.
- It is not covered by the shift's approval. Each one is a separate owner-started request, shown with its provider
  and with no publishing.
- A job already started shows "Started" and opens its work.

**Home leads with the shift.** A new role-neutral `home-lead` slot places a card right after Home's greeting. Marketing
uses it only while a shift exists, so the latest morning round leads Home. A shift that has ended or stopped keeps
its last round there for 36 hours, with "Start a new shift" beneath it. Otherwise Home keeps results first, then the
ladder and the offer to start a shift.

## Consequences

- No new authority, table or migration. A shift is bounded by the existing schedule rules: 31 occurrences at most, ten
  active schedules, review again when anything it was approved with changes, and no replay of an uncertain run.
- A shift is one model request a morning, plus any jobs the owner starts.
- A round that falls while the employee is busy with another request is handled like any recurring occurrence.
- Each round's report needs the owner's review like any result, so it also appears under Needs you.
- Not yet:
  - checks during the day between rounds;
  - weekday-only shifts;
  - filling the numbers from connected analytics;
  - measuring a change against a rule set in advance;
  - end-of-shift learning and the weekly rhythm.

  These are step 3.
