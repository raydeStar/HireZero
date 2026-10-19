# Phase 2B.5e1 report: coding-agent sessions (read-only) and one-call publications

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Steering a coding-agent session is the first ported feature that makes another agent act, in your real folder,
under that app's own permissions. Today it happens on one click, with no review. So 2B.5e is split:

- **2B.5e1 (this phase):** the parts that need no new decision from you: read-only session listing, and the Core
  mechanism steering and mail replies both need.
- **2B.5e2:** steering itself, waiting on the choices below.

This phase also fixed the Wiki's HTTP clients in the company profile (a separate commit).

## Decision

[ADR 0016](../adr/0016-publishing-one-approved-call.md): publishing one approved external call.

A publication may now have no local calls when its proposal carries the digest of its one external call. You
approve that exact call; binding accepts only it; it is authorized once and never retried. A publication with no
local calls can never be recorded as landed without its send.

## Session listing

- **Off until you opt in.** Put `{"apps": ["claude", "codex"]}` (either or both) in `agent-apps.json` in the company
  data root. Until then the integration is not active, and the overview's Integrations card says why.
- **List agent sessions** (the Developer's `agent-sessions` action, choosing all, claude or codex): it scans the
  enabled apps when you start it and lists up to 25 sessions as untrusted text, for your review.
  - Each session shows its title, folder name (never the full path), repository, branch, status and flags.
  - No transcript or log path is read or returned.
  - There is no watcher, no hooks and no session store in the company workspace.
- **Codex listing uses your installed Codex app,** as the legacy listing does. It is only used when you enable
  `codex`.

## What changed

- **Core:** publications can carry `External` (the approved external call) with zero local calls, and binding
  enforces it.
- **`HireZero.Tools.AgentApps`** (contracts only): `agentapps.status` and `agentapps.sessions.list`, both Read.
- **Integration:** `AgentAppsToolProvider` reuses the legacy session sources, scans only enabled apps, strips paths
  and enforces bounds. Codex is resolved lazily, only when listed.
- **Host:** registers the tools only when apps are enabled, and adds the agent-apps status to the integrations
  list.
- **Developer:** the `agent-sessions` action and the two Read tools.
- **Wiki fix:** the company Wiki now uses private session clients, so the company profile still has no general
  HTTP client factory with the Wiki pilot on. A host test covers it.

## Tests

- `AgentAppsToolProviderTests` (2): nothing is listed until an app is enabled (an unknown app counts as none); only
  enabled apps are scanned; folders are names only, so no full path or log path leaks; bounds and app filters are
  enforced.
- `Microsoft365CompanyTests`: the agent-sessions listing becomes reviewed evidence, an unknown app is refused, and
  the host reports agent-apps as not active, with its reason.
- `WikiPublicationTests`:
  - an external-only publication: refused without its digest; a different call refused at bind; the approved call
    authorized once; never recorded as landed without the send (mutation-checked);
  - the Wiki pilot adds no general HTTP client factory.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 214 passed in two consecutive runs |
| CSharpier, web typecheck and format check; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** listing real sessions. It needs `agent-apps.json` and your installed apps.

## Decisions for you, before steering (2B.5e2)

1. **Edits.** May a steer let the agent edit files? My proposal is read-only first: Claude in its default
   permission mode, a read-only Codex turn and no network, with edits as a later flag inside the approved call.
2. **Binary.** Steering has to use your installed agent app, not the pinned runtime, because it continues that
   app's own session. Accept that exception to pinning, with the command path set explicitly in `agent-apps.json`?
3. **Message text.** Should the session read include its recent messages, bounded and guarded? That would be new;
   the legacy code read metadata only. Without it, a steer is drafted from the listing and your brief.
4. **Starting sessions.** Leave out starting new sessions (the legacy Start), unless you want it.
