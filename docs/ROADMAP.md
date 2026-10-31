# HireZero roadmap

HireZero is a local-first platform for scaffolding a company out of AI employees. Each employee is a module you
can hire, onboard, steer, review and fire. The aim of this roadmap is a product an enterprise would pay for:
safe by default, auditable, multi-user, and extensible by adding a folder rather than editing the platform.

This file is the canonical plan. Architecture decisions behind it live in [docs/adr](adr/). Update the status
column when a phase lands; open a new ADR when a decision changes.

## Context-first product track — adopted 2026-10-25

The owner requested implementation of **all three phases** of the [context-first product vision](product/context-first-vision.md):
context-first onboarding, persistent employee workflows and advanced autonomous capabilities. The
[delivery and acceptance ledger](CONTEXT_FIRST_DELIVERY.md) tracks every phase, cross-cutting invariant and
customer-validation target against current evidence. This track builds on the engineering foundations below;
those earlier phases are not a completion claim for the new product scope.

Current stage: durable business identity, candidate search, bounded discovery, sourced profile drafting,
correction history, approved private text documents, bounded retrieval, source freshness review, finite recurring work, a durable inbox with optional bounded email, structured memory, monthly usage controls and a first Marketing plan with source provenance
([ADR 0051](adr/0051-context-first-company-records.md), [0052](adr/0052-pre-hire-company-drafting.md),
[0053](adr/0053-brief-to-first-assignment.md), [0054](adr/0054-bounded-business-discovery.md),
[0055](adr/0055-business-history-and-provenance.md), [0056](adr/0056-private-documents-and-retrieval.md),
[0057](adr/0057-source-freshness-and-owner-review.md), [0058](adr/0058-bounded-recurring-work.md),
[0059](adr/0059-durable-inbox-and-bounded-notices.md), [0060](adr/0060-structured-organizational-memory.md),
[0061](adr/0061-monthly-usage-admission-and-cost-receipts.md), [0062](adr/0062-company-owned-private-connections.md),
[0063](adr/0063-explicit-runtime-release-receipts.md), [0064](adr/0064-cross-company-checkout-leases.md)).
Private connections now belong to individual companies, with scoped consent callbacks and local isolation verification.
Drive copies now retain company-bound import provenance and offer explicit source comparisons followed by a
separately reviewed replacement ([ADR 0118](adr/0118-private-document-source-receipts.md),
[local source and browser evidence](history/2026-10-26-private-document-source-review.md)).
Drive selection now accepts document/file links and preserves case-sensitive file identities
([ADR 0123](adr/0123-drive-links-and-exact-file-identities.md),
[fictional setup/import verification](history/2026-10-26-drive-file-selection.md)).
Member connection pages and conversation cards now show availability and owner guidance without loading protected
setup forms ([local role and browser verification](history/2026-10-26-member-connection-ux.md)).
The broader member journey now keeps setup controls with owners, work approvals with approvers/owners, and saved
records readable to viewers ([setup/work and owner regression evidence](history/2026-10-26-member-work-ux.md)).
Hosted browser drafts and recovery receipts now stay with their member, with stale-account request refusal,
session rechecks and confirmed sign-out ([ADR 0114](adr/0114-member-bound-browser-sessions.md),
[fictional middleware/UI verification](history/2026-10-26-member-session-recovery.md)).
Secondary-company invitations, stale links and revoked memberships now have an authenticated company chooser and
recovery path ([ADR 0115](adr/0115-authenticated-company-entry-recovery.md),
[fictional entry and isolation verification](history/2026-10-26-company-entry-recovery.md)).
Compact cockpit and shortcut-help overlays now enforce modal focus and background isolation, preserve keyboard
return focus and avoid reopening after resize ([ADR 0122](adr/0122-modal-workspace-overlays.md),
[real-keyboard fictional browser verification](history/2026-10-26-modal-workspace-overlays.md)).
Plan and meeting decisions now appear in the durable inbox with exact, reloadable destinations. The cockpit
surfaces meeting minutes instead of separate review requests for each discussion turn
([ADR 0116](adr/0116-coordinated-work-inbox-destinations.md),
[local lifecycle/browser verification](history/2026-10-26-coordinated-work-inbox.md)).
Accepted minutes now offer an editable, exactly cited decision draft with separate memory approval and preserved
unfinished work ([ADR 0117](adr/0117-accepted-minutes-as-reviewed-memory.md),
[local provenance and browser verification](history/2026-10-26-meeting-decision-memory.md)).
Local coding has durable runtime-release receipts and is restricted to the original local company.
Physical checkout leases also exclude competing companies within the same application database.
The bounded sandbox provider has offline checks ([ADR 0065](adr/0065-bounded-sandbox-provider.md)). Durable
approval, compute admission, revocation and restart recovery have local fixture checks
([ADR 0066](adr/0066-durable-sandbox-authority-and-allocation.md)). Task review/results, compute controls and
the recoverable host queue have fictional desktop/mobile E2E proof
([ADR 0067](adr/0067-script-review-ui-and-recoverable-queue.md)). Qualification-based provider registration,
engine-bound recovery and independent execution/recovery controls have local verification
([ADR 0068](adr/0068-qualified-runtime-activation.md)). A complete local standard/extended script suite and receipt
restart checks now pass ([execution evidence](history/2026-10-26-local-script-qualification.md)), but earlier
runtime exits still block reliability acceptance. [Diagnostics](history/2026-10-26-script-runtime-task-exhaustion.md)
identified host task exhaustion; reducing Node pools did not fix it. The
[revised resource plan](history/2026-10-26-script-task-budget-plan.md) was approved and implemented, with 28 offline
checks passing on Windows/Linux. Its first workload-limit case failed and stopped the run; no receipt was issued
([attempt evidence](history/2026-10-26-script-task-budget-attempt.md)). The separately approved
[single diagnostic](history/2026-10-26-script-task-limit-diagnostic-plan.md) found 33 workload tasks where 32
were required; it stopped with cleanup confirmed and no receipt. The subsequent approved correction accounts
for the initial task outside the runtime UID counter: actual 32-workload/128-host limits now pass the bounded
local standard/extended suite and restart-stable v3 receipt checks ([ADR 0146](adr/0146-script-initial-task-accounting.md)).
Execution remains disabled; broader reliability, production acceptance and compute pricing remain pending.
Saved script outputs can now enter task review, completion and explicit employee handoff while preserving revocable
source lineage ([ADR 0069](adr/0069-reviewed-sandbox-output-reuse.md)); this journey has fictional desktop/mobile E2E proof.
Never-approved document drafts can be explicitly deleted, including old operation-result copies, with optional
retention for discarded drafts ([ADR 0070](adr/0070-unapproved-document-erasure-and-retention.md)). Revoked library
sources have a separate exact owner deletion flow ([ADR 0099](adr/0099-revoked-document-library-deletion.md)).
Earlier task copies, other drafts, full-company deletion and backup retirement remain separate privacy work.
Never-approved memory now has its own exact owner deletion flow, including saved command responses and matching
browser edits ([ADR 0103](adr/0103-unapproved-memory-deletion.md)). Existing audit titles, source reports and
historical copies remain; this is not full-company erasure.
Revoked or replaced saved memory now has separate exact owner deletion, with content-free tombstones,
scrubbed command responses and preserved historical task packets/approved replacements
([ADR 0121](adr/0121-retired-memory-source-deletion.md),
[backend and desktop/mobile evidence](history/2026-10-26-retired-memory-deletion.md)).
The approved-context and retired-source flows now also pass on real PostgreSQL, alongside company isolation,
migrations, audit integrity and password rotation: seven database facts pass without skips
([database verification](history/2026-10-26-postgres-context-verification.md)). Hosted acceptance remains separate.
Local backup retention now verifies the selected source root, manifest and exact contents before pruning,
refuses linked paths and preserves existing partial folders ([ADR 0104](adr/0104-verified-backup-retention.md)).
Unknown or incomplete cleanup requires manual review rather than broader deletion.
Owners can inspect company-scoped record counts and unresolved work before planning closure
([ADR 0105](adr/0105-company-retirement-review.md)). The read-only review never authorizes deletion;
company-wide quiescence, bootstrap suppression and coordinated erasure remain outstanding.
An owner can now pause new work across execution admissions without losing earlier results or recovery
([ADR 0106](adr/0106-company-work-admission-pause.md)). Already admitted work may still start or finish;
resume preserves existing approvals, limits and missed-slot rules. This is not full-company quiescence.
Company creation now validates before saving and commits its owner, audit and request receipt atomically
([ADR 0107](adr/0107-atomic-company-creation-and-recovery.md)). Lost responses recover through a saved-result
check, including after a tab reload, without creating another company.
The first Marketing plan is now a tracked assignment with a bounded deliverable, exact result review and explicit
completion ([ADR 0071](adr/0071-tracked-first-deliverable.md)); ordinary chat limits are unchanged.
Company progress now summarizes saved onboarding milestones, brief edits, completed work, member request days
and model-cost coverage, with a content-free JSON export ([ADR 0072](adr/0072-company-progress-from-saved-history.md)).
CEO proposals now produce reviewable plans whose ordinary tasks can use the measured queue, share only approved
dependency results and complete under an unchanged parent allowance. Plans show named assignments and task links,
with separate job approval and a seven-day execution window ([ADR 0073](adr/0073-measured-delegation-and-approved-dependencies.md)).
The full fictional browser journey passes; actual multi-employee planning quality is still unverified.
Home now resumes the role-owned first assignment after onboarding, links to pending authorization and result
review, and keeps its guide until an approved result belongs to completed tracked work
([ADR 0112](adr/0112-home-guides-the-first-completed-result.md)). Chat and setup-context drafts do not satisfy
that milestone. This improves the route to first value; customer usefulness remains an observed-pilot requirement.
Approved goals now inform explainable role suggestions. Developer and CEO join Marketing in offering editable
brief-based onboarding and a tracked first plan, while existing employees are offered before duplicate hires
([ADR 0074](adr/0074-goal-based-team-setup.md)). These deterministic suggestions have fictional E2E proof;
real recommendation relevance remains unverified.
Public-page browser work now has a separate bounded network broker and Chromium itinerary driver with fictional
verification ([ADR 0075](adr/0075-bounded-public-browser-foundation.md)). Dynamic capture, scope, crawl policy,
budgets, profile isolation and cleanup uncertainty are covered. Durable authority, resource recovery and task-review
UI are composed under the decisions below; production runtime qualification and registration remain pending.
The broker and driver now connect through a bounded worker protocol with typed returned-output checks and
revocation fences ([ADR 0076](adr/0076-browser-worker-protocol-and-output-fence.md)). A fictional .NET-to-worker-to-Chrome
journey verifies dynamic capture and cancellation using private debugging pipes; local runsc evidence is recorded below.
The distinct browser resource adapter now binds an exact request and original image/engine, retains output before
release and recovers stopped-resource logs without replay ([ADR 0077](adr/0077-browser-resource-execution-and-recovery.md)).
Its contained supervisor and resource profile have offline checks. Durable company approval, shared script/browser
allocation, recovery without another visit and revocable captured-text review now have local lifecycle proof
([ADR 0078](adr/0078-durable-browser-authority-and-reviewed-captures.md)). The usage screen describes the shared
allowance. The all-company browser queue, owner itinerary/capture controls, result downloads and captured-text
review now have fictional host and desktop/mobile product evidence
([ADR 0079](adr/0079-browser-queue-and-owner-review-journey.md)). Completing a parent task withdraws remaining
browser permission. Receipt-bound production registration and independent readiness monitoring now exist
([ADR 0095](adr/0095-qualified-browser-activation.md)); runtime withdrawal cancels active work while original
resource recovery remains available. Real production image qualification and activation remain pending.
The owner-approved [dedicated local WSL host](../runtime/qualification-host/README.md) is now installed from
the pinned inputs. The real browser suite passed with fictional pages, confirmed cleanup and receipt readiness
before/after its daemon restart ([execution evidence](history/2026-10-26-local-browser-qualification.md)).
The run found and repaired capability-name matching, bounded-log configuration, unnecessary Chromium startup
work and unstable capability-order fingerprints. Application execution remains disabled; this local receipt
does not qualify scripts, another installation or a production deployment.
Raw browser captures now have owner deletion and optional expiry after confirmed cleanup, while selected task
results retain exact source proofs and revocation checks ([ADR 0080](adr/0080-browser-capture-retention.md)).
This erases current application records; task history, backups and database recovery files require separate work.
Billing now has a provisional Stripe adapter, exact subscription approval, durable single dispatch, canonical
payment verification and separate purchased AI/compute allowance admission
([ADR 0081](adr/0081-stripe-billing-and-purchased-allowances.md)). Host registration, signed durable event
processing, customer portal requests and the Billing UI now pass fictional backend and desktop/mobile journeys
([ADR 0082](adr/0082-billing-host-and-owner-journey.md)). Real hosted Stripe test-mode checkout, merchant setup,
launch commercial terms and full operating costs remain pending. Company-owned API keys now have encrypted
company-scoped storage, explicit provider/model consent, fresh approval after key changes and direct-provider
funding receipts ([ADR 0085](adr/0085-company-owned-model-connections.md)). Fictional backend and desktop/mobile
journeys pass; live provider acceptance and operator-reviewed production API prices remain pending.
Paid plans can now explicitly include extended batch scripts with exact duration approval, separate runtime
qualification and shared monthly allocation ([ADR 0086](adr/0086-paid-extended-scripts.md)). Fictional backend
and desktop/mobile journeys verify payment/runtime loss, retained holds, recovery without rerun and completed
result review after payment lapse. Production extended qualification, broader premium environments and actual
compute-cost accounting remain pending.
Finite recurring script reports now compose saved script reviews with approved document replacements,
per-occurrence compute admission and recovery without rerun ([ADR 0087](adr/0087-recurring-reviewed-scripts.md)).
Fictional lifecycle and desktop/mobile journeys cover two reports through owner acceptance, finite completion,
payment loss, paused work and cleanup holds. Production qualification and observed recurring usefulness remain pending.
Accepted measured work now offers **Repeat this work**, preserving its skill, selected approved references and
measured allowance in a separate draft ([ADR 0093](adr/0093-recurring-measured-deliverables.md)). Exact model
choices and finite consent apply to each occurrence; previous conversation history stays out, and each result
still needs separate owner acceptance. Two occurrences, draft recovery and the existing script flow have
fictional lifecycle and desktop/mobile proof. Actual recurring usefulness and retention remain unverified.
Owner feedback can now start beside an approved result, with its citation and originating employee already
selected ([ADR 0088](adr/0088-result-feedback-memory.md)). Existing drafts are preserved, failed citation reads
can be retried, and only separately approved current feedback enters future context. Selected records now support
owner-authorized model checks for possible conflicts, duplicates and review needs, with exact quoted citations,
usage receipts, stale-source fences and recovery without replay ([ADR 0091](adr/0091-owner-reviewed-memory-consistency.md)).
Reports preserve memory and unfinished drafts; changes still require a separate revision and approval.
Reports can now include cited suggested wording that opens an editable replacement draft, preserving its audience,
work citation and source revisions ([ADR 0094](adr/0094-owner-reviewed-memory-revisions.md)). Separate owner approval
retires the old record; lost-response recovery, stale-source refusal and unfinished-draft preservation have fictional
lifecycle and desktop/mobile proof. Real model quality and semantic retrieval usefulness remain unverified.
Routine work now retains its default model while explicitly declared planning/review work can use an optional
analysis route ([ADR 0092](adr/0092-explicit-purpose-model-routing.md)). Exact review, native/API dispatch and usage
receipts agree; changed choices require fresh review, and failures never select another model. Fictional tests
cover the owner journey, while real model suitability and cost comparisons remain pending.
Private document drafts now accept bounded Word body text and tables, with explicit extraction limitations,
source filenames, preserved edits and separate saved-text approval ([ADR 0089](adr/0089-reviewed-word-text-import.md)).
Fictional backend and desktop/mobile journeys verify failed imports, reload recovery, employee scope and
revocation. Original binaries are not retained. Selected Google Drive files now have a separate company-owned
connection, bounded version-checked text previews and explicit copy approval
([ADR 0090](adr/0090-selected-drive-document-copies.md)). Fictional setup, mobile import, isolation and
disconnection/revocation journeys pass. Copies are independent of Drive and their source notes are editable.
Selectable-text PDFs now use a separate bounded parser process, preserve page labels and mark pages without
text before separate document approval ([ADR 0098](adr/0098-reviewed-pdf-text-import.md)). Fictional encrypted,
scanned, malformed and oversized inputs plus desktop/mobile recovery and scoped retrieval have local proof.
Live Drive authorization, delegated OAuth/Picker setup, OCR, visual fidelity and further private connectors remain separate work.
Ordinary delegated assignments now support explicitly selected approved predecessor results as bounded script
inputs, separate exact execution consent and reviewed task completion under the unchanged parent token ceiling
([ADR 0083](adr/0083-reviewed-scripts-in-delegated-plans.md)). Fictional lifecycle and desktop/mobile E2E checks
cover admission races, revocation, cleanup uncertainty and completed history. Public browser assignments now
share that exact plan authority and can supply reviewed evidence to a later employee's script
([ADR 0084](adr/0084-reviewed-browsers-in-delegated-plans.md)). The fictional browser-to-script plan and existing
standalone journeys pass desktop/mobile checks. Model, script and browser execution are mutually exclusive on
an assignment until resources are released. Real runtime qualification and repository
orchestration remain separate work.
Completed work now has owner-only cost reconciliation across all model attempts and separately reviewed
compute, tool, retrieval, storage and operating expenses ([ADR 0097](adr/0097-task-cost-reconciliation.md)).
Progress now aggregates those current completed-task totals for owners, exposes missing/stale cost coverage,
and withholds an incomplete average ([ADR 0101](adr/0101-completed-task-cost-progress.md)).
The [private pilot protocol](product/pilot-protocol.md) and offline cohort report now combine those exports with
explicit observer assessments ([ADR 0102](adr/0102-private-pilot-observations.md)); real customer validation remains pending.
Unknown costs, estimates, usage changes and corrections remain explicit; desktop/mobile recovery has fictional
proof. Actual invoices, shared-cost allocations and sustainable pricing still require operational observations.
Marketing now has a goal ladder and weekly numbers: one number to grow, the steps customers take, the step to work
on first, and a weekly scorecard ([ADR 0170](adr/0170-goal-ladder-and-weekly-numbers.md),
[notes](history/2026-10-30-goal-ladder.md)). It is the first of three steps toward an owner-approved Marketing shift.
The second puts Marketing on shift: a finite run of morning rounds that read the ladder, the numbers and recent work,
come back with up to three jobs and draft the first, with the rest one click away
([ADR 0171](adr/0171-an-owner-approved-shift.md), [notes](history/2026-10-30-marketing-shift.md)). The third gives
the shift a weekday rhythm, lessons the owner keeps for every later round, and done changes judged against the
numbers by a fixed rule ([ADR 0172](adr/0172-shift-rhythm-lessons-and-measured-changes.md),
[notes](history/2026-10-30-shift-rhythm-and-lessons.md)).
A site that counts its own visitors can now fill the weekly numbers form for the owner to check and save
([ADR 0173](adr/0173-site-analytics.md), [notes](history/2026-10-31-site-analytics.md)).
Actual usefulness, identification accuracy and commercial retention still require observed customer trials.
The full product goal remains in progress.

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
  - **Approved ad changes (done, [ADR 0033](adr/0033-approved-ad-changes.md)):** pause a campaign or set its daily
    budget as an External `ads.change` call, published once after the owner approves the exact change and a live
    re-check; budgets move at most half per step and never when shared; Google Ads changes use a separate, opt-in
    editing identity; unknown outcomes are settled only by reading.
  - **Email and CRM (done, [ADR 0035](adr/0035-email-and-crm-read-contracts.md)):** neutral `email.*` and `crm.*`
    Read contracts that carry aggregates only (enforced by a test), fictional stand-ins, owner-started reads with
    fixed list-hygiene and pipeline rules, and a measured review skill. Real providers come later behind the same
    contracts.
- **Third employee: Support (done, [ADR 0034](adr/0034-support-employee.md)):** added only as
  `src/HireZero.Roles.Support` and `web/src/roles/support`. It drafts customer replies, triage and help articles
  from messages redacted by a deterministic intake action, which also flags legal, security and privacy requests
  for a person. Finance/Ops can follow the same way.

Standalone website reviews now support finite daily/weekly recurrence with exact itinerary consent, per-slot
shared compute admission, no-replay recovery and separately approved capture results
([ADR 0096](adr/0096-recurring-website-reviews.md)). Pausing withdraws active authority; completed evidence remains
reviewable when future slots end. Local browser qualification is recorded above; production qualification,
automatic analysis and recurring usefulness remain pending.
