# 0170 - A goal ladder and weekly numbers

Status: accepted, 2026-10-30. Builds on [ADR 0164](0164-single-consent-goal-generation.md) and the department
context of [ADR 0041](0041-context-drafted-from-sources.md).

## Context

The owner wants the Marketing employee to work like a marketing manager on shift: look at the numbers each morning,
work out what they mean and go after the step that loses the most people. Owners who know nothing about marketing
rarely say more than "I want more money". Somebody has to infer what marketing should grow, which steps customers take
to get there, and where to start. The owner's own research on how strong marketing managers work begins every day
with one scorecard and a short list of decisions, and ranks work by what it adds to one main number. The research is
in the earlier cockpit project: `docs/research/ELITE_MARKETING_MANAGER_WORKFLOW.md`.

HireZero had neither piece. Department context described the business. Nothing held the goal or the steps, and the
numbers lived only in analytics read-outs the owner had to ask for.

This is the first of three steps toward a Marketing shift. The second is the morning shift and its queue of stepped
work. The third is measuring, deciding, end-of-shift learning and the weekly rhythm.

## Decision

**The goal ladder is optional department context.**
- Marketing declares four more department fields, all optional: `goal`, `start`, `steps` and `focus`.
  - `goal` is the one number to grow each week.
  - `start` is a new customer's first step.
  - `steps` lists the steps to that number, one per line, each with the number that shows it.
  - `focus` is the step to work on first, and what to leave alone for now.
- `OnboardingField.Optional` is a role-neutral flag. Core skips optional fields when it checks that saved context is
  complete. A value that is present must still be text.
- The department editor and the hiring form mark optional fields and save only the filled ones. Context drafts still
  ask for them. Existing approved context stays valid without them.
- Once approved, the ladder reaches every Marketing request through the existing department context, so ordinary
  drafts already see the focus. No role instructions changed, so no recurring schedule's fingerprint changed.

**Chip drafts it; the owner approves it.**
- A new measured Marketing skill, `marketing-goal-ladder`, returns Markdown under five fixed headings: Goal, First
  step, Steps, Focus and Notes for you. It is told to infer from a broad wish, use plain words with no marketing
  jargon, and invent no numbers, targets or results. It must call its focus a best guess until the numbers confirm it.
- It starts from a Home card through the one-consent goal path, for the existing active employee. That path saves
  nothing to the business facts or department context. It reads up to three public pages of a confirmed business
  website, as unverified references.
- Local checks look for the five sections, three to five steps with numbers, and something to leave alone.
- The card shows the draft. The owner can edit any of the four parts, then choose "Use this ladder". That one choice
  does three things: saves the edited parts as a new department-context version beside the existing values, approves
  that version, and approves and completes Chip's draft.
- The card warns that recurring work for the employee will wait for review, because its department context changed.

**Weekly numbers are a role-neutral Core record.**
- `reading.record` saves one week's numbers for an active employee: the week is named by its Monday, with 1 to 8
  named numbers.
  - Each number is from 0 to a trillion, with at most two decimals. Names are unique.
  - The week must have started (with a day's grace for time zones), and be at most two years old.
  - An approver or owner may record numbers.
- Recording a week again needs its current version and replaces its numbers.
- The snapshot carries each employee's latest 26 weeks. Core does not know what the numbers mean. The Marketing card
  names them after the ladder's steps.

**Code compares; judgment stays with Chip and the owner.**
- The card shows each step's number and the share of the step before it, as "1 in 100" or "25%".
- When two consecutive weeks have numbers, it names the step-to-step share that fell most since the week before. A
  share must fall by at least a fifth to count.
- The approved focus stays as written until the owner or Chip changes it.

## Consequences

- Migration 30 adds `co_readings`. The migration uses `CREATE TABLE IF NOT EXISTS`, so the existing schema-downgrade
  tests, which drop only the tables they know, still step forward.
- Weekly numbers do not enter model requests yet. The shift's morning step will read them in step 2.
- Numbers come only from the owner for now. Filling them from connected analytics belongs to the shift.
- A role can now put a card on Home through the `home-card` slot, the same way it already adds draft checks or a
  business setup.
- No publishing, sending, spending or automatic retry is added.
