# Phase 2B.5c2 report: Wiki drafts

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

The legacy Developer's Wiki drafts are now a company feature. You gather a synthetic Wiki project's knowledge,
review it, have the Developer draft one entry (or write the draft yourself), approve the draft, then approve and
publish it once. It works through a small generalization of publications: an approved artifact can now be the thing
published, not only a code change.

## Decision

[ADR 0015](../adr/0015-publishing-an-approved-artifact.md): publishing an approved artifact.

## How it works

1. **Read Wiki knowledge** (`wiki-read` action, synthetic projects only). It lists the project's entries with their
   revisions, marked as untrusted, as an artifact for your review.
2. **Wiki draft** (`wiki-draft`, measured), with that approved read as evidence. The draft is JSON with the legacy
   fields. You can also save your own draft on the task as an owner-authored artifact.
3. **Approve the draft, then Propose publication.** The Developer:
   - checks the draft: valid, no conflict or missing information, no credential, email or instruction-like text;
   - re-reads the project;
   - proposes the exact contribution as the publication's one local call.

   An entry being replaced must still be at the revision you read.
4. **Approve the publication, then Publish once.**
   - The proposal goes through `wiki.contribute`.
   - The Developer checks that the Wiki's review queue holds exactly your draft.
   - The approval goes through `wiki.publish`, bound after the contribution.
   - A read confirms the new revision is current.

   If the Wiki publishes on submission, no approval call is made, and the publication is recorded as published once
   a read confirms it.
5. **If the outcome is unknown,** it is never retried. "Check whether it was published" only reads.

## What changed

- **Core:**
  - A publication's source can be an approved artifact. Its own digest is what you reviewed, and gathered evidence
    is never published.
  - `Publishes` is allowed on measured skills.
  - A publication can finish as published when the destination completed it on the local call and a read confirms
    it.
- **Developer:**
  - `CompanyWiki` provides the `wiki-read` action, the `wiki-draft` skill and its instructions, and the publication.
  - The legacy target parsing and draft screen are shared, not copied.
  - The pull request fix refuses a publication that isn't its own.
- **Wiki integration:** adds the session's request token itself when a caller supplies none, so an approved call's
  digest doesn't depend on a per-session value. If the session has no token, nothing is sent.
- **Host:** registers the Wiki tools in the company workspace only when `HIREZERO_SYNTHETIC_WIKI=1`. Propose
  accepts an artifact as well as a grant.
- **Web:** a Publication panel on publishing skills that aren't execution skills, sharing the exact-digest controls
  with the execution panel. Publication results are now labelled "Result" rather than "Commit".

## Differences from the legacy Wiki publisher

- Nothing is published automatically. Each publication waits for your approval of the draft and then of the
  publication.
- The legacy monitor re-submitted uncertain drafts every three seconds. There is no monitor now, and an uncertain
  outcome is never replayed (AGENTS.md).
- The idempotency key is `hirezero:<artifact id>`, and the approval note no longer mentions the legacy workspace.

## Tests

- `WikiPublicationTests` (6), against a fictional stateful Wiki behind the real registry and publication verifier:
  - **The full path:** reads are audited as reads; Publish is refused before approval; then there is exactly one
    contribution and one approval, both under the publication; a repeat is refused.
  - **Replacing an entry:** refused, with nothing contributed and the approval kept, when someone else updated the
    entry after your read; it works once the entry is back at the read revision.
  - **Refused:** conflicting drafts, an email address in the body, invalid content, publishing gathered evidence
    (through the module and through Core), and non-synthetic projects.
  - **Unknown approval:** a timed-out approval is unknown, stays unknown through a restart, and is confirmed only by
    a read; nothing is sent twice.
  - **Publishes on submission:** recorded as published, with no approval call.
  - **Revoked draft:** cannot be published.
- `WikiToolProviderTests` (3): the session token is fetched when absent; a caller's token is sent as is; a session
  without a token sends nothing.
- `EmployeeModuleTests` (2 more): `Publishes` on a text skill is refused; a measured skill may publish.
- `execution-steps.test.mjs` (2 more): only an approved draft, not evidence, offers Propose; an artifact publication
  uses the same controls.
- **Mutation check:** removing the published-on-submission outcome fails its test.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 199 passed in two consecutive runs |
| CSharpier, web typecheck, format check and build; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** a run against the real synthetic Wiki pilot. It needs the pilot running on loopback with
`HIREZERO_SYNTHETIC_WIKI=1`, and its exact response shapes are taken from the legacy adapter.

## Choices made (each reversible)

- Wiki stays synthetic-only, as in the legacy profile.
- `wiki-draft` is measured, so it runs only on your one-job authorization.
- You may write or edit the draft yourself (an owner-authored artifact).
- If the Wiki publishes on submission, the publication is recorded as published once confirmed. The other option
  was to refuse such a Wiki outright.

## Next

2B.5d onward: each remaining legacy-only feature as an optional integration (Microsoft 365 intake, coding-agent
steering, Azure DevOps, Jev triage, tailnet access, daily brief). Live coding step 2 is waiting on you.
