# A goal ladder and weekly numbers for Marketing

The owner asked for a Marketing employee that works like a marketing manager on shift. It looks at the numbers each
morning, infers what to do from them and goes after the step that loses the most people, for an owner who knows
nothing about marketing. Owners rarely say more than "I want more money"; the employee has to work out the rest.

The design follows the owner's research on how strong marketing managers work. The research sits in the earlier
cockpit project, as `docs/research/ELITE_MARKETING_MANAGER_WORKFLOW.md` and `docs/EMPLOYEE_SHIFTS.md`. It describes
the loop sense, prioritize, create, align, launch, measure, decide, learn. It describes a daily signal scan and a
weekly rhythm. And it holds that one scorecard ranks all the work. The work is in three steps; this is step 1.
[ADR 0170](../adr/0170-goal-ladder-and-weekly-numbers.md) records the decisions.

## What the owner sees

**An offer on Home.** Once the Marketing employee is free and has delivered a first result, Home asks "What should
marketing grow?". One consent, in the same form as the first draft, has the employee write one private goal-ladder
draft. It works from the approved brief and up to three public pages of the confirmed website.

**Chip's proposal.** The draft has five parts:
- the number to grow;
- a new customer's first step;
- three to five steps, each with the weekly number that shows it;
- what to work on first and what to leave alone;
- notes for the owner.

The owner can change any part, then choose "Use this ladder". That saves it to the Marketing department context, which
every later Marketing request reads, and approves and completes the draft. A note warns when recurring work will wait
for review because the context changed.

**The ladder and its numbers.**
- Home then shows the goal, the steps as cards and the work to do first.
- A small form records a week's numbers for each step. Each step shows its share of the step before, as "1 in 100" or
  "21%".
- With two weeks recorded, Home names the step-to-step share that fell most, when it fell by a fifth or more.
- Code only compares. Why a number fell stays with the employee and the owner.

## A live run

The run used one fresh local company on the pinned Luna connection, on its own port and data folder. The owner's
walkthrough company on 5081 was left untouched. The business was hirezero.app, and the goal was exactly "I want more
money."

- **First draft.** For that goal, the first draft was one LinkedIn post. This is the gap the ladder addresses: a broad
  wish produced a single asset, with no view of what to grow.
- **The ladder.** It came back in under two minutes, with 8,906 reported tokens at high effort.
  - **Goal:** new paying customers a week. The ladder said this was a proposal, because pricing and the payment path
    were not supplied.
  - **First step:** choosing "Get started" on the website.
  - **Steps, five in all:** hear about HireZero, visit the website, choose Get started, begin using HireZero, become a
    paying customer. Each had a weekly number, with website analytics named where it is needed.
  - **Focus:** with no numbers, it said honestly that nothing yet showed where people were lost. It asked to count
    visits and Get started clicks first.
  - **Leave alone:** expanding marketing before knowing which step needs attention.
- **A side effect.** The same run opened a Google Analytics connect card under Needs you, so the employee asked for
  the numbers it said it needed.

On the live run the card at first showed raw Markdown asterisks. Ladder text is now rendered as Markdown. The run also
showed that the offer appeared while the first draft was still being written, when starting it would have been
refused, so the offer now waits until the employee is free.

## Checks

- C# formatting and the web typecheck, build and formatting pass.
- **xUnit:**
  - all 619 Company tests (5 skipped) and the 43 Core tests;
  - two new facts. One covers optional department fields and the ladder draft through the goal path. The other
    covers weekly numbers: replacement by version and the refusals.
- **Frontend unit tests:** a new test covers parsing the ladder, its steps, weeks, shares, the largest fall and the
  draft checks.
- **Browser smoke (new).** `goal-ladder-browser-smoke.mjs` uses fictional providers only. It covers a first draft,
  the offer with no run before consent, and one consent. It then checks the owner's edit and the kept department
  context, two weeks of numbers and the named fall, desktop and phone.
- **Existing suites:**
  - The goal-start, measured and walkthrough browser suites pass, as do the 13 Host tests, including the architecture
    tests.
  - The onboarding suite stops early, at a "Chat" rail button removed in 3514f66, before it reaches department context.
    It is one of the stale suites already listed for repair.
- **The department editor, by hand:** in the live-run company it labels the four ladder fields optional, and it saved
  a draft with one of them left blank.

## Not done yet

- Weekly numbers do not reach model requests yet. The morning shift (step 2) will read them, fill them from connected
  analytics where possible, and turn the focus into a queue of stepped work.
- Step 3, still to come, covers measuring, deciding, end-of-shift learning and the weekly rhythm.
- The installed copies on 5065 and 5066 are not updated. Applying a ladder there changes the real Marketing context,
  and its recurring work then waits for the owner's review.
