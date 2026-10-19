# Phase 2B.1 report: Developer processes through tools

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Every process the Developer used to start (git, the GitHub CLI and npm) now runs as a declared, audited tool
behind a risk gate. Pushes, merges and review requests are bound to the specific approved workflow or fix loop.
Roles no longer reference `System.Diagnostics.Process`, and an architecture test enforces that.

## Decision

[ADR 0009](../adr/0009-legacy-features-are-optional-integrations.md): you chose to keep every legacy-only feature:

- Microsoft 365 intake
- coding-agent session steering
- Azure DevOps
- Jev triage
- tailnet multi-user access
- the daily brief

Because your home and work setups differ, each becomes an optional integration that activates only when its
configuration is present.

## What changed

### Tool contracts

- `HireZero.Tools.Git` adds `git.read` (Read), `git.write` (Write) and `git.push` (External).
  - `GitCommands.Classify` maps a command line to its tool, skipping leading `-c` pairs.
  - Read: `rev-parse`, `status`, `diff`, `ls-files`, `ls-tree`, `cat-file`, `log`, `show`, `remote get-url`,
    `config --get` (refused if any write flag is present) and `worktree list`.
  - Write: `fetch`, `clone`, `worktree add`, `reset`, `clean`, `add`, `commit`.
  - Push: `push`.
  - Anything else is refused.
- `HireZero.Tools.GitHub` adds `github.read` (Read) and `github.write` (External).
  - Read: explicit-`GET` REST calls with no fields, GraphQL queries containing no `mutation`, `search prs`,
    `pr view` and `pr list`, and `run view`.
  - Write: REST `POST`, `PUT`, `PATCH` and `DELETE`.
  - It also adds `github.sign-in` (External) and `github.sign-in-status` (Read).
- `HireZero.Tools.Node` adds `npm.install-pinned` (Write): one package at an exact version.
- Core adds `ToolFailureException` (stable kinds such as `signed-out`, `clone-failed`, `too-large`, `failed`,
  `unavailable`, `refused`) and `OwnerActionApprovals`. The latter covers owner-clicked actions: the owner-only
  endpoint approves one exact call, which is consumed once.

### Integrations

- `HireZero.Integrations.GitHub`:
  - `GitHubCli` is the hardened runner, moved from the Developer unchanged: no prompts, no hooks or fsmonitor,
    no ext or file protocols, bounded output, process start through `AgentEffectBoundary`, and signed-out
    detection.
  - Each handler re-checks the classification, so a write sent through a read tool never runs.
  - `GitCloneTransportPolicy` moved here (isolated clone transport).
  - `GitHubSignIn` is the browser device-code sign-in.
- `HireZero.Integrations.Node`: `NpmInstaller` refuses ranges, tags and missing directories.

### Developer

- `GitHubReviewClient.CommandAsync` classifies each command and invokes the matching tool through the Developer's
  toolbox. Writes carry the ambient `DeveloperAuthority`; tool failures map back to the same exceptions and GitHub
  sign-in health as before. The protected seam is unchanged, so every existing fixture keeps working.
- `DeveloperAuthority`: approved workflows enter their scope inside `InScope`, and a fix loop does the same when it
  requests a Codex review.
- `WorkflowAuthorityVerifier`:
  - `git.write` needs the workflow's repair or publication approval to have been claimed.
  - `git.push` needs the workflow to be `pushing` its own approved commit to `refs/heads/...` after the publication
    claim.
  - `github.write` allows only a `PUT` merge of the workflow's own pull request while it is `merge-pending`.
  - Canceled, revoked, scope-changed, merged and read-only workflows authorize nothing.
- `LoopAuthorityVerifier`: an active loop may post exactly `@codex review` on its own pull request, and nothing
  else.
- `GitHubReconnect` and `TrustedPostgres` call the sign-in and npm tools with owner-action approvals.
- The manifest declares all 13 tools with their contract risks.

### Host

- **Developer profile:** builds the registry with the GitHub, sign-in and npm tools. The verifiers come from the
  hired roles plus owner actions, and the audit is kept in memory for this profile.
- **Recovery profile:** has a registry with no verifiers, so every write tool is refused.

### Tests

New tests in this phase:

- `DeveloperToolTests` (27): the real client and integration, with only the process replaced, against a real
  approval store.
  - Reads need no authority.
  - Writes outside a scope are refused and never run.
  - Claimed approvals authorize local writes.
  - Only the approved commit is pushed, and only while pushing.
  - Only the workflow's own pull request is merged.
  - The loop may only request a Codex review.
  - The integration refuses misrouted commands.
  - Classification tables for git and gh.
- `ToolRegistryTests`: owner-action approvals cover one exact call, once.
- `ArchitectureTests`:
  - roles start no processes (checked in assembly metadata);
  - an HTTP ratchet allows only the Developer to use HTTP directly until 2B.2.
- `GitCloneTransportChecks` now expects the integration's refusal type.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 129 passed, 0 failed (99 before, plus 30 new) |
| `node --test tests/*.test.mjs` | 6 passed |
| Headless Chrome, all 7 smoke tests | all passed |
| Launcher tests | 14 + 14 passed |
| `dotnet csharpier check .` | passed |

**Not verified:** a real push, merge, review request, sign-in or npm install. These need your GitHub account and
network, and are outside routine checks. The fixtures run the real classification, gate, verifiers and error
mapping, with only the process replaced.

**Risk to watch:** if a real legacy flow performs a write in a state the verifiers don't expect, it is refused
rather than run. The refusal is audited and the workflow shows the error. Tell me if you see one and I'll widen the
rule for that state deliberately.

## Next: Phase 2B.2

Move SemaForge, Wiki (session sign-in), Jev and the local context sources into integrations as typed tools. Then
the HTTP ratchet becomes "no role opens an HTTP client". After that come Core vocabulary (2B.3), the single provider
contract (2B.4), and porting every legacy feature as an optional integration and retiring the legacy profiles
(2B.5).
