# 0011: Employee actions gather evidence the owner reviews

Status: Accepted (Phase 2B.5)

## Context

Porting the Developer's legacy services into the company workspace needs a way for an employee to read external
systems, such as a pull request on GitHub. The company scheduler deliberately gives models no tools: a model sees
only admitted context and approved evidence. Letting models call tools would bypass the review that makes that
evidence trustworthy.

## Decision

- **What an action is.** An employee module declares actions in its manifest: an id, a name, a description and
  bounded input fields. Actions are deterministic, read-only code; they never call a model.
- **Read-only toolbox.** A module runs an action with a toolbox that never carries authority, so only Read tools can
  succeed. Every call is audited with the employee and task.
- **Running an action.** The owner starts it (`/api/company/actions`):
  1. The store records an `action` task in the `gathering` state. The scheduler never claims it.
  2. The host runs the action once. A retried request returns the recorded task without running it again, and
     startup recovery fails an interrupted action rather than replaying it.
  3. The output becomes an artifact with provenance `action:<id>` that waits for the owner's review at its exact
     digest.
- **Using the result.** Only an approved artifact can be attached as evidence to a skill. The model then receives it
  as untrusted data, like any other evidence.

## Consequences

- The first action is the Developer's `pr-evidence`: description, changed files, checks, reviews and review comments,
  read through `github.read`. The new measured `pr-review` skill drafts a review from it.
- More legacy services port the same way: deterministic reads as actions, and judgement as skills over approved
  evidence. Writes (pushes, merges, wiki publication) stay with execution skills and owner approvals, never with
  actions.
