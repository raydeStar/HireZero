# HireZero roadmap

HireZero is a local-first platform for scaffolding a company out of AI employees. Each employee is a module you
can hire, onboard, steer, review and fire. The aim of this roadmap is a product an enterprise would pay for:
safe by default, auditable, multi-user, and extensible by adding a folder rather than editing the platform.

This file is the canonical plan. Architecture decisions behind it live in [docs/adr](adr/). Update the status
column when a phase lands; open a new ADR when a decision changes.

| Phase | Theme | Status |
| --- | --- | --- |
| 0 | Clean foundation | Done (see [Phase 0 report](history/PHASE0_REPORT.md)) |
| 1 | One employee module contract | Done (see [Phase 1 report](history/PHASE1_REPORT.md)) |
| 2 | Restore the layers: tools, integrations, one model provider | 2A and 2B.1–2B.4 done; 2B.5 under way ([2B.5a](history/PHASE2B5A_REPORT.md)) |
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

Delivered (see [Phase 1 report](history/PHASE1_REPORT.md)):

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

### 2A: Foundations (done, see [Phase 2A report](history/PHASE2A_REPORT.md))

- Tool registry in Core: typed contracts in `HireZero.Tools.*` assemblies, a toolbox per module that enforces the
  manifest's declarations, a risk gate (Read; Write with a verified authority; External and Spend with a
  single-use owner approval bound to the exact input) and an audit trail (`co_tool_calls`, schema version 3).
- `Integrations.Git` with the first tools (`git.checkout-inspect`, `git.checkout-review`); the company Developer
  uses them instead of a Core port.
- Model route catalog (`config/model-routes.json`, overridable per data root): no model id remains in source.
- Architecture tests for tool contracts and declared tools.

### 2B: Developer port and one provider

**2B.1 (done, see [Phase 2B.1 report](history/PHASE2B1_REPORT.md)):** every process the Developer starts goes through
tools.

- `Integrations.GitHub` runs git and gh for `git.read`, `git.write`, `git.push`, `github.read` and `github.write`. It
  enforces the read, write and push classification itself.
- Developer writes run under the approved workflow or fix loop in scope. Verifiers bind them to the claimed
  approval, the workflow's own commit and branch, and its own pull request.
- GitHub sign-in and the pinned npm install are owner-action tools.
- Roles no longer reference `System.Diagnostics.Process`, and a test enforces it.

**2B.2 (done, see [Phase 2B.2 report](history/PHASE2B2_REPORT.md)):** every HTTP call the Developer made goes through
tools. SemaForge, the Wiki pilot and Jev are integrations. Writes are bound to:

- an approved workflow;
- a fix's own runs;
- a reserved wiki draft;
- or your request in flight.

No role opens an HTTP client, and a test enforces it.

**2B.3 (done, see [Phase 2B.3 report](history/PHASE2B3_REPORT.md)):** Core starts no process, and a test enforces it.

- The repository context moved to the Developer, read through `git.read`.
- Tailscale identity moved to `Integrations.Tailscale`.
- Company routing and task reasons are role-neutral; the Codex connection owns its provider and runtime constraint.
- Employer and personal identifiers were scrubbed from test fixtures.

The remaining vocabulary in Core is legacy-only: work-log PR fields, owner-task PR rules, thread-title URL rules,
wiki notices, Codex and Claude session names, and git-host parsing for boss sharing. It moves out with those
features in 2B.5 rather than twice.

**2B.4 (done, see [Phase 2B.4 report](history/PHASE2B4_REPORT.md)):** one model provider contract, recorded in
[ADR 0010](adr/0010-model-provider-contract.md).

- The company contract is now the platform's `IModelProvider` (`ModelProviders.cs`), with a how-to for adding a
  vendor.
- Runtime constraints belong to providers.
- Usage that a provider didn't report is flagged and estimated, never silently zero.
- The legacy `IModelGateway` and `ICodingRunner` are not extended; they retire with the legacy profile.

**2B.5 is under way**, one feature at a time:

- **2B.5a (done, see [Phase 2B.5a report](history/PHASE2B5A_REPORT.md)):** employee actions
  ([ADR 0011](adr/0011-employee-actions.md)). The company Developer gathers PR evidence (`pr-evidence`), and a
  measured `pr-review` skill reviews it once you approve the evidence.
- **2B.5b1 (done):** the PR fix as an execution skill. A reviewed fix runs in an approved checkout of the PR head,
  then commits and pushes through `git.write` and `git.push`, each call authorized once by your publication approval
  ([ADR 0012](adr/0012-company-publication.md)). Fixture runner only.
- **2B.5b2 (done):** the company host and UI drive it: endpoints to prepare, run, publish and reconcile, approval buttons
  for the grant and the publication, and the diff review for every execution skill.
- **2B.5b3a (done):** execution limits are approved with the grant and size its reservation
  ([ADR 0013](adr/0013-execution-limits-approved-with-the-grant.md)).
- **2B.5b3b (done, offline):** a qualified live Codex coding runner for company execution
  ([ADR 0014](adr/0014-pinned-codex-runtime.md)): pinned runtime, MXC sandbox writing only the checkout, named
  read-only toolchain folders, network off, the measured-reply preflight and a usage tripwire, behind
  `--company-coding`. Live qualification (sandbox probe, one fictional coding turn, a real fix) needs your approval
  for each step. Then the fix loop.
- **2B.5c1 (done):** request preparation as the `request-prep` measured skill over approved evidence. The legacy
  stage board retires with the legacy profile; your approved artifact is the record.
- **2B.5c2 (done, see [ADR 0015](adr/0015-publishing-an-approved-artifact.md)):** wiki drafts. A `wiki-read` action for evidence, a measured `wiki-draft` skill, and a publication
  sourced from the approved draft artifact (contribute, then publish once; no automatic retry). Synthetic projects
  only.
- **Trusted Postgres moves to the live-coding track** (after 2B.5b3 qualification). It is part of the coding
  environment and only coding runs use it. Its install downloads a package and runs its install scripts, so it stays
  an owner-approved install bound to the exact package, and it is never an action.
- **2B.5d1 (done):** Microsoft 365 intake in the company workspace. Owner-started mail and Teams reads as the
  Developer's `m365-read` action (reviewed evidence, no polling, no cursor moved), the measured `inbox-triage` skill,
  setup on the company overview, an integrations status list, and a test keeping Microsoft 365 names out of Core.
- **2B.5d2 (needs your decision):** replying to a message as a publication sourced from an approved reply. It needs
  the `Mail.Send` permission, which the legacy integration never requested, and a publication with no local call.
- **2B.5e1 (done):** read-only coding-agent session listing as an optional integration (`agent-sessions` action,
  off until apps are listed in `agent-apps.json`), and publications with one approved external call
  ([ADR 0016](adr/0016-publishing-one-approved-call.md)).
- **2B.5e2 (done):** read-only steering of a listed session as an owner-approved, one-call publication, using the
  owner's installed app; the legacy steering code and its Core vocabulary moved into the integration.
- **2B.5f (done):** Azure DevOps as an optional, read-only integration (`ado-pulls` action, `pr-attention` skill,
  configured in `azure-devops.json`), with its remote-URL format, sharing rule and title pattern moved out of Core.
- **2B.5g (done):** Jev as an optional integration: the owner-started `jev-sort` action (at most ten message
  excerpts sent to Jev), with Jev's name out of Core.
- **OpenRouter (done):** a second classifier for the message sort (`openrouter-sort`), with the key in the
  environment and the owner's chosen model in `openrouter.json`.
- **Pull request steps (done):** the Developer's `pr-step` action reads a pull request live and names one step
  (merge, re-run its or its base branch's failed jobs, or ask Dependabot to rebase), published once as a single approved GitHub call
  ([ADR 0017](adr/0017-publishing-a-step-from-an-action.md)). A merge needs green head checks and a green base
  branch, so merges go one at a time.
- **2B.5i (done):** the daily brief: the company's Today view (what waits on you, the day's events), the
  Developer's `session-brief` action (one day of agent-session counts, no transcripts) and the measured `day-brief`
  skill. See [the report](history/PHASE2B5I_REPORT.md).
- **2B.5h (moved to Phase 3):** tailnet multi-user access belongs with real users, sign-in and roles.
- **2B.5j (done, see [ADR 0019](adr/0019-retiring-the-legacy-profiles.md)):** the company path stands apart from the
  legacy code; modules version their own storage ([ADR 0018](adr/0018-module-storage-migrations.md)); the budget
  panel, GitHub sign-in, review queue (`pr-queue`), owner tasks and work log (on Today) and Trusted PostgreSQL setup
  (`postgres-setup`) are ported; session hooks, shift proposals, backlog and rule-based intake are dropped. The
  developer, recovery and held profiles, `IRole`, `roles.json`, `IModelGateway`, `UsageMeter`, the OpenClaw
  gateway and `web/src/shell` are retired. Deferred: long-term memory. Trusted PostgreSQL inside live coding runs
  followed ([ADR 0023](adr/0023-trusted-postgres-in-live-coding-runs.md)): per-repository profiles set through
  `postgres-profile`, and the toolchain given read-only to a run whose repository has one.

**After 2B:** expose and consume tools over MCP.

## Phase 3: Enterprise readiness

- **Members, roles and sign-in (done, [ADR 0020](adr/0020-members-roles-and-sign-in.md)):** owner, approver and
  viewer roles enforced by one table in Core; invitations by email; hosted mode behind HTTPS with generic OIDC
  sign-in (code flow with PKCE, any compliant provider); the acting member recorded on every event; a sign-in
  screen and members panel. See [the report](history/PHASE3_IDENTITY_REPORT.md).
- **Tenancy (done, [ADR 0021](adr/0021-companies-per-data-root.md)):** several companies per data root, each
  with its own store, tools, scheduler and members; a request names its company, and owners create companies.
- Tailnet multi-user access (from 2B.5h), on the hosted identity model.
- **Tamper-evident audit log (done, [ADR 0022](adr/0022-tamper-evident-audit-log.md)):** a keyed hash chain per
  company of every event (each approval with its actor), tool call, model call and coding run; verify in the
  workspace and export as NDJSON ending with the verified head.
- **Secrets from files, vault-neutral (done, [ADR 0027](adr/0027-secrets-from-files.md),
  [guide](SECRETS.md)):** every secret is read by name from a mounted file, systemd credential or the
  environment, so any vault delivers it through its agent or CSI driver; rotation without restarts for the OIDC
  secret, API keys and database password; stored token keys wrapped by a key-encryption key on every OS; a shared
  audit key for hosts on one database.
- **Typed settings (done, [ADR 0028](adr/0028-typed-settings.md), [reference](SETTINGS.md)):** every host setting
  in one typed, validated `IOptions<CompanySettings>`, from the environment or a JSON settings file with the same
  names; all problems reported at once; secrets and unknown names refused in the file.
- **OpenTelemetry (done, [ADR 0024](adr/0024-opentelemetry-traces-and-metrics.md)):** spans for employee jobs, tool
  calls and requests (tagged with their company); metrics for jobs and their duration, tool calls by outcome, model
  calls and tokens per company and employee, and coding runs. Counted only after commit, identifiers only, exported
  over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- **One schema, two databases (done, [ADR 0025](adr/0025-one-schema-two-databases.md)):** SQLite for
  single-machine installs, PostgreSQL for hosted ones (`HIREZERO_DATABASE`), through one store seam that translates the
  portable statements; one migration framework and ledger for the company and every module; per-company write
  exclusion on both.
- **Container and releases (done, [ADR 0026](adr/0026-container-and-releases.md)):** a digest-pinned, non-root
  hosted-mode image; a portable release zip with PowerShell and POSIX launchers; a CycloneDX SBOM from the
  lockfiles; SHA256SUMS signed with ECDSA P-256 (openssl-verifiable); dependency advisories triaged before each
  release; every offline check in one script any CI runner can call.

## Phase 4: Prove "omni"

- Marketing, rebuilt on the contract:
  - **Analytics reads (done, [ADR 0029](adr/0029-marketing-analytics-reads.md), [guide](ANALYTICS.md)):** neutral
    `analytics.*` Read tools served by GA4 and Search Console (service account, no SDK) or by fictional synthetic
    data; an owner-started read comparing periods and flagging unusual days with a robust score; a measured KPI
    digest skill over the approved evidence.
  - **A/B tests (done, [ADR 0030](adr/0030-ab-test-design-and-significance.md)):** deterministic actions size a
    test (sample per variant, whole-week run length, Bonferroni for several variants) and read its result (lift,
    confidence interval, p-value, sample-ratio check); a measured skill writes the plan or readout from them.
  - **Ads reads (done, [ADR 0031](adr/0031-ads-read-contracts.md)):** neutral `ads.*` Read contracts with
    fictional fixtures in XTS; an owner-started read deriving CTR, CPC, CPA and ROAS, flagging unusual days and
    listing campaigns that need attention by fixed rules; a measured ads review skill that only proposes changes.
  - **Google Ads (done, [ADR 0032](adr/0032-google-ads-provider.md)):** the first real provider behind `ads.*`:
    read queries it composes itself, refresh-token sign-in with a read-only user, secrets for the developer token,
    client secret and refresh token, and an API version the owner can move.
  - Still to come: ad changes as owner-approved publications; email/CRM.
- A third employee (Support or Finance/Ops) built only from a new module folder, as the proof that the platform
  scales by hiring.
