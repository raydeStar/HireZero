# Phase 2B.5a report: the company Developer reviews pull requests

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](ROADMAP.md).

This is the first legacy Developer service ported into the company workspace. A Developer employee can now gather a
pull request's evidence from GitHub. You review that evidence, and once approved the Developer drafts a review of
it, measured and owner-authorized like every other company job. It works through a new, general mechanism, employee
actions, which the remaining ports reuse.

## Decision

[ADR 0011](adr/0011-employee-actions.md): employee actions gather evidence that you review.

Models in the company workspace get no tools; that is what keeps their context reviewable. So reading an external
system is a deterministic, read-only action, and its output becomes an artifact that waits for your review. A skill
can use it only after you approve it.

## How it works

1. In the Developer's private conversation, open **Gather evidence**, choose **Gather PR evidence**, enter the
   repository (`owner/name`) and the PR number, and select **Run**.
2. The action reads the PR through `github.read`, which is audited with the employee and task:
   - title, author, state and branches;
   - description;
   - changed files;
   - checks;
   - reviews;
   - review comments.
3. The output is bounded and marked as untrusted third-party text. It is saved as an artifact (provenance
   `action:pr-evidence`) waiting for your review.
4. Once approved, attach it as evidence to a **Pull request review** task. That is the new measured skill: it
   reports findings by severity with files, lines and suggested fixes, plus risks and questions. It never claims to
   have run code, and it takes no action.

## What changed

- **Contract:**
  - The manifest gains `Actions` (id, name, description, bounded fields), validated: ids distinct from skill ids,
    fields present, bounds at most 4,000 characters.
  - `IEmployeeModule.RunActionAsync` runs an action.
  - `ReadOnly()` wraps a toolbox so calls never carry authority.
- **Company store** (`CompanyActions.cs`):
  - `action.start` validates the input against exactly the declared fields and records an `action` task in a new
    `gathering` state, which the scheduler never claims.
  - `CompleteAction` turns the output into a reviewable artifact.
  - `FailAction` records a failure.
  - Startup recovery fails interrupted actions instead of replaying them.
  - `task.create` refuses an action id as a skill.
- **Company host:**
  - `/api/company/actions` runs a newly started action once; a retried request returns the recorded task.
  - Unexpected errors fail the task without leaking internals.
  - The company registry now has the read-only GitHub CLI tools. It has no verifiers, so every write is refused.
  - The dashboard serves each role's actions.
- **Developer:**
  - `PullRequestEvidenceAction`;
  - the `pr-review` skill with its own instructions;
  - the action and skill declared in the manifest.
- **Web:** an **Gather evidence** panel in each employee's private conversation, driven by the manifest. It keeps one
  request id per attempt until the outcome is known, so a retry never runs an action twice.

## Tests

New tests in this phase:

- `EmployeeActionTests` (5):
  - **The full path:** an action is gathered, the scheduler never claims it, the artifact needs review, unapproved
    evidence is refused, and approved evidence feeds `pr-review`.
  - **Input:** it must be exactly the declared fields; unknown actions are refused; an action can't be created as a
    skill task.
  - **Failure:** failed and interrupted actions fail without replay.
  - **Evidence format:** read through `github.read` and marked untrusted; a bad repository is refused.
  - **The HTTP endpoint end to end:** a retried request doesn't rerun the action, and its reads are audited against
    the Developer employee.
- `EmployeeModuleTests`: three more malformed manifests:
  - an action colliding with a skill id;
  - an action without fields;
  - an unbounded field.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` (clean rebuild) | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 160 passed in two consecutive runs (152 before, plus 8 new) |
| Web typecheck, build and format check; Node tests | passed; 6 passed |
| Headless Chrome, all 7 smoke tests; launcher tests | all passed; 14 + 14 passed |

**Not verified:**

- **The panel in a browser.** The existing smoke tests don't cover it yet.
- **A real `gh pr view`.** It would read GitHub with your signed-in account; the tests use a fake `github.read`
  handler behind the real registry.

Please try it once in the company workspace against a PR of yours.

## Next: 2B.5b, the PR fix

A reviewed fix becomes an execution skill. It runs in an approved checkout under a single-use execution grant, then
commits through `git.write` and pushes through `git.push`, bound to your publication approval, as the legacy
workflow does today. The fix loop follows.
