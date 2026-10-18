# Phase 2B.5b1 report: the company Developer fixes pull requests

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

The second legacy Developer service is ported into the company workspace, as far as Core and the Developer module. A
Developer employee can fix a pull request in your checkout of its head. You review the diff, approve the publication,
and the fix is committed and pushed to the pull request's branch, once.

It works through a new general mechanism, publications. Any module can use it to send reviewed execution somewhere.

## Decision

[ADR 0012](../adr/0012-company-publication.md): publishing reviewed execution through exact calls.

A publication binds a completed execution's reviewed diff to the exact local calls that will commit it and, later,
to one external call. You approve it after approving the change. A new verifier, `company-publication`, then
authorizes each call once and in order, for that employee and task only. Core still knows nothing about git or
GitHub: the module supplies the calls and the summary.

## How it works

1. **Gather and approve evidence** for the pull request (`pr-evidence`, from 2B.5a).
2. **Assign a Pull request fix** (`pr-fix`) with that evidence and a brief, then grant the checkout of the PR head.
3. **Prepare.** The Developer checks that the live pull request, the evidence and the checkout are all at the same
   head, and that the checkout is clean. It then proposes the execution grant.
4. **Run.** Once you approve the grant, the fix runs and the diff is recorded for your review. The runner never
   commits.
5. **Propose the publication.** Before proposing, the Developer refuses unsafe paths and credential-like values. It
   then reads the branch: open, unprotected, not the default, at the reviewed head, and pushable by the signed-in
   account.
6. **Approve** the change's artifact, then the publication.
7. **Publish.** The Developer:
   1. re-reads the branch and the diff, which must still be exactly what you reviewed;
   2. stages exactly the reviewed files and checks the staged set;
   3. commits without repository hooks;
   4. checks that the commit sits directly on the reviewed head and holds exactly those files;
   5. pushes once with `--force-with-lease` on the reviewed head;
   6. confirms the pull request's head is the new commit.
8. **If the push isn't confirmed**, the publication is `unknown` and is never retried. **Reconcile** only reads the
   pull request, and marks it published if its head is the commit.

## What changed

- **Core:**
  - `CompanyPublications.cs`: a publication record, `publication.review` (approve or revoke at the exact digest),
    claim, bind, finish and confirm.
  - The `company-publication` verifier.
  - Recovery: an interrupted publication becomes failed or unknown.
  - Schema migration 4 adds `co_publications`.
  - Two artifact reads.
- **Developer:**
  - The `pr-fix` skill (Execution mode) and its instructions.
  - `CompanyPullRequestFix`: prepare, run, propose, publish and reconcile.
  - The execution bridge accepts `pr-fix` and can require an exact revision.
  - The evidence title is kept on one line, so the head the fix is bound to can't be spoofed by a title.

## Tests

`PullRequestFixTests` (5) use real git in a temporary repository, the FakeCodex runner and fake GitHub and push
handlers behind the real registry and verifier:

- **The full path:**
  - publication approval is refused before the change is approved, and at the wrong digest;
  - publishing makes one commit on the reviewed head holding exactly the fixed file;
  - one push, with the lease and the exact refspec;
  - every call is audited under the publication;
  - a second publish, or the same push again, is refused.
- **Authorization:**
  - nothing runs before the claim;
  - another task's toolbox, other files, `reset --hard` and a repeated add are refused;
  - no push is allowed before every local call ran and the push was bound;
  - a restart fails the publication without resuming it.
- **Changes after approval:** a moved or protected branch is refused before anything is consumed. A checkout edited
  after review fails the publication with no commit and no push.
- **Unknown outcomes:** a push that times out is unknown, stays unknown through a restart and is never retried.
  Reconciliation confirms it only once GitHub shows the commit.
- **Prepare:** refused when the pull request moved or the checkout is dirty.

A mutation check confirmed the authorization test fails if the verifier accepts any write. The v1 upgrade test now
also covers migration 4.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 167 passed in two consecutive runs (162 before, plus 5 new) |
| CSharpier, web typecheck and format check; Node tests | passed; 6 passed |
| Headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:**

- **The flow from the company UI.** Nothing in the host drives company execution yet; that is 2B.5b2.
- **A real push.** It would publish to GitHub. It needs your explicit go-ahead on a branch of yours, after 2B.5b2.
- **A real coding run.** Company execution has only the fixture runner. Its pilot limits (20 seconds, 5,000 tokens)
  are too small for real fixes; that is 2B.5b3.

## Decisions for you

- **Legacy safeguards not carried over.** The legacy fix also required proof that a test failed and then passed,
  and a model business-rule review. Here your diff review replaces both. They could return later as skills over the
  same evidence.
- **The approval window.** A publication must be approved and started within an hour of being proposed.
- **The model-reply guard** from the 2B.5a report still stands. A review that quotes "system prompt" is withheld.

## Next

- **2B.5b2:** host endpoints to prepare, run, propose, publish and reconcile, through `IEmployeeModule` so the host
  stays role-neutral. Approval buttons for the grant and the publication, and the diff review for every execution
  skill, not only `developer-work`.
- **2B.5b3:** a qualified live coding runner sized for real fixes, then the fix loop.
