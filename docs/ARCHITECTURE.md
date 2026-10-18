# Architecture

This describes HireZero as it is today, including the known gaps, and points at the target shape in the
[roadmap](ROADMAP.md). Decisions are recorded in [docs/adr](adr/).

## Layers

```
Host (composition roots, one per startup profile)
 ├── Roles            src/HireZero.Roles.{Developer,Marketing,Ceo}   web/src/roles/*
 ├── Integrations     src/HireZero.Integrations.{Git,GitHub,Node,SemaForge,Wiki,Jev,Tailscale,OpenClaw,Codex,AgentApps,Microsoft365,AzureDevOps}
 ├── Tool contracts   src/HireZero.Tools.{Git,GitHub,Node,SemaForge,Wiki,Jev}   (referenced by roles and integrations)
 └── Core             src/HireZero.Core
```

The rule ([ADR 0001](adr/0001-three-layers.md)): Core knows no role or vendor; roles reach the world only through
tools that integrations provide ([ADR 0007](adr/0007-tool-registry.md)); integrations know no role. Tool contracts
(`src/HireZero.Tools.*`) sit between them. Project references follow it: roles reference Core and tool contracts,
integrations reference Core, tool contracts and (for AgentApps) Codex, and only Host references everything.

Architecture tests (`tests/HireZero.Host.Tests/ArchitectureTests.cs`) enforce the assembly references, that every
role project ships one discovered module, that every declared tool has a contract with the same risk, and that the
company profile and shared company UI name no role.

Core starts no process and roles open no HTTP client; architecture tests check both, in assembly metadata.

Known remaining vocabulary in Core, all in legacy-only features that move out in Phase 2B.5: work-log PR fields,
owner-task PR classification, thread-title URL rules, wiki notice kinds, Codex and Claude session names, and
GitHub/Azure DevOps remote parsing used for boss sharing (`GitRemote.cs`).

## Startup profiles

`src/HireZero.Host/Program.cs` reads the owner and data root, then selects exactly one profile. Each profile is its
own composition root, so a restricted profile never constructs the services it must not use. Shared listener and
request-boundary plumbing lives in `LocalHosting.cs`.

| Profile | Selected by | Composition root | Notes |
| --- | --- | --- | --- |
| Company | `--company-local` | `CompanyWorkspace.cs` | Departments, employees, conversations, tasks, artifacts, plans, meetings. Port 5065. Optional pinned Codex connection (`--company-codex`). |
| Recovery | `HIREZERO_DISCOVERY_ONLY=1` | `RecoveryWorkspace.cs` | Developer UI with fresh-message text chat and read-only PR discovery. |
| Held | `HIREZERO_EXECUTION_HOLD=1` | `HeldWorkspace.cs` | Status only; every mutation refused. |
| Developer | default | `DeveloperWorkspace.cs` | The original single-agent workspace with roles hired through `roles.json`. Retired in Phase 1. |

All profiles bind loopback only and ignore endpoint configuration from args, appsettings or environment. The
Developer profile can additionally listen on a tailnet address and identify callers through Tailscale.

## Employees

Each kind of employee is one `IEmployeeModule` (`Core/EmployeeModules.cs`) in its own `src/HireZero.Roles.*`
assembly ([ADR 0002](adr/0002-employee-module-contract.md)). Its manifest declares identity, instructions, skills
and their modes, tools, KPIs, onboarding copy, capabilities and UI slots. Host references every role project with a
wildcard and `EmployeeModuleCatalog.Discover` finds the modules at startup
([ADR 0006](adr/0006-compiled-in-module-discovery.md)).

- The company workspace projects each module into its internal `CompanyRole` (`CompanyRole.From`) and serves the
  manifest's hiring copy, tools, KPIs and UI slots to the web.
- Platform skills that the company orchestrates (`chat`, `meeting-turn`, `meeting-chair`, `company-plan`) are
  named by `PlatformSkills`; all other skills are opaque to Core.
- `IRole` (`Core/Roles.cs`) remains only for the legacy Developer and Recovery profiles; a module exposes it as
  `LegacyRole`. It is removed with those profiles in Phase 2.

On the web, `web/src/roles/company.ts` collects every `web/src/roles/<id>/company.tsx` and renders a contribution
only where the module's manifest declares the slot (`skill-input:<skill>` for structured input editors, named
panels such as `plan-guidance`).

How to add one: [ADDING_AN_EMPLOYEE.md](ADDING_AN_EMPLOYEE.md).

## Safety model

See [ADR 0003](adr/0003-owner-approval-safety-model.md). In short: exact-version owner approval for anything
consequential, single-use execution grants, budget reservations that stay held when usage is unknown, and no
automatic replay of uncertain outcomes.

## Tools

Employees reach external systems through tools ([ADR 0007](adr/0007-tool-registry.md)).

- **Contracts** (`ToolContract<TInput, TOutput>`) live in `src/HireZero.Tools.*`: ids, risk levels and records,
  no implementation. Roles and integrations reference them; they reference only Core.
- **Integrations** register handlers in the `ToolRegistry` (for example `GitToolRegistration.AddGitTools`).
- **Roles** call `IToolbox.InvokeAsync`, obtained with `ToolRegistry.For(manifest)`. The call is refused unless
  the manifest declares the tool with the contract's risk; `Write`, `External` and `Spend` tools need an authority
  that a registered `IToolAuthorityVerifier` accepts for that exact input.
- **Audit**: every call, allowed, refused or failed, is recorded with caller, risk, input digest, authority and
  outcome. The company workspace stores them in `co_tool_calls` and serves them at `/api/company/tool-calls`.

Today's tools:

| Tools | Risk | Integration |
| --- | --- | --- |
| `git.checkout-inspect`, `git.checkout-review` | Read | `Integrations.Git` |
| `git.read`, `github.read`, `github.sign-in-status` | Read | `Integrations.GitHub` |
| `git.write` | Write | `Integrations.GitHub` |
| `git.push`, `github.write`, `github.sign-in` | External | `Integrations.GitHub` |
| `npm.install-pinned` | Write | `Integrations.Node` |
| `semaforge.read` / `semaforge.write` | Read / Write | `Integrations.SemaForge` |
| `wiki.read`, `wiki.origin` / `wiki.contribute` / `wiki.publish` | Read / Write / External | `Integrations.Wiki` |
| `jev.classify`, `jev.status` | Read (classification text leaves to the configured classifier) | `Integrations.Jev` |

Integrations contribute tools through `IToolProvider`. `AddToolRegistry()` composes the providers, the
verifiers and the audit.

Authorities in use:

- `developer-workflow`: an approved PR workflow in scope. It is checked against the workflow's claimed approval
  and its own commit, branch and pull request.
- `developer-loop`: an active fix loop's Codex review request.
- `developer-fix-cancel`: a fix cancelling its own SemaForge runs. This is always allowed, because cancelling only
  stops work.
- `developer-wiki-draft`: a reserved draft submitting its own proposal, or approving its own revision while
  publishing.
- `owner-request`: the owner's HTTP request, valid only while that request is in flight.
- `owner-action`: the owner's click, approving one exact call.

Roles neither start processes nor open HTTP clients, and Core starts none; architecture tests check this in the
assemblies' metadata. Platform infrastructure that is not an employee capability, such as identifying tailnet callers
(`Integrations.Tailscale`), is an integration behind a Core port rather than a tool.

## Model runtimes and routes

- **Routes**: `config/model-routes.json` maps purposes (`company.text`, `developer.escalation`, ...) to model ids
  and display names. It is embedded in Core and read through `ModelRoutes`; `HIREZERO_MODEL_ROUTES` can name an
  owner-maintained override file. No model id appears in source ([ADR 0008](adr/0008-model-routes.md)).
- **OpenClaw** (`Integrations.OpenClaw`): a local gateway exposing chat completions; used by the Developer and
  Recovery profiles. Its version is pinned in `runtime/openclaw` and verified before any process starts
  ([ADR 0004](adr/0004-pinned-agent-runtimes.md)).
- **Codex** (`Integrations.Codex`): app-server over stdio; used by the company workspace for measured one-turn work
  and by Developer coding runs. The executable is pinned by a reviewed bundle identity hash.

- **Provider contract**: `IModelProvider` and `IMeasuredModelProvider` (`Core/ModelProviders.cs`,
  [ADR 0010](adr/0010-model-provider-contract.md)).
  - The company scheduler admits each call with exact input accounting, reserves budget and dispatches under the
    provider gate. It watches authority while the call runs, drains on cancellation and holds the provider when
    termination or usage is unconfirmed.
  - Implementations: `CompanyCodexConnection` (measured), `ScopedCompanyText` and `UnavailableModelProvider`. Adding
    a vendor: [ADDING_A_MODEL_PROVIDER.md](ADDING_A_MODEL_PROVIDER.md).
- **Legacy contracts**: `IModelGateway` (OpenClaw text) and `ICodingRunner` (Codex coding runs) serve the Developer
  profile only and retire with it in Phase 2B.5. Their usage goes to the budget ledger, which flags anything the
  provider didn't report.

## Storage

One SQLite database per data root (`workspace.sqlite`), accessed with ADO.NET. The company workspace uses
versioned, checksummed migrations (`Core/CompanyDatabase.cs`); older stores create tables on construction and
alter them ad hoc. Small JSON files in the data root hold role hiring, access, brief, intake and sharing settings.

## Frontend

React 19 + Vite, built into the Host's `wwwroot`. `web/src/main.tsx` renders the company workspace
(`web/src/company`) when the session reports company mode, otherwise the legacy Developer shell
(`web/src/shell`). Employee UI lives under `web/src/roles/<id>`: `company.tsx` contributes to the company
workspace through manifest-declared slots, and the Developer folder also holds the legacy shell's panels
(registered in `web/src/roles/index.ts`). Phase 2 merges the remaining Developer panels into the company workspace.

## Tests

xUnit v3 on Microsoft.Testing.Platform ([ADR 0005](adr/0005-offline-checks-on-xunit.md)). Each historical check
group is one test; new tests should be ordinary focused facts (see `tests/HireZero.Integrations.Tests/
OpenClawRuntimeTests.cs`). The company test executable also hosts the fixtures used by the headless Chrome smoke
tests in `tests/*-browser-smoke.mjs`.
