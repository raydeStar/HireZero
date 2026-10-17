# Phase 1 report: one employee module contract

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](ROADMAP.md).

Every employee is now one `IEmployeeModule` with a manifest, discovered at startup. The company workspace, the
hiring flow and the employee UI are driven by manifests, and architecture tests enforce the rules. Adding an
employee no longer touches Host, Core or shared web code ([how-to](ADDING_AN_EMPLOYEE.md)).

## Decisions

Recorded as [ADR 0002](adr/0002-employee-module-contract.md) (now Accepted) and the new
[ADR 0006](adr/0006-compiled-in-module-discovery.md):

- **Discovery: compiled in.** Role projects are built with the host through a wildcard reference and found by an
  assembly attribute. A runtime plugin folder was rejected for now: loading third-party code into the process
  that holds approvals, budgets and credentials would bypass every gate. Third-party employees need a sandboxed
  extension model first (for example out-of-process modules over MCP).
- **Legacy data: no migration.** Company mode keeps refusing a legacy `workspace.sqlite`; there was no Phase 1
  reason to change that.
- **Legacy Developer features: not decided yet.** Their port depends on Phase 2 tools, so the choice of which to
  keep moves to Phase 2 (listed in the roadmap).

## What changed

### Contract (`src/HireZero.Core/EmployeeModules.cs`)

- `IEmployeeModule` with an `EmployeeManifest`:
  - identity, summary and versions (contract and module);
  - base and per-skill instructions;
  - skills with modes (`Text`, `Measured`, `Execution`) and structured input validation;
  - tools with risk levels (`Read`, `Write`, `External`, `Spend`);
  - KPIs and onboarding copy;
  - capabilities (workspace claims, company overview), UI slots and display order.
- `PlatformSkills` names the four skills the company orchestrates; Core's string literals for them are gone.
- `EmployeeModuleCatalog` discovers and validates modules: unique and well-formed ids, supported contract
  version, non-empty skills and onboarding fields, tool id format, and no workspace claims without an execution
  skill.
- `CompanyRole` is now a projection built by Core (`CompanyRole.From`); roles no longer author it.

### Roles

- `MarketingModule`, `CeoModule` and `DeveloperModule` replace `CompanyMarketing`, `CompanyCeo` and
  `CompanyDeveloper`. Instructions, skills and onboarding fields are unchanged, so existing instruction digests
  and saved work still match.
- The manifests declare KPIs, Developer's two checkout tools and UI slots. Marketing contributes the campaign
  brief editor, CEO the plan guidance.
- Developer and Marketing expose their legacy `IRole` through `LegacyRole`, so the old profile keeps working.

### Removed

About 1,200 lines were deleted:
- `Core/Employees.cs`, `Core/EmployeeEndpoints.cs` and `Host/EmployeeRegistration.cs` (the unused
  `EmployeeDefinition` path and its store, workers and endpoints);
- `Integrations.OpenClaw/UnqualifiedEmployeeGateway.cs`;
- Marketing's dead skill classes;
- the three `web/src/roles/*/onboarding.ts` copies of the hiring text.

The two helpers still in use moved to `ContentGuard.Digest` and `ContentGuard.Bounded`.

### Host

- `HireZero.Host.csproj` references `../HireZero.Roles.*/HireZero.Roles.*.csproj`.
- `HostEmployees` discovers modules once. The company profile builds its roles from them, and the legacy profiles
  take their hireable roles from `LegacyRole`. No hand-written role list remains.

### Web

- The dashboard's `roles` now carry each manifest's summary, hiring copy, tools, KPIs and UI slots.
- The hiring wizard reads suggestions from the snapshot. Its default role is the first by display order (still
  Marketing).
- `web/src/roles/company.ts` collects `web/src/roles/<id>/company.tsx` with `import.meta.glob` and renders a
  contribution only in slots the manifest declares.
- The conversation composer handles any structured skill input generically.
- The "Prepare delegation plan" button now keys off the `company-plan` skill instead of `roleId === "ceo"`.
- `tsconfig.json` gained Vite client types.

### Tests

- `ArchitectureTests` (6 tests) check that:
  - Core references no role or integration;
  - integrations reference no role;
  - roles reference only Core;
  - every role project ships exactly one discovered module;
  - the company profile and the shared company UI name no role.
- `EmployeeModuleTests` (10 cases) cover ordering, duplicate ids, seven malformed manifests and the company
  projection.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 86 passed, 0 failed (70 before, plus 16 new) |
| `node --test tests/*.test.mjs` | 6 passed |
| Headless Chrome, all 7 smoke tests (onboarding, company, chat, reply, review, measured, handoff) | all passed (see note) |
| Launcher tests | 14 + 14 passed |
| Web `typecheck`, `build`, `format:check`; `dotnet csharpier check .` | passed |

Note: `conversation-reply-browser-smoke` failed once when run directly after another smoke test, then passed
three runs in a row on its own. It looks like a port or timing flake between back-to-back fixture hosts rather
than a regression, but the failure output was not captured.

Not verified: live model calls (out of scope for routine checks) and a hand-run of the UI beyond the scripted
browser smokes.

## Moved to Phase 2

Each of these needs Developer's git and GitHub work to go through integration tools first:

- porting Developer's legacy services onto skills;
- module-owned storage migrations;
- merging the Developer panels into the company UI;
- retiring `IRole`, `roles.json`, the Developer, Recovery and Held profiles and `web/src/shell`.

## Next: Phase 2, restore the layers

1. **Tool registry** in Core: typed, named tools with the manifest's risk levels, wired into the approval and
   budget gates. A module may only call the tools its manifest declares.
2. **Integration projects**: move git and GitHub calls out of `Roles.Developer` and Core into
   `Integrations.Git` / `Integrations.GitHub`, exposed as tools. Remove PR and wiki vocabulary from Core.
3. **One model provider**: replace `IModelGateway`, `ICompanyModel`, `IEmployeeModelGateway` (now deleted) and
   `ICodingRunner` with one abstraction. Routing per employee and skill comes from configuration, and no model id
   appears in source.
4. **Port Developer and retire the legacy profiles**, after deciding which legacy-only features move into the
   company workspace.
5. **Fix the reply-smoke flake**: give each browser smoke its own port, or wait for the previous host to exit.
