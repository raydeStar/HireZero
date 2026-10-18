# Phase 2B.5b2 report: the company host drives reviewed execution

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](ROADMAP.md).

Reviewed execution, and the Developer's pull request fix from 2B.5b1, can now be driven from the company workspace.
A job card for any execution skill walks you through each step:

1. grant a checkout;
2. prepare the exact proposal;
3. approve it;
4. run it once;
5. review the diff;
6. for skills that publish, propose, approve and publish once, or check an unknown outcome by reading.

The host stays role-neutral: it reaches the employee's module through one new contract method.

## What changed

- **Contract** (`Core/EmployeeModules.cs`):
  - `IEmployeeModule.ExecuteAsync(step, id, services, token)` runs one step: Prepare (task), Run and Propose (grant),
    Publish and Reconcile (publication). It receives `CompanyExecutionServices`: the store, the audited registry and
    an optional coding factory.
  - `EmployeeSkill.Publishes` marks an Execution skill whose result can be published; manifest validation refuses it
    on any other mode.
  - Core refuses a publication for a skill that doesn't publish.
- **Developer:** implements the steps. `developer-work` prepares and runs; `pr-fix` does every step. Without a
  qualified coding runner it still prepares, but refuses to run before anything starts.
- **Host** (`CompanyWorkspace.cs`):
  - `POST /api/company/executions/{step}/{id}` dispatches to the module of the employee the work belongs to.
  - Each step is single use in the store, so a repeated request is refused. An unexpected error returns a generic
    502; the store has already recorded the outcome.
  - The registry now has the publication verifier. It is still the only verifier, so any write outside an approved
    publication is refused.
  - A `fixtureCoding` parameter is used by tests. The live host has no coding runner yet.
- **Store:** task detail includes publications. The dashboard serves each role's `publishingSkills`.
- **Web** (`ExecutionSteps.tsx`):
  - The panel shows only the next step the saved state allows. Approvals go at the exact digest. Publish is offered
    only for an approved publication, and an unknown one offers only the read-only check.
  - The repository diff now shows for every execution skill, not just `developer-work`.
  - The blocked-task label now explains what execution needs instead of saying it is unavailable.

## Tests

- `PullRequestFixTests`, 2 new (7 in total):
  - **Through the host:** an unknown step is 404; run before approval is refused; prepare, run, propose and publish
    each succeed once and are refused when repeated; one push is made; task detail shows the published publication;
    the dashboard lists `pr-fix` as publishing.
  - **No coding runner:** the module prepares but refuses to run. The grant stays approved, with no run and no file
    written, and a publication can't be proposed.
- `tests/execution-steps.test.mjs` (6): each saved state offers exactly its next step, and approvals carry the exact
  digest. Only publishing skills with a non-empty completed diff offer a publication. Publish is offered only once
  approved, and an unknown publication offers only the check.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 169 passed in two consecutive runs (167 before, plus 2 new) |
| CSharpier, web typecheck, format check and build | passed |
| Node tests | 12 passed (6 before, plus 6 new) |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |
| Live company host (scratch data root) | a `pr-fix` task from the approved evidence shows the panel, starting at the checkout grant; prepare without a checkout is refused; an unknown step is 404 |

**Not verified:**

- **A run or publish in the live host.** There is no qualified coding runner, so a run is refused. A real push would
  need your go-ahead on a branch of yours.
- **The panel's later steps in a browser.** Only the first step was seen live; the rest is covered by the unit tests
  and the HTTP test.

## Next: 2B.5b3

A qualified live coding runner for company execution, sized for real fixes. The current pilot limits are 20 seconds
and 5,000 tokens. That needs:

- the pinned Codex runtime identity check that measured jobs already use;
- a sandbox scoped to the approved checkout;
- your decision on limits.

Then the fix loop.
