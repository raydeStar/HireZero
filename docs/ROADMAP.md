# HireZero roadmap

HireZero is a local-first platform for scaffolding a company out of AI employees. Each employee is a module you
can hire, onboard, steer, review and fire. The aim of this roadmap is a product an enterprise would pay for:
safe by default, auditable, multi-user, and extensible by adding a folder rather than editing the platform.

This file is the canonical plan. Architecture decisions behind it live in [docs/adr](adr/). Update the status
column when a phase lands; open a new ADR when a decision changes.

| Phase | Theme | Status |
| --- | --- | --- |
| 0 | Clean foundation | Done (see [Phase 0 report](PHASE0_REPORT.md)) |
| 1 | One employee module contract | Done (see [Phase 1 report](PHASE1_REPORT.md)) |
| 2 | Restore the layers: tools, integrations, one model provider | 2A done ([report](PHASE2A_REPORT.md)); 2B next |
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

Replace the three overlapping employee definitions (`IRole`, `EmployeeDefinition`, `CompanyRole`) with one
contract, `IEmployeeModule`, described by a manifest ([ADR 0002](adr/0002-employee-module-contract.md)), and find
modules without editing the platform ([ADR 0006](adr/0006-compiled-in-module-discovery.md)).

Delivered (see [Phase 1 report](PHASE1_REPORT.md)):

- The contract and manifest: identity and versions, instructions, skills with modes, structured input validation,
  tools with risk levels, KPIs, onboarding copy, capabilities, UI slots and display order.
- Compiled-in discovery: a wildcard project reference plus `[assembly: EmployeeModule]`, validated at startup.
- Developer, Marketing and CEO ported; the company workspace builds its roles from manifests; the unused
  `EmployeeDefinition` path is deleted.
- Hiring copy is served from manifests, and employee UI plugs into named slots through
  `web/src/roles/<id>/company.tsx`, picked up automatically.
- Architecture tests for the layering and module rules.

Exit criteria met: adding an employee is a new `src/HireZero.Roles.*` project plus an optional `web/src/roles/*`
folder, with no Host, Core or shared web edits ([how-to](ADDING_AN_EMPLOYEE.md)).

Moved to Phase 2, because each needs Developer's git and GitHub work to run through integration tools first:
porting Developer's legacy services (PR review, fix loop, wiki drafts, preparation, Trusted Postgres) onto
skills; module-owned storage migrations; merging the developer panels into the company UI; and retiring `IRole`,
`roles.json`, the Developer, Recovery and Held profiles and `web/src/shell`.

## Phase 2: Restore the layers

Employees reach the world only through registered, audited tools ([ADR 0007](adr/0007-tool-registry.md)); every
model is chosen by purpose from one catalog and called through one provider contract
([ADR 0008](adr/0008-model-routes.md)). Delivered in two parts.

### 2A: Foundations (done, see [Phase 2A report](PHASE2A_REPORT.md))

- Tool registry in Core: typed contracts in `HireZero.Tools.*` assemblies, a toolbox per module that enforces the
  manifest's declarations, a risk gate (Read; Write with a verified authority; External and Spend with a
  single-use owner approval bound to the exact input) and an audit trail (`co_tool_calls`, schema version 3).
- `Integrations.Git` with the first tools (`git.checkout-inspect`, `git.checkout-review`); the company Developer
  uses them instead of a Core port.
- Model route catalog (`config/model-routes.json`, overridable per data root): no model id remains in source.
- Architecture tests for tool contracts and declared tools.

### 2B: Developer port and one provider (next)

- `Integrations.GitHub` and the remaining `Integrations.Git` operations as tools: reads (PRs, reviews, checks,
  evidence), local writes (worktree, reset, commit) under execution grants, and external writes (push, merge,
  review-request comments) as `External` tools with digest-bound owner approvals. Then SemaForge, Wiki, Jev and
  Trusted Postgres, removing every process and HTTP call from `Roles.Developer`.
- Remove role vocabulary from Core: the git calls in `WorkingContext`, remote parsing, PR fields in the work log
  and owner tasks, wiki notices, Codex and Claude names.
- One provider contract replacing `IModelGateway`, `ICompanyModel`, `ICompanyMeasuredModel` and `ICodingRunner`,
  with the company reservation and settlement path as the single source of usage.
- Port Developer's legacy services onto skills and tools, give modules their own storage migrations, merge the
  developer panels into the company UI, then retire `IRole`, `roles.json`, the Developer, Recovery and Held
  profiles and `web/src/shell`. Decide which legacy-only features (Microsoft 365 intake, coding-agent session
  steering, Azure DevOps, Jev triage, tailnet multi-user access, daily brief) move into the company workspace.
- Expose and consume tools over MCP.

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
