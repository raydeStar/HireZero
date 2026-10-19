# 0006: Employee modules are compiled in and discovered at startup

Status: Accepted (Phase 1)

## Context

Hiring a new kind of employee should not require editing the platform. Two ways to find modules were considered:

- **Compiled in**: every `src/HireZero.Roles.*` project is built with the host and found by scanning the host's own
  output directory at startup.
- **Plugin folder**: assemblies dropped into a data-root folder are loaded at runtime.

A plugin folder would let third parties ship employees, but loading arbitrary assemblies into the process that
holds approvals, budgets and credentials is a security boundary HireZero does not have yet: the code would run with
full trust, outside review, and could bypass every approval gate.

## Decision

Modules are compiled in. The host references every role project with a wildcard
(`../HireZero.Roles.*/*.csproj`), so a new role project is built and copied automatically. At startup
`EmployeeModuleCatalog.Discover` loads `HireZero.Roles.*.dll` from the application's own directory, finds the types
named by each assembly's `[assembly: EmployeeModule(typeof(...))]` attribute, validates the manifests (unique ids,
supported contract version, skills and onboarding fields well formed) and orders them deterministically.

The web registers employee UI the same way: `import.meta.glob` picks up every `web/src/roles/*/company.tsx`.

## Consequences

- Adding an employee needs no edits to Host, Core or shared web code; an architecture test enforces that the company
  profile names no role.
- Third-party employees are out of scope until a sandboxed extension model exists (for example out-of-process
  modules speaking MCP). That would be a new ADR.
