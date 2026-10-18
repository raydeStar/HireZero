# 0017: Publishing a step from an action

Status: Accepted. Extends [0011](0011-employee-actions.md), [0015](0015-publishing-an-approved-artifact.md) and
[0016](0016-publishing-one-approved-call.md).

## Context

Gathered evidence (an action's result) is never published: it holds third-party text, and publishing it would let that
text become an effect. Some steps, though, are fully determined by a live reading and need no drafting:

- merging a pull request whose checks passed;
- re-running a workflow's failed jobs;
- asking a dependency bot to rebase its branch.

Routing them through a model-drafted skill would add a model call and an approval, and gain nothing, because the call
cannot come from the model anyway: it is rebuilt from the reading.

## Decision

- **A publishing action.** A role may declare an action with `Publishes`. Its reviewed result names one exact external
  step, worded by the module, not copied from third-party text. The result of any other action stays unpublishable,
  and Core checks only the declared flag.
- **One approved external call.** It is published as in [0016](0016-publishing-one-approved-call.md):
  - zero local calls;
  - the call's digest is part of the proposal the owner approves;
  - it is authorized once and consumed before it runs;
  - an unconfirmed outcome is unknown and never retried, and is reconciled only by reading.
- **Live re-check before the claim.** Before claiming, the module reads the live state again and refuses unless it
  still yields the approved step. A refusal leaves the publication approved, with nothing sent.
- **Pull-request steps (Developer `pr-step`):**
  - A merge requires an open, non-draft pull request that GitHub reports as clean. Its head checks must have passed,
    and so must the base branch's checks. The call names the reviewed head commit, so GitHub refuses it if the
    branch moves.
  - Requiring a green base branch makes merges sequential: the next merge waits until the previous one's checks pass.

## Consequences

A dependency-bump queue can be worked through the company workspace: read a step, approve it, publish it once. Each
merge, re-run or rebase request is a separate approval. Other external steps derived from a reading, such as closing
an issue, can use the same shape; each needs its own decision about what the step may do.
