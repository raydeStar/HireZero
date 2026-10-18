# Phase 2B.2 report: Developer services through tools

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

SemaForge, the Wiki pilot and Jev now sit behind integrations as audited tools. Every write is bound to what
authorizes it: an approved workflow, a fix's own runs, a reserved wiki draft, or your request while it is in flight.
No role opens an HTTP client or starts a process, and architecture tests enforce both. Along the way I found and
fixed a startup crash in the Developer host that predates this work.

## What changed

### Contracts and composition

- **New contract projects**
  - `HireZero.Tools.SemaForge`: `semaforge.read` and `semaforge.write`. GETs and the context-packet query are reads;
    every other method is a write.
  - `HireZero.Tools.Wiki`:
    - `wiki.read` covers GETs and pilot sign-in.
    - `wiki.contribute` is a proposal, and only in the contribution session.
    - `wiki.publish` is a review approval, and only in the publication session.
    - `wiki.origin` is read-only.
  - `HireZero.Tools.Jev`: `jev.classify` and `jev.status`. Classification is marked Read, but its description says
    the request text leaves for the classifier you configured with a key.
- **Core additions**
  - `ServiceCall` / `ServiceReply`.
  - `IToolProvider`.
  - `AddToolRegistry()`, which composes providers, verifiers and the audit.
  - `OwnerRequests`: a scope per owner request, valid only while that request is in flight. A task that outlives
    the request loses it. `UseOwnerRequestAuthority()` opens the scope.
  - The registry now treats a verifier that throws as a refusal.

### Integrations

- **`Integrations.SemaForge`** (new): loopback client, optional API key, requests start through
  `AgentEffectBoundary`, bounded responses.
- **`Integrations.Wiki`** (new):
  - Loopback-only origin.
  - One long-lived cookie handler per session.
  - Pilot, Origin and CSRF headers are added by the integration, never by callers.
- **`Integrations.Jev`**: now also provides the Jev tools. The Developer's duplicate key reader and HTTP client are
  gone.

Each handler checks the classification again, so a write sent through a read tool never runs.

### Developer

- **Adapters moved onto tools.** `GitHubFixCoordinator`, `LocalSources`, `GitHubChatClassifier`,
  `WikiProposalAdapter` and the "record in SemaForge" endpoint call tools instead of HTTP clients. Error and
  status mapping is unchanged.
- **New authorities**
  - `semaforge.write` under a workflow needs that workflow's repair approval to have been claimed.
  - `FixCancelVerifier`: a fix may cancel only its own scout and builder runs, in any state. Cancelling only stops
    work.
  - `WikiDraftAuthorityVerifier`:
    - a reserved draft may submit only its own proposal (matched by idempotency key);
    - while publishing, it may approve only its own revision.
  - Owner-endpoint actions fall back to the owner request in flight.
- **Manifest:** declares 18 tools in total.

### Host

- The Developer profile composes its tools with `AddToolRegistry()`: GitHub, sign-in, npm, SemaForge, Wiki and
  Jev. It opens owner-request scopes after the access check.

### Bug fixed: Developer host crashed on restart without OpenClaw

Booting the legacy host twice on the same data root crashed the second time, because no OpenClaw runtime was
installed:

- The owner cell's configuration sync started `node` without checking for it.
- Before Phase 0 it threw a `Win32Exception`. After Phase 0 it threw the clearer pin error, but it still crashed.

Now the sync is skipped until the pinned runtime is installed. The gateway start already refuses an unpinned
runtime, so nothing runs with stale configuration. A regression test fails without the fix and passes with it.

### Tests

New this phase:

- **`DeveloperServiceToolTests`** (15):
  - SemaForge and Wiki classification tables, including Wiki session mismatches;
  - a draft may propose only itself, only once reserved;
  - a draft may publish only its own revision, only while publishing;
  - a fix may cancel only its own runs.
- **`ToolRegistryTests`:** owner requests authorize only while in flight; a captured authority used after the
  request is refused.
- **`ArchitectureTests`:** the HTTP rule now forbids `HttpClient` and `IHttpClientFactory` in every role. Before,
  it only checked that the Developer was the one exception.
- **`OpenClawRuntimeTests`:** the startup regression above.

The existing workflow tests now run through the real SemaForge integration and verifiers, with the synthetic service
as transport. They exposed both missing scopes that the authorities above now cover: workflow cancellation and
owner-endpoint fix scoping.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` (clean rebuild) | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 146 passed, 0 failed (129 before, plus 17 new) |
| `node --test tests/*.test.mjs` | 6 passed |
| Headless Chrome, all 7 smoke tests | all passed |
| Launcher tests | 14 + 14 passed |
| Legacy Developer host booted twice on one fresh data root | both boots served `/api/session`, `/api/source-status` (SemaForge reported unavailable), `/api/github/reconnect` and `/api/wiki/connection` |
| `dotnet csharpier check .` | passed |

**Not verified:** a real SemaForge, Wiki pilot or Jev account. None is running on this machine, and contacting them
is outside routine checks. The synthetic SemaForge drives the full workflow tests through the real integration and
verifiers. For the Wiki pilot, the adapter's port is covered by classification and verifier tests, but no flow test
runs it end to end (none existed before either). Please exercise a wiki publication in your setup.

## Next: Phase 2B.3, Core vocabulary

Move out of Core:

- the `git` calls in `WorkingContext`;
- `tailscale whois`;
- GitHub and Azure DevOps remote parsing;
- PR fields in the work log and owner tasks;
- wiki notice kinds;
- Codex and Claude names.

After that, a test can forbid processes in Core too. Then come 2B.4 (one provider contract) and 2B.5 (port every
legacy feature as an optional integration and retire the legacy profiles).
