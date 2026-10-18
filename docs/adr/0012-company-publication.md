# 0012: Publishing reviewed execution through exact calls

Status: Accepted (Phase 2B.5b)

## Context

The company workspace can already run reviewed execution: the owner approves a single-use grant for one checkout,
the employee changes files there, and the owner reviews the resulting diff as an artifact. Porting the Developer's
pull request fix needs the next step, sending that change somewhere: committing it and pushing it to the pull
request's branch.

The legacy Developer publishes through role-specific authorities that know its workflow states. The company path
must stay role-neutral: Core cannot know about git, branches or GitHub.

## Decision

- **What a publication is.** A Core record that binds:
  - one completed execution grant and its reviewed diff digest;
  - the artifact holding that diff;
  - a summary the module writes (for example, the repository, the branch and the head being built on);
  - the exact digests of up to eight local write calls, in order (for the Developer: stage exactly the reviewed
    files, then commit them without repository hooks).
- **Approval.** The owner approves the publication at its exact digest, and only after approving the change's
  artifact. Approval is refused if the grant's review or the artifact changed, or if the task was canceled.
- **Authority.** Hosts register `CompanyStore.PublicationAuthority`, a verifier for authority kind
  `company-publication`. It authorizes a call only for the publication's own employee and task, while the change is
  unchanged:
  - a Write call only as the next proposed local call, after the module claims the publication, once;
  - one External call only after every local call ran and the module bound its exact digest (for the Developer:
    a push of the new commit, leased on the reviewed head), once.

  Each authorization is recorded before the call runs, so a call that fails is never authorized again.
- **Outcomes.**
  - A publication that stops before its external call is `failed`.
  - Once the external call was authorized, a publication is either `published`, if the module confirms it, or
    `unknown`. It is never retried.
  - An unknown publication can be confirmed only by a read-only reconciliation.
  - Startup recovery turns interrupted publications into `failed` or `unknown` the same way.

## Consequences

- **The Developer's `pr-fix` skill** (Execution mode) fixes a pull request from one approved `pr-evidence` artifact:
  - The checkout, the live pull request and the evidence must all be at the same head.
  - Before publishing, the module re-reads the diff and the branch: an unprotected feature branch at the reviewed
    head, which the signed-in account may push to.
  - The push uses `--force-with-lease` on the reviewed head, so a branch that moved is never overwritten.
- **Other modules** can publish the same way, for example a wiki page or a release note. They declare their own
  local and external calls and keep Core free of their vocabulary.
- **Difference from the legacy workflow.** The owner's diff review replaces the legacy proof that a test failed and
  then passed, and its business-rule model review. Those can return as skills over the same evidence.
