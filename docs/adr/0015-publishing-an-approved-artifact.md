# 0015: Publishing an approved artifact

Status: Accepted (Phase 2B.5c2). Extends [0012](0012-company-publication.md).

## Context

Publications (ADR 0012) were sourced from a completed execution grant: a reviewed diff in a checkout. Porting the
Developer's Wiki drafts needs the same discipline for a different source, a draft the owner has approved. There is no
checkout and no execution: the reviewed thing is the artifact itself.

## Decision

- **Source.** A publication's source is either a grant (unchanged) or, with no grant, an approved artifact of a
  publishing skill's task. The reviewed digest is the artifact's own digest, so any change to the work refuses the
  publication. Gathered evidence (`action:*` artifacts) is never published. Each artifact has at most one live
  publication.
- **Skills.** `Publishes` is allowed on measured skills as well as execution skills. A measured skill's approved
  artifact is what gets published.
- **Steps.** `IEmployeeModule.ExecuteAsync(Propose, id)` takes the grant or, for such a skill, the artifact. Publish
  and Reconcile are unchanged.
- **Landed on a local call.** A destination may complete the publication on a local call, for example a Wiki that
  approves a proposal on submission. The module may then finish the publication as published, but only when every
  approved local call ran and a read confirms the result. Otherwise it stays failed, and the reason says what may be
  left at the destination.
- **The Wiki** (Developer, synthetic projects only):
  - the `wiki-read` action gathers a project's entries as evidence;
  - the measured `wiki-draft` skill drafts one entry as JSON; an owner-authored artifact on the same task works too,
    which lets you edit the draft;
  - the publication's one local call is the exact `wiki.contribute` proposal;
  - its one external call is the `wiki.publish` approval of that revision, bound after the contribution;
  - an entry being replaced must still be at the revision you read;
  - there is no background monitor and no retry, unlike the legacy Wiki publisher.
- **Request tokens.** The Wiki integration adds the session's request token itself when a caller supplies none. An
  approved call's input, and therefore its digest, then never depends on a per-session value.

## Consequences

- Other modules can publish an approved artifact the same way, for example a release note or a reply, by declaring
  the local and external calls.
- The legacy Wiki draft store, endpoints and automatic publication monitor retire with the legacy profile (2B.5j).
