# Architecture

This describes HireZero as it is today, including the known gaps, and points at the target shape in the
[roadmap](ROADMAP.md). Decisions are recorded in [docs/adr](adr/).

## Layers

```
Host (composition roots, one per startup profile)
 ├── Roles          src/HireZero.Roles.{Developer,Marketing,Ceo}   web/src/roles/*
 ├── Integrations   src/HireZero.Integrations.{OpenClaw,Codex,AgentApps,Microsoft365,AzureDevOps,Jev}
 └── Core           src/HireZero.Core
```

The rule ([ADR 0001](adr/0001-three-layers.md)): Core knows no role or vendor; roles reach the world only through
integrations; integrations know no role. Project references already follow it: roles and integrations reference
only Core, and only Host references everything.

Known violations, scheduled for Phase 2:

- Core contains Developer vocabulary and behaviour: the coding-runner contract (`Coding.cs`), GitHub/Azure DevOps
  remote parsing (`GitRemote.cs`), `git` process calls (`WorkingContext.cs`), PR fields in the work log, wiki
  message kinds, and a model routing record that hard-codes OpenAI routes on the Codex runtime
  (`CompanyContracts.cs`).
- `Roles.Developer` calls `gh`, `git`, `npm` and HTTP APIs directly instead of through integration projects.

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

Three employee definitions coexist today; Phase 1 replaces them with one module contract
([ADR 0002](adr/0002-employee-module-contract.md)):

- `CompanyRole` (`Core/CompanyContracts.cs`): used by the company workspace. Id, instructions, skill names,
  onboarding fields, measured and execution skills.
- `IRole` (`Core/Roles.cs`): used by the Developer profile. Registers services and endpoints for a hired role.
- `EmployeeDefinition` / `IEmployeeSkill` (`Core/Employees.cs`): wired by `EmployeeRegistration.AddEmployees` but
  not called at runtime.

## Safety model

See [ADR 0003](adr/0003-owner-approval-safety-model.md). In short: exact-version owner approval for anything
consequential, single-use execution grants, budget reservations that stay held when usage is unknown, and no
automatic replay of uncertain outcomes.

## Model runtimes

- **OpenClaw** (`Integrations.OpenClaw`): a local gateway exposing chat completions; used by the Developer and
  Recovery profiles. Its version is pinned in `runtime/openclaw` and verified before any process starts
  ([ADR 0004](adr/0004-pinned-agent-runtimes.md)).
- **Codex** (`Integrations.Codex`): app-server over stdio; used by the company workspace for measured one-turn work
  and by Developer coding runs. The executable is pinned by a reviewed bundle identity hash.

Four overlapping model contracts exist (`IModelGateway`, `ICompanyModel`, `IEmployeeModelGateway`,
`ICodingRunner`); Phase 2 replaces them with one provider abstraction.

## Storage

One SQLite database per data root (`workspace.sqlite`), accessed with ADO.NET. The company workspace uses
versioned, checksummed migrations (`Core/CompanyDatabase.cs`); older stores create tables on construction and
alter them ad hoc. Small JSON files in the data root hold role hiring, access, brief, intake and sharing settings.

## Frontend

React 19 + Vite, built into the Host's `wwwroot`. `web/src/main.tsx` renders the company workspace
(`web/src/company`) when the session reports company mode, otherwise the legacy Developer shell
(`web/src/shell`). Employee-specific panels live under `web/src/roles`; only the Developer panels are registered
today. Phase 1 merges everything into the company workspace.

## Tests

xUnit v3 on Microsoft.Testing.Platform ([ADR 0005](adr/0005-offline-checks-on-xunit.md)). Each historical check
group is one test; new tests should be ordinary focused facts (see `tests/HireZero.Integrations.Tests/
OpenClawRuntimeTests.cs`). The company test executable also hosts the fixtures used by the headless Chrome smoke
tests in `tests/*-browser-smoke.mjs`.
