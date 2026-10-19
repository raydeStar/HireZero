# Phase 2B.5c1 report: request preparation

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

The legacy Developer's request preparation is now a company skill. `request-prep` is a measured skill over approved
evidence: an approved `pr-evidence` artifact, a wiki read (2B.5c2) or a brief you wrote yourself. Like `pr-review`,
it runs only when you authorize one job, and its answer is an artifact you review.

## Choices made (each reversible)

- **Plain text, not JSON.** The reply has four sections: stage, blocker, next action and at most five questions.
  This keeps Core free of a role-specific output validator. The legacy instructions were carried over, minus the
  JSON format.
- **No preparation store.** Your approved artifact is the record, and the company task state replaces the legacy
  stage board. The board and its consent leasing retire with the legacy profile (2B.5j).
- **Trusted Postgres moves to the live-coding track.** It is part of the coding environment and only coding runs
  use it. Its install downloads a package and runs its install scripts, so it stays an owner-approved install bound
  to the exact package, never an action. Recorded in the roadmap.

## Tests

`EmployeeActionTests.ApprovedEvidenceFeedsRequestPreparation`: approved evidence feeds a `request-prep` task, the
skill is measured, and the role routes its own instructions.

## Verification

See the 2B.5c1 commit. The full .NET suite, Node tests and format checks passed.
