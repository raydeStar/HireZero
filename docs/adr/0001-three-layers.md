# 0001: Core, roles and integrations stay separate

Status: Accepted

## Context

HireZero grew from a single marketing agent into a developer agent and then a multi-employee company. Without a
firm boundary, role-specific vocabulary (pull requests, wikis, Codex sessions) and direct process or HTTP calls
leak into shared code, and each new employee costs edits across the platform.

## Decision

- **Core** owns the company: employees, conversations, tasks, artifacts, approvals, budgets, audit and storage.
  It knows nothing about any particular role, vendor or model.
- **Roles** (`src/HireZero.Roles.*`, `web/src/roles/*`) define what an employee does. A role reaches the outside
  world only through tools provided by integrations.
- **Integrations** (`src/HireZero.Integrations.*`) wrap external systems (GitHub, git, Microsoft 365, model
  runtimes). They know nothing about roles.
- **Host** composes the three and holds no business logic.

A new employee is a new role project plus a web role folder, never an edit to another role.

## Consequences

The current code violates this in known places (role vocabulary and git process calls in Core; direct
GitHub/git/npm calls in `Roles.Developer`). Phase 2 of the [roadmap](../ROADMAP.md) removes them and adds an
architecture test so the rule is enforced by the build rather than by review.
