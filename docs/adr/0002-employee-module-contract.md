# 0002: One employee module contract

Status: Accepted (Phase 1)

## Context

Three overlapping employee definitions existed:

- `IRole` (`src/HireZero.Core/Roles.cs`), hired through `roles.json`, used by the legacy Developer host.
- `EmployeeDefinition` / `IEmployeeSkill` (`src/HireZero.Core/Employees.cs`), wired by
  `EmployeeRegistration.AddEmployees` but never called at runtime.
- `CompanyRole` (`src/HireZero.Core/CompanyContracts.cs`), used by the company workspace.

Adding an employee meant writing two of these and editing Host and the web onboarding code by hand. Skills were
strings matched with `if`, and nothing declared which tools or KPIs an employee needs.

## Decision

Each kind of employee is one class implementing `IEmployeeModule` (`src/HireZero.Core/EmployeeModules.cs`) in its
own `src/HireZero.Roles.*` assembly. Its `EmployeeManifest` declares:

- **Identity and versions**: id, name, summary, the contract version it targets, its own module version.
- **Instructions**: the base system instructions, with optional per-skill instructions.
- **Skills**: id, name, description and mode (`Text`, `Measured`, `Execution`). Structured input is validated by the
  module (`ValidateInput`), so a skill's input is a typed record such as Marketing's campaign brief rather than
  free text. Platform skills that the company itself orchestrates (`chat`, `meeting-turn`, `meeting-chair`,
  `company-plan`) are named by `PlatformSkills` constants in Core.
- **Tools**: the capabilities the employee needs, each with a risk level (`Read`, `Write`, `External`, `Spend`) and
  a purpose. Phase 1 declares them; Phase 2's tool registry enforces them.
- **KPIs**: id, name, unit and description of what the employee reports.
- **Onboarding**: suggested department, default name and responsibility, the knowledge fields with owner-facing
  help text, and the boundaries shown at hiring.
- **Capabilities**: platform behaviours the employee may use (workspace claims, company overview).
- **UI slots**: named places where the company UI renders the employee's own panels (for example
  `skill-input:marketing-draft`).

The company workspace consumes modules only through this contract. Its internal `CompanyRole` projection is
built from the manifest by Core and is no longer authored by roles. Hiring copy is served from the manifest, so
the web needs no per-role code unless an employee contributes UI slots.

During the transition a module may also expose the legacy `IRole` (`LegacyRole`) so the Developer profile keeps
working until it is retired.

## Consequences

- A new employee is a new `src/HireZero.Roles.*` project with one `IEmployeeModule`, plus an optional
  `web/src/roles/<id>/company.tsx` for UI slots. Neither Host nor shared web code changes (see ADR 0006).
- The unused `EmployeeDefinition` path is deleted.
- `IRole`, `roles.json` and the legacy Developer profile remain until Developer's legacy services are ported onto
  skills and Phase 2 tools; that retirement is tracked in the roadmap.
