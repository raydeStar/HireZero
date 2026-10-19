# Architecture

This describes HireZero as it is today, including the known gaps, and points at the target shape in the
[roadmap](ROADMAP.md). Decisions are recorded in [docs/adr](adr/).

## Layers

```
Host (the company workspace's composition root)
 ├── Roles            src/HireZero.Roles.{Developer,Marketing,Ceo}   web/src/roles/*
 ├── Integrations     src/HireZero.Integrations.{Git,GitHub,Node,SemaForge,Wiki,Jev,OpenRouter,OpenClaw,Codex,AgentApps,Microsoft365,AzureDevOps}
 ├── Tool contracts   src/HireZero.Tools.*   (referenced by roles and integrations)
 └── Core             src/HireZero.Core
```

The rule ([ADR 0001](adr/0001-three-layers.md)): Core knows no role or vendor; roles reach the world only through
tools that integrations provide ([ADR 0007](adr/0007-tool-registry.md)); integrations know no role. Tool contracts
(`src/HireZero.Tools.*`) sit between them. Project references follow it: roles reference Core and tool contracts,
integrations reference Core, tool contracts and (for AgentApps) Codex, and only Host references everything.

Architecture tests (`tests/HireZero.Host.Tests/ArchitectureTests.cs`) enforce the assembly references, that every
role project ships one discovered module, that every declared tool has a contract with the same risk, and that the
host and shared company UI name no role, and that company code names no retired legacy piece.

Core starts no process and roles open no HTTP client; architecture tests check both, in assembly metadata.

Known remaining vocabulary in Core: GitHub and Azure DevOps remote parsing (`GitRemote.cs`), behind its
origin-format port.

## Startup

`src/HireZero.Host/Program.cs` reads the owner and data root and starts the company workspace
(`CompanyWorkspace.cs`): departments, employees, conversations, tasks, artifacts, plans and meetings on port 5065,
with the optional pinned Codex connection (`--company-codex`) and live coding (`--company-coding`). It needs
`--company-local`, explicit local owner mode and an explicit data root, and refuses to start while
`HIREZERO_EXECUTION_HOLD=1`. Phase 2B.5j retired the developer, recovery and held profiles
([ADR 0019](adr/0019-retiring-the-legacy-profiles.md)). The host binds loopback only and ignores endpoint
configuration from args, appsettings or environment; listener plumbing lives in `LocalHosting.cs`.

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

Employees can also declare read-only actions ([ADR 0011](adr/0011-employee-actions.md)). For example, the Developer's
`pr-evidence` action gathers a PR through `github.read`. Its output is an artifact for owner review, and only once it
is approved can it be evidence for a skill such as `pr-review`. Models themselves get no tools.

Reviewed execution can be published ([ADR 0012](adr/0012-company-publication.md)). The module proposes the exact
local calls and one external call. The owner approves that publication after the change itself, and the
`company-publication` verifier authorizes each call once, in order. The Developer's `pr-fix` uses this to commit a
reviewed fix and push it to the pull request's branch with a lease on the reviewed head. A measured skill's approved
artifact can be published the same way ([ADR 0015](adr/0015-publishing-an-approved-artifact.md)): the Developer's
`wiki-draft` contributes and then publishes one Wiki entry. A role may also declare an action whose reading names
one exact step ([ADR 0017](adr/0017-publishing-a-step-from-an-action.md)), such as the Developer's `pr-step` (merge,
re-run, rebase), `postgres-setup` and `postgres-profile`.

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
| `npm.install-pinned`, `postgres.prepare-runner`, `postgres.save-profile` / `postgres.status`, `postgres.profile` | Write / Read | `Integrations.Node` |
| `agentapps.sessions.list`, `agentapps.sessions.activity`, `agentapps.session.check` / `agentapps.session.send` | Read / External | `Integrations.AgentApps` |
| `semaforge.read` / `semaforge.write` | Read / Write | `Integrations.SemaForge` |
| `wiki.read`, `wiki.origin` / `wiki.contribute` / `wiki.publish` | Read / Write / External | `Integrations.Wiki` |
| `jev.classify`, `jev.status` | Read (classification text leaves to the configured classifier) | `Integrations.Jev` |

Integrations contribute tools through `IToolProvider`. `AddToolRegistry()` composes the providers, the
verifiers and the audit.

The one authority in use is `company-publication`: an approved publication authorizes each of its exact calls once,
in order ([ADR 0012](adr/0012-company-publication.md)). Every other write is refused.

Roles neither start processes nor open HTTP clients, and Core starts none; architecture tests check this in the
assemblies' metadata.

## Model runtimes and routes

- **Routes**: `config/model-routes.json` maps purposes (`company.text`, `developer.escalation`, ...) to model ids
  and display names. It is embedded in Core and read through `ModelRoutes`; `HIREZERO_MODEL_ROUTES` can name an
  owner-maintained override file. No model id appears in source ([ADR 0008](adr/0008-model-routes.md)).
- **Codex** (`Integrations.Codex`): app-server over stdio; used for measured one-turn work and live coding runs. The
  executable is pinned in `runtime/codex` and by a reviewed bundle identity hash
  ([ADR 0004](adr/0004-pinned-agent-runtimes.md), [ADR 0014](adr/0014-pinned-codex-runtime.md)).

- **Provider contract**: `IModelProvider` and `IMeasuredModelProvider` (`Core/ModelProviders.cs`,
  [ADR 0010](adr/0010-model-provider-contract.md)).
  - The company scheduler admits each call with exact input accounting, reserves budget and dispatches under the
    provider gate. It watches authority while the call runs, drains on cancellation and holds the provider when
    termination or usage is unconfirmed.
  - Implementations: `CompanyCodexConnection` (measured), `ScopedCompanyText` and `UnavailableModelProvider`. Adding
    a vendor: [ADDING_A_MODEL_PROVIDER.md](ADDING_A_MODEL_PROVIDER.md).

## Storage

One SQLite database per data root (`workspace.sqlite`), accessed with ADO.NET. The company schema uses versioned,
checksummed migrations (`Core/CompanyDatabase.cs`); modules and integrations version their own tables through
`ModuleStorage` ([ADR 0018](adr/0018-module-storage-migrations.md)). Small JSON files in the data root hold optional
integration settings (`agent-apps.json`, `azure-devops.json`, `openrouter.json`).

## Frontend

React 19 + Vite, built into the Host's `wwwroot`. `web/src/main.tsx` renders the company workspace
(`web/src/company`); the shared API client and preferences are in `web/src/lib`. Employee UI lives under
`web/src/roles/<id>/company.tsx` and contributes to the company workspace through manifest-declared slots.

## Tests

xUnit v3 on Microsoft.Testing.Platform ([ADR 0005](adr/0005-offline-checks-on-xunit.md)). Each historical check
group is one test; new tests should be ordinary focused facts (see `tests/HireZero.Company.Tests/
PullRequestStepTests.cs`). The company test executable also hosts the fixtures used by the headless Chrome smoke
tests in `tests/*-browser-smoke.mjs`.
