# 0002: One employee module contract

Status: Proposed (Phase 1)

## Context

Three overlapping employee definitions exist:

- `IRole` (`src/HireZero.Core/Roles.cs`), hired through `roles.json`, used by the legacy Developer host.
- `EmployeeDefinition` / `IEmployeeSkill` (`src/HireZero.Core/Employees.cs`), wired by
  `EmployeeRegistration.AddEmployees` but never called at runtime.
- `CompanyRole` (`src/HireZero.Core/CompanyContracts.cs`), used by the company workspace.

Adding an employee today means writing two of these and editing Host by hand. Skills are strings matched with
`if`, and nothing declares which tools, permissions or KPIs an employee needs.

## Decision

Replace all three with `IEmployeeModule`, described by a manifest:

- identity and versions (id, name, contract version, module version);
- typed skills (input and output records);
- required tools by name, each with a risk level that drives approval;
- KPIs the employee reports;
- onboarding fields;
- optional UI slots;
- a module-owned storage schema with versioned migrations.

Modules are discovered at startup, so Host needs no edits to hire a new kind of employee.

## Consequences

Developer, Marketing and CEO are ported, then `IRole`, `roles.json`, the unused `EmployeeTeam` path and the legacy
single-agent host and UI are removed. The exact interface is designed with the owner before implementation.
