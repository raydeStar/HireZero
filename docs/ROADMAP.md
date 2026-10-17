# HireZero roadmap

HireZero is a local-first platform for scaffolding a company out of AI employees. Each employee is a module you
can hire, onboard, steer, review and fire. The aim of this roadmap is a product an enterprise would pay for:
safe by default, auditable, multi-user, and extensible by adding a folder rather than editing the platform.

This file is the canonical plan. Architecture decisions behind it live in [docs/adr](adr/). Update the status
column when a phase lands; open a new ADR when a decision changes.

| Phase | Theme | Status |
| --- | --- | --- |
| 0 | Clean foundation | Done (see [Phase 0 report](PHASE0_REPORT.md)) |
| 1 | One employee module contract | Next |
| 2 | Restore the layers: tools, integrations, one model provider | Planned |
| 3 | Enterprise readiness | Planned |
| 4 | Prove "omni": a real second and third employee | Planned |

## What we keep

The safety model is the product's strongest asset and every phase preserves it:

- Anything consequential (push, merge, send, publish, spend) needs explicit owner approval, bound to the exact
  version that was reviewed.
- Single-use execution grants, budget reservations that stay held when usage is unknown, and no automatic replay
  of an uncertain external outcome.
- Synthetic tests with no live model, GitHub or GPU calls in routine checks.

## Phase 0: Clean foundation

Make the repository read like a product and make the build reproducible.

- Remove ZIP-export leftovers; archive agent session logs under `docs/history/`; write a product README.
- Canonize this roadmap and the first ADRs.
- Make the SDK pin buildable on any .NET 10 SDK.
- Collapse duplicated host plumbing; remove dead startup profiles; make Program.cs a profile dispatcher.
- Pin the OpenClaw runtime by version and verify it at startup.
- Centralize build settings, add `.editorconfig`, analyzers and frontend `typecheck`/`format` scripts.
- Move the .NET checks onto xUnit so failures are reported per check group instead of stopping at the first.
- Apply automated formatting (CSharpier, Prettier) to undo the one-line style.

Deliberately not in Phase 0: unifying the role models, retiring the legacy Developer host and merging the two web
UIs. Each depends on the Phase 1 contract; doing them first would orphan working Developer features.

## Phase 1: One employee module contract

Today there are three overlapping ways to define an employee (`IRole`, `EmployeeDefinition`, `CompanyRole`).
Replace them with one contract, `IEmployeeModule`, described by a manifest:

- Identity: id, display name, contract version, module version.
- Skills: typed input/output records instead of string kinds matched with `if`.
- Required tools: named capabilities such as `github.pr.read` or `analytics.query`, each with a risk level.
- KPIs: what the employee reports on its scorecard.
- Onboarding fields: what the owner must supply when hiring.
- UI slots: panels the company UI renders for this employee.
- Storage: a module-owned schema with versioned migrations.

Modules are discovered (assembly scan or plugin folder), so hiring a new kind of employee needs no Host edits.
Port Developer, Marketing and CEO onto the contract, then retire `IRole`/`roles.json`, the dead
`EmployeeTeam` path and the legacy single-agent host. Merge the developer panels into the company UI and delete
the legacy `web/src/shell` UI.

Exit criteria: adding an employee is a new `src/HireZero.Roles.*` project plus a `web/src/roles/*` folder;
an architecture test enforces it.

## Phase 2: Restore the layers

- Tool registry in Core: typed, named capabilities with risk levels, wired into the existing approval and budget
  gates. Integrations provide tools; roles request them by name and never start processes or HTTP clients.
- Move git, GitHub, npm and Postgres helpers out of Core and `Roles.Developer` into integration projects
  (`Integrations.Git`, `Integrations.GitHub`, ...). Remove role vocabulary (PRs, wiki, Codex) from Core.
- One model provider abstraction replacing `IModelGateway`, `ICompanyModel`, `IEmployeeModelGateway` and
  `ICodingRunner`; routing per employee and skill from configuration; measured, bounded usage as middleware.
  No model id in source.
- Expose and consume tools over MCP.
- Architecture tests enforce: Core references no role or integration; integrations reference no role.

## Phase 3: Enterprise readiness

- Tenancy: multiple companies, real users, OIDC sign-in (Entra ID, Okta), RBAC (owner, approver, viewer).
- Tamper-evident audit log of approvals, tool calls and model calls, with export.
- Configuration through `IOptions`; secrets from a vault/Key Vault instead of DPAPI files.
- OpenTelemetry traces and metrics: per-employee jobs, token cost per company.
- SQLite for single-machine installs, Postgres for hosted; one migration framework.
- Container image, cross-platform launcher, CI running only offline checks, signed releases, SBOM, dependency
  advisories triaged.

## Phase 4: Prove "omni"

- Marketing, rebuilt on the contract: GA4 and Search Console (read), an ads platform (read first, writes only
  with approval), email/CRM; skills for KPI digests, anomaly detection, A/B test design with significance
  calculation, briefs and drafts.
- A third employee (Support or Finance/Ops) built only from a new module folder, as the proof that the platform
  scales by hiring.
