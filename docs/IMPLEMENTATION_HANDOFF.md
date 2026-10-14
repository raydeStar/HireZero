# HireZero company workspace implementation handoff

Prepared 2026-10-14 for Sol 6.1. This is the approved direction translated into an implementation sequence, not a claim that the prototype is complete. Implement in this isolated checkout. Retain the reference extraction, original archive, historical marketing checkout, existing runtime data, and active OpenClaw work unchanged.

## Checkpoint and first delivery

The branch is `feature/employee-contexts`. The preserved source baseline is `3228e0db564dbb006f23f547d7afb17cddb695d9`; the partial prototype is `71e2998822f145fb2e76f0ed66a6f07d5625b0a1`. The prototype changes 13 files and has no employee tests or employee UI. A previous Core test-project build succeeded, but its checks were not executed. A later Host test-project build was interrupted. Neither is evidence that the prototype works end to end. Source status was clean before this planning document.

The first deliverable is a working local company workspace with two Marketing and two Developer employee instances, department onboarding, private employee conversations, assigned tasks, exact-version artifact review and handoff, and a persistent company activity feed. The owner can use these management functions without a model. Tests exercise generated replies through an injected deterministic provider. The normal UI reports model work as blocked until a live adapter is qualified; it never presents fixture answers as real employee output.

Deliver this slice before CEO autonomy or meeting automation. Then add the CEO and Board Meetings using the same task, context, authorization, and budget machinery. There is no justified all-day completion estimate yet. Judge progress by these demonstrated slices and the verification gates below.

## Source findings that constrain the design

Paths below are relative to this checkout; line references describe the preserved prototype.

| Source | Consequence |
| --- | --- |
| `AGENTS.md` | Core remains role-neutral. Integrations remain role-neutral. Role behavior belongs in its own project and frontend directory. Public source and fixtures must contain no real personal/company/runtime data. |
| `src/HireZero.Core/Employees.cs:270` and `EmployeeRegistration.cs:8` | `EmployeeId` currently equals `RoleInfo.Id`, with one worker per compiled definition. Replace this identity model before extending it. |
| `Employees.cs:222` and `:232` | Claiming uses a transaction, but there are no attempt fences or durable dispatch receipts. Context reads cross several connections and are not one consistent snapshot. |
| `Employees.cs:169`, `:238`, `:259` | Private notes and company facts have no version checks; handoffs are selected globally per employee rather than per task; stopping an employee is not owner-scoped. Do not retain these semantics. |
| `src/HireZero.Core/Roles.cs:21` | `roles.json` describes installed/activated legacy role modules, with services selected on restart. It cannot represent employee instances or per-employee employment. |
| `ConversationReplies.cs:68` and `HireZero.Roles.Developer/DeveloperWork.cs:103` | Legacy responder selection takes the first answer, and the general Developer responder always answers. New employee routing must not use this chain. |
| `src/HireZero.Core/Home.cs:11` | Existing Home mirrors request notices through an in-process event and excerpts answers. Useful UI precedent, but not an atomic company event ledger or an employee privacy boundary. |
| `src/HireZero.Core/Budgets.cs:22`, `:180`, `:209` | Existing budgets account for role/task usage and gate proactive shifts. They are not atomic reservations for employee calls and delegation trees. Preserve their legacy behavior. |
| `src/HireZero.Integrations.OpenClaw/OpenClawCells.cs:54` | Existing calls serialize per login, can expose owner memory, and record missing token usage as zero. Reusing a session label does not qualify isolation or bounded spending. |
| `UnqualifiedEmployeeGateway.cs:9` | The refusal to dispatch is the correct default. Extend the contract without adding an automatic fallback to the legacy owner gateway. |
| `src/HireZero.Host/Program.cs:23`, `RecoveryWorkspace.cs:8` | Startup profiles are safety boundaries. Broad normal startup prepares the owner model and registers legacy workers. Recovery intentionally restricts those services. Do not launch broad startup to preview the company UI. |
| `web/src/shell/Workspace.tsx:21`, `shell/api.ts`, `roles/types.ts:12` | The React shell already supports query-string navigation, typed API calls, errors with unknown outcomes, and role UI slots. Extend it; do not replace the frontend framework. |
| `tests/HireZero.Core.Tests/Program.cs`, `tests/HireZero.Host.Tests/Program.cs` | Checks are console executables, not an xUnit suite. Host fixtures already use temporary roots, loopback listeners, and injected providers. Keep that convention. |

Historical `src/Thaddeus.Host/CompanyMeetings.cs` in the old marketing source has useful exact-plan digests, owner grants, expiry, selected knowledge snapshots, and uncertain-outcome handling. Its runtime is tied to Marketing and its execution contract is a specific two-task pilot. Reuse the ideas, not the old runtime, data, or implementation wholesale.

## Domain and composition

Use one host process, one existing `workspace.sqlite` per data root, one shared adapter, and lightweight employee workers. No VM, container, service bus, vector database, additional runtime installation, or new database server is required.

Separate these identifiers everywhere, including JSON contracts and frontend keys:

| Entity | Identity and responsibility |
| --- | --- |
| Company | Opaque persistent `companyId`; one company per local data root initially. Bound to the configured human owner identity. |
| Department | Opaque `departmentId`; owns versioned approved knowledge and onboarding. Marketing and Engineering are initial names, not authorization keys. |
| Role definition | Stable compiled `roleId`, initially `marketing`, `developer`, later `ceo`; declares skills and policy/schema metadata. No private state or employee identity. |
| Employee | Server-generated immutable `employeeId`; company, department, role, display name, responsibility, lifecycle, revision, and authorization generation. Several employees may share a role and department. |
| Thread | Immutable employee and company ownership; private history with a monotonically increasing message sequence. |
| Task and attempt | Task has one current assignee, revisioned scope, root budget/grant and dependencies. Attempt has a unique fence and dispatch outcome. |
| Artifact | Immutable content version, digest, provenance and review record. Approval attaches to that exact version and purpose. |
| Handoff | Explicit source artifact version, recipient employee, target task or thread, and sharing authorization. Not a broadcast. |
| Company event | Structured, owner-visible status or owner-authored message. It is not the concatenation of employee transcripts. |

An employee's responsibility is descriptive context. It cannot grant repository access, tools, budget, hiring rights, or access to another employee's memory. Changing department or role is outside the first slice: retire and create a new instance. Renaming or changing responsibility preserves identity, increments revision and authorization generation, and requires pending work to be revalidated.

Keep `IRole`, `RoleCatalog`, `session.roles`, and `/api/roles` as compatibility contracts for legacy workflows. Undo the prototype's coupling from `/api/roles/{id}` into employee hiring. New instance employment uses the employee service only. Register new role skill definitions once, independently of the number of hires; register legacy Developer execution services exactly as before. Hiring a new Developer must not call `DeveloperRole.AddServices` again or start another legacy scanner.

Suggested implementation units, split by responsibility rather than one growing file:

- Core: `CompanyContracts`, `CompanySchema`, `CompanyStore`, `DepartmentKnowledge`, `EmployeeContextBuilder`, `EmployeeTaskStore`, `EmployeeScheduler`, `EmployeeAuthorization`, `EmployeeBudget`, `CompanyFeed`, and corresponding endpoint modules.
- Roles.Marketing: retain/refine `MarketingRole`, brief schema, and draft/review skills. No publishing capability.
- Roles.Developer: preserve current behavior; add an employee-facing text/advisory skill and, in a later stage, an explicit adapter to existing approved Developer work.
- Roles.Ceo, later: planning and meeting-chair skills. The orchestration enforcement and ledger remain generic Core services.
- Integrations.OpenClaw: unavailable adapter now; qualified scoped adapter later. It receives neutral context/dispatch contracts, never Marketing or CEO classes.
- Host: composition and capability reporting. Frontend: shared company views plus role-specific forms in `web/src/roles/{roleId}`.

The repository instruction that a new employee requires a role project applies to a new employee *type*. Additional instances of an existing type reuse its role definition. Do not create a project for each hire.

## Storage and migration contract

Add a namespaced migration ledger, `company_schema_migrations(version, checksum, applied_at)`. Existing stores do not share a migration version; do not repurpose global `PRAGMA user_version`. Apply ordered, checksum-verified migrations transactionally before company workers start. Reject unknown newer schemas and changed applied migration checksums. Parameterize SQL; never interpolate IDs or ordering from requests.

Use `co_` tables for the new model so the unvalidated `employee_*` prototype tables and legacy request/agent tables can remain intact. Do not automatically import prototype records or map old role IDs to real employees. If nonempty prototype tables are encountered, expose a local migration-required status, leave them untouched, and start no work from them. A later explicit importer must retain their provenance and mark pending work held. Current development starts on a fresh temporary data root, so no production migration is needed.

First-slice tables, with UTC timestamps, integer revisions, explicit state checks, and foreign keys:

| Tables | Minimum persisted fields |
| --- | --- |
| `co_company`, `co_departments`, `co_employees` | Owner identity; department approved-knowledge pointer and onboarding guide; employee role, department, responsibility, status, revision, authorization generation. |
| `co_onboarding`, `co_knowledge_versions`, `co_knowledge_reviews` | One current onboarding per department; schema version, immutable structured content, content digest, author, reviewer, approval/revocation, reason. Company knowledge uses the same versioning pattern. |
| `co_threads`, `co_messages`, `co_employee_notes` | Company and employee ownership; ordered messages and author identity; revisioned private notes. Notes are employee-private relative to other agents, readable by the human owner. |
| `co_tasks`, `co_task_dependencies`, `co_attempts` | Assignee, skill, typed input, scope revision, parent/root IDs, context ID, state/reason, assignment generation; attempt fence, lease owner/time, dispatch status, provider receipt. |
| `co_context_snapshots` | Canonical serialized admitted context, digest, company/department knowledge versions, responsibility/notes versions, history watermark, selected evidence, grant generation, adapter profile. Private data, never a feed payload. |
| `co_artifact_versions`, `co_artifact_reviews`, `co_handoffs` | Immutable body/digest and input/context provenance; exact-version reviews; source/target scope, selected evidence and revocation. |
| `co_operations`, `co_events` | Idempotency key, operation name, canonical payload digest, resulting entity/version; transactional event sequence, type, aggregate ID/version, actor, safe payload. |
| `co_grants`, `co_budget_reservations`, `co_usage` | Typed authority scope and generations; atomic reservation and settlement tied to unique dispatch IDs. Build the minimal task grant/budget now; CEO extends it later. |

Every identity-bearing row includes `company_id`. Use composite foreign keys or equivalent validated transactional relationships to prevent cross-company, cross-employee, or cross-thread references. Turn SQLite foreign keys on for every new connection. Keep connections/transactions short; set a bounded busy timeout. Use the existing database journal configuration initially; WAL is optional only after all existing connections and backup behavior are tested.

Unique constraints include `(company_id, operation_id)`, `(company_id, department_id)` for active onboarding, `(thread_id, sequence)`, event aggregate/version/type, dispatch ID, and at most one active attempt per employee. Identity generation occurs on the server. Do not accept company/owner/author identity from JSON. Avoid hard deletes; archive/retire preserves history.

Use one transaction for each state transition, associated message/artifact, operation receipt, budget adjustment and feed event. A crash before commit writes none; after commit all are visible. A retry with the same operation ID and identical canonical request returns the original receipt; changed payload returns 409. Recheck authorization even when returning a receipt. Keep original receipts immutable, and provide a separate current-state read.

Before a later real-data upgrade, stop only the target HireZero instance with owner authorization and take a consistent SQLite backup using the backup API or a fully quiesced database, including auxiliary role configuration. Never copy a live main `.sqlite` file alone. Rollback is restoration of that complete backup with the matching code; no automatic destructive down-migration.

## Onboarding and inheritance

Creating the first hire in a department atomically creates one onboarding record and designates that employee as guide. A simultaneous second hire joins the same department and sees `waiting_for_onboarding`; it cannot create a second onboarding flow. The guide has no extra authority.

State transitions: `not_started -> drafting -> awaiting_owner_review -> approved`. `changes_requested` returns to drafting. A later department update creates a new draft while the prior approved version remains current. Approval atomically advances the department pointer. Revocation invalidates admission and dispatch that depend on the revoked version. If the first guide leaves, the owner can choose another; department knowledge remains.

The first slice uses role-provided structured forms and deterministic questions. Marketing: product facts, audience, positioning, voice, supported claims/source references, exclusions, channels, review expectations. Engineering: product/repository descriptions, architecture references, standards, constraints and acceptance conventions. These descriptions grant no repository access. Shared fields include purpose, business context, boundaries and unresolved questions. Optional future model assistance proposes edits; only the owner publishes knowledge.

Later hires reference the approved department version and receive a short view of inherited knowledge plus their own responsibility. They do not inherit the first employee's chats, credentials, approvals, private notes, unfinished tasks, or grants. Inherited context is a versioned reference, not a recursive copy. New tasks pin the latest approved knowledge; running tasks retain their admitted versions unless explicitly revoked. Ordinary new knowledge does not silently rewrite an in-flight task.

Employee lifecycle: `onboarding -> active -> paused -> active`, or any state to `retired`. Onboarding-specific owner input is allowed before activation. Pausing stops new claims and cancels/withholds pending dispatch; retiring also revokes grants and preserves read-only history. Restoring work requires owner action; it never replays uncertain attempts.

## Context and memory boundaries

Build context in one consistent database read transaction at task admission or explicit re-admission. Store its digest and immutable snapshot. The allowed set is:

1. Compiled role instructions and the employee's pinned responsibility.
2. Approved company knowledge and that employee's department knowledge version.
3. That employee's private notes and messages from the selected thread through the admitted watermark.
4. Task-specific owner instructions, brief and explicitly selected evidence.
5. Handoffs addressed to this employee **and this task/thread**, still authorized.

Do not include sibling employees' notes, all department chats, the entire company feed, global `WorkingContext`, ambient owner OpenClaw memory, local session histories, filesystem contents, or unselected handoffs. CEO and meeting context have their own stricter rules below. An opaque session ID is correlation, not isolation. Construct a new task/attempt correlation ID; provider history must be disabled or independently proven scoped.

Keep instructions separate from evidence in the request contract. Retrieved text and model output are untrusted data. ContentGuard remains useful for redaction and bounds, but regex cleaning is not authorization or prompt-injection protection. The gateway has no tool channel in the first slice. Skills return typed proposals/artifacts, never executable commands.

Do not supersede every queued message merely because another message arrived, as the prototype's thread-wide version does. Assign each turn a target message and history watermark. Preserve FIFO order within an employee. Before a following turn is admitted, include the preceding completed reply and then freeze its context. Explicit replace, responsibility changes, revocation and reassignment invalidate affected attempts. A queued turn may wait for that context capture; the UI must distinguish queued from dispatched.

The owner can edit private notes with an expected revision. Employee-suggested durable memory is a proposal; the first slice requires owner approval. Promotion to department/company knowledge is a separate explicit exact-content review. No automatic cross-scope memory promotion or summarization.

## Scheduling, claims, retries and cancellation

Use one `EmployeeScheduler` hosted service with a registry of lightweight workers keyed by immutable employee ID. Each has a separate persistent queue and per-employee execution lock. Workers are created lazily and disposed on retirement; no per-worker process. A round-robin ready-employee selection and bounded active worker pool avoid one busy employee monopolizing admission. Proposed initial limits: 20 active employees, 10 queued tasks per employee, four concurrent local preparations, one model dispatch per account/principal.

Task states: `queued`, `blocked`, `running`, `awaiting_review`, `completed`, `failed`, `canceled`, `superseded`, `unknown`. `blocked` has a typed reason, such as onboarding, dependencies, provider unavailable, budget, missing grant or owner pause. Attempt phases: `claimed -> reserved -> dispatching -> response_received -> committed`, with terminal `not_sent`, `failed`, `canceled`, `superseded`, or `unknown`.

Claim in a short `BEGIN IMMEDIATE` transaction: validate active employee, ready dependencies and policy; choose FIFO task; create attempt/fence and lease; change task state conditionally. Enforce the one-active-attempt constraint in the database, not only a semaphore. Never hold a SQLite transaction while awaiting the provider or a process lock.

Acquire the shared principal dispatch gate, then recheck task generation, employment, current grant, revocation, selected evidence, budget and execution profile. Reserve usage atomically and durably mark `dispatching` immediately before the outbound call. Check the same fence and authorization again in the result-commit transaction. A late response after reassignment, revocation or cancellation may settle usage and be retained as withheld attempt evidence, but cannot publish a reply, approve an artifact or complete a current task.

Per-task cancellation uses linked cancellation tokens plus a persisted generation change. `cancel_requested`/withheld outcome may be shown while an in-flight provider is unresolved; do not claim a remote call stopped just because HTTP was canceled. Owner cancellation, employee pause/retirement, root mandate cancellation and meeting cancellation all reach this path.

Retry rules:

- Deterministic pre-dispatch validation failure or database contention may be retried within a bounded local policy; never re-run a committed command mutation.
- Provider unavailable blocks without consuming a call or dropping saved input. Default retry policy for model calls is none.
- A provider-declared definitive rejection can be shown as known failure, with an owner-requested fresh attempt after revalidation.
- Timeout, lost response, process death after dispatch started, or unclear acceptance is `unknown`. Retain the reservation; do not dispatch again automatically. Reconcile by a proven provider receipt/query if available. A new owner-requested attempt clearly warns about possible duplicate use and consumes a new reservation.
- An expired pre-dispatch claim can return to queued only if the durable phase proves it was not dispatched. On startup, any ambiguous dispatched attempt becomes unknown. Disable automatic HTTP retries around provider calls.

One host owns the development data root. Add a small process ownership guard for the company scheduler; reject a second live company scheduler using the same root. Database constraints and fences still protect against stale workers. Do not inspect, stop or claim unrelated OpenClaw processes.

## Server authority and budgets

All company endpoints are human-owner-only in the first release. Derive company and owner from verified host identity. Use the existing same-origin JSON/header protection and host validation, including missing Origin, spoofed forwarding, loopback and oversized body tests. A local test fixture must not weaken the production middleware. Return 404 for inaccessible resource IDs and 403 for disallowed capabilities. Only the server can label an actor `owner`; model-authored JSON cannot.

Use `effective capability = compiled skill capability AND employee policy AND current task grant AND owner policy AND execution-profile capability`. Grants identify issuer, authority source, employee IDs, skill/action kinds, selected resources, exact scope/plan digest, generation, expiry, root task, delegation depth and budget. Checks occur at submission, claim, dispatch and commit. Approval of content, knowledge, or a meeting outcome never implies permission to publish, send, spend, edit repositories or change credentials.

The first slice permits local records, private text, internal drafts and reviews. It has no external-action adapter. A developer employee can propose a coding task; its proposal is not a legacy coding approval. A later bridge must create a linked legacy request under existing approval rules and await the same exact-source/strategy/publication approvals. Never fabricate these from a CEO instruction or copy an old approval. Keep one authoritative execution queue for each bridged action.

Add reservation accounting for new employee work without silently redefining the legacy BudgetLedger. Each dispatch has one durable reservation and one settlement, counted against all applicable company, department, employee, root-task/mandate/meeting limits. Enforce the strictest remaining limit. Siblings share their root allowance; reassignment, retries, new child IDs, revision or meeting follow-up do not reset it. Scope limits should not block unrelated departments unless a company-wide limit is exhausted.

The gateway contract must return structured usage and outcome: dispatch ID, provider receipt if available, route/model, accepted/not-sent/unknown, input/output/total usage, finish reason and response. Replace `Task<string>`; do not silently convert missing usage to zero. Reserve a provable maximum including input, output and any billable reasoning. Release only when no send is proven, or settle against trusted actual usage; retain conservative amounts on uncertainty. If the route cannot bound all billed usage, live bounded work stays disabled.

Proposed offline defaults: one model call per ordinary attempt, at most 4,000 input and 1,000 output tokens, a 60-second attempt deadline, no automatic model retries. Context packing is deterministic, keeps required policy/brief/evidence, and reports omitted optional history. Do not silently cut mandatory evidence; ask for smaller scope. Character count is not a verified token bound. Fake providers expose a deterministic tokenizer for tests; live routes need compatible counting and hard output limits.

For test scenarios, start with 40,000 tokens per delegation root and 50,000 per meeting, with the other structural limits below. These are proposed ceilings, not authorization to spend. Normal company execution remains unavailable/zero-live-budget until the owner approves a model route and actual limits. Admission must reject a plan whose required reservations exceed its root. Unknown usage remains visible and prevents over-allocation. If existing legacy calls cannot share the account gate or budget ledger, describe new limits as company-module limits and do not claim an account-wide spending cap.

## Company feed and API contracts

Use `co_events` as both the append-only audit/event source and the directly queried first-slice feed. Insert the safe user-facing event in the same transaction as the underlying mutation. This removes the need for a message broker or a second asynchronous projector. If a materialized feed is later necessary, consume event sequence idempotently with a durable cursor and unique event ID; never emit an event before the originating transaction commits.

Events contain company sequence, event ID, UTC time, actor type/ID, employee/department/task references, aggregate version, event type and safe display fields. Examples: employee hired, onboarding approved, task assigned, provider blocked, draft ready, review requested, handoff shared, meeting outcome accepted. Generate status text from committed state; never take a model's claim that work was completed. No private note or transcript excerpts by default. An explicit owner share may add selected text with provenance.

Keep legacy `/api/home` and request routes operational under their existing profiles. New routes use `/api/company`; every instance ID here is an employee ID, never a role ID. All mutations take `requestId` (UUID) and `expectedVersion` where a mutable resource exists. Include `code`, readable `error`, current version where safe, and correlation ID on errors. Use 201 for creation, 202 for accepted queued work, 200 for idempotent replay/read, 409 for stale state or changed idempotency payload, 422 for invalid domain input, 503 for unavailable infrastructure when the input was not admitted. A saved task blocked on a provider is a successful admission with a blocked state, not a lost request.

| Route | Contract |
| --- | --- |
| `GET /api/company` | Company, capability profile, role catalog, employee/department counts, provider qualification and execution availability. |
| `GET/POST /api/company/departments` | List/create departments. Create returns department and current onboarding status. |
| `GET/PUT /api/company/departments/{id}/onboarding` | Read/save structured draft with expected version and role schema version. |
| `POST /api/company/departments/{id}/knowledge/reviews` | Approve/reject exact draft version and digest. Revocation is a distinct owner command. |
| `GET/POST /api/company/employees` | List/hire `{roleId, departmentId, name, responsibility, requestId}`. Return generated ID, employment and inherited knowledge pointer. |
| `GET/PATCH /api/company/employees/{id}` | Read/rename/change responsibility using version checks. Lifecycle changes use `/lifecycle` with explicit action. |
| `GET/PUT /api/company/employees/{id}/notes` | Owner-managed private notes with expected version. |
| `GET/POST /api/company/employees/{id}/threads` | List/create private threads. Detail and messages are under the exact employee/thread pair. |
| `POST .../threads/{threadId}/messages` | Persist human text once; `requestReply` explicitly requests a text task. Return message and optional task IDs/status. |
| `GET/POST /api/company/tasks` | List/filter or assign `{employeeId, threadId, skillKind, input, briefVersion, evidenceIds, requestId}`. Resolve role and scope server-side. |
| `GET /api/company/tasks/{id}` | Task, attempts, selected context metadata, artifacts, grant/budget summary and allowed owner actions. Raw context only through a separate owner inspection endpoint. |
| `POST /api/company/tasks/{id}/commands` | Typed cancel/pause/reassign/retry/complete command. Reject invalid transitions. Reassign revokes old fences and creates fresh admission. |
| `POST /api/company/tasks/{id}/artifacts` | Owner-authored artifact with explicit manual provenance; useful offline. |
| `POST /api/company/artifacts/{id}/reviews` | Decision on exact artifact version/digest and input knowledge/brief provenance. |
| `POST /api/company/handoffs` | Exact approved artifact version plus target employee and target task/thread. Owner explicitly authorizes the sharing. |
| `GET /api/company/feed?after={seq}&limit={n}` | Ordered safe events with cursor and `hasMore`; max 100 per response. A snapshot read returns its feed cursor in the same transaction. |
| `POST /api/company/messages` | Main company message, explicit target employee or owner-note purpose. Never implicitly broadcast. Later defaults can target the selected CEO. |
| `GET /api/company/operations/{requestId}` | Reconcile an uncertain HTTP outcome before offering a new mutation. |

Company knowledge editing/review follows the same draft/digest contracts as department knowledge. Task artifact bodies and other large fields use dedicated bounded responses. Default request payload remains within the host's 16 KiB bound; use separate records for evidence instead of inflating a single command. Do not include credential input fields.

Suggested response types include `EmployeeSummary {id, roleId, departmentId, name, responsibility, state, version, knowledgeVersion, queueCount}`, `TaskView {id, employeeId, state, reasonCode, version, attempts, allowedActions}`, and `CapabilityProfile {companyEnabled, localManagement, modelExecution, providerStatus, ceoEnabled, meetingsEnabled, legacyDeveloperAvailable}`. Report capabilities from host composition and adapter readiness, not hardcoded optimistic flags or the presence of a role name.

## Frontend and safe preview

Extend the existing shell with a Company workspace, a Departments/Employees panel, employee drill-down and later Meetings. Company becomes the owner landing view when this module is enabled; existing Developer chats and work remain reachable through the existing workspace route. Preserve old `open` links and role slots. Keep human access administration distinct from AI employment.

The first UI supports:

- Hire an employee with department, role and responsibility; show inherited knowledge and first-hire onboarding status.
- Edit department onboarding, compare/review the exact draft, and visibly publish its approved version.
- Open an employee panel with private chat, notes, tasks, role-specific brief form, artifacts and context provenance.
- Main company chat/feed with attributed, durable updates and employee/task drill-down links. Clicking a link never changes execution state.
- Queue, blocked reason, owner review, cancel, retire, retry-after-reconciliation and manual artifact controls. Hide or disable unavailable actions with a specific reason.

Use the existing API helper and explicit operation IDs retained across lost-response retries. State is keyed by company/employee/thread, and is cleared on identity changes. Prevent late responses from a previously selected employee rendering in another's panel. Escape all content, keep Markdown raw HTML disabled, and allow only known internal route or safe external link schemes. Abort polling for hidden/unmounted views; use cursor polling at roughly two seconds while active and slower when idle. SSE is optional later, not a prerequisite.

Add role UI slots for employee-specific brief/artifact panels instead of writing Marketing conditions in the shared shell. Company components live under `web/src/company`; new role forms under `web/src/roles/marketing`, and later `ceo`. Use the existing visual theme; no framework or design-system replacement.

For deterministic preview, add an explicit company-local composition profile in the **same host**. It uses a fresh selected data root and loopback listener, the company stores and unavailable model gateway, and no legacy model preparation, discovery timer, connector, coding runner, session watcher or runtime startup. Test fixture provider injection remains internal to tests; an HTTP request cannot select it. A demo, if needed, must be explicitly labelled and use a disposable root. Reuse request-hygiene logic without weakening existing host profiles. Use a separate unused port for previews, and never stop another listener to make it fit.

Keep HeldWorkspace behavior exactly held. The existing Recovery and normal profiles retain their current behavior unless a separately tested, additive company-module option is enabled. Remove the prototype's unconditional company worker registration from existing profiles while introducing explicit composition; do not accidentally expose new write routes just by starting recovery. New profile capabilities drive the UI so it does not poll unavailable legacy endpoints or show false runtime health.

## CEO stage

The CEO is an employee instance of a compiled CEO role, normally in a Leadership department. It receives approved company knowledge, explicitly shared department summaries, permitted company task/status metadata, and selected evidence. It has no automatic access to other employees' private chats or notes. It can propose work and manage work within an owner-issued mandate; it cannot grant itself authority.

Mandate states: `draft -> awaiting_owner -> active -> paused/completed/expired/revoked`. Delegation plan states: `draft -> proposed -> approved -> executing -> awaiting_owner_review -> completed`, with `blocked`, `canceled`, `superseded` and `needs_reconciliation` branches. Store mandate versions, exact plan digests and the authority source. Prompt text cannot transition approval states.

First CEO delivery requires owner approval of each exact plan. Once proven, an explicit reusable owner mandate may permit internal task creation within typed limits: allowed employees/departments, skill kinds, evidence scope, objective, maximum tasks/attempts, deadline and shared budget. Both flows call the same authorization service. The CEO cannot hire/fire, change grants, raise budgets, add adapters or select a fallback model.

Proposed initial structural limits: at most four child tasks per root wave, one level of delegation below the CEO, one revision attempt per child, and a finite root deadline. Count every generated task and revision in the root lifetime even if canceled or renamed. Allow a new wave only under new explicit owner approval; retain parent accounting. Reject cycles, self-delegation, duplicate normalized task fingerprints and dependencies on descendants. Other employees return handoff requests or proposals; they do not silently spawn more employees/tasks.

The server validates model plans against a strict schema and allowed skills, creates approved children in one transaction, and emits one event per actual transition. CEO status summaries are derived from task states and cited artifact versions. A task is not complete because the CEO says it is. Missing evidence or exhausted limits moves the plan to blocked/needs owner. Human pause, revoke, cancel or reassignment wins at the next enforcement check and invalidates stale results.

API additions: `/api/company/mandates`, `/mandates/{id}/commands`, `/plans`, `/plans/{id}/reviews`, and `/plans/{id}/commands`. UI: show objective, proposed assignees, dependencies, selected evidence, limits and expected outcomes before approval; expose root-level pause/cancel and per-task drill-down.

## Board Meeting stage

Meetings use employee instances, not role names, so both Marketing employees can participate with different responsibilities. The owner selects agenda, participants, chair, approved evidence versions, intended outcome and budget. The CEO facilitates when selected; a meeting does not create a superior authority above the owner.

Meeting states: `draft -> ready -> in_progress -> outcome_proposed -> awaiting_owner -> closed`. Side states: `paused`, `blocked`, `canceled`, `needs_reconciliation`. Opening pins participants, agenda revision and evidence/context digest. Turn states reuse task attempts. An edited agenda, participant list or evidence packet requires re-approval/reopening with a new revision; existing records remain visible.

Use a separate meeting context builder. Shared turns see the approved meeting packet, approved knowledge selected for the meeting, participant responsibility and public meeting transcript. They do not see that employee's private chat/notes or unrestricted department knowledge. An employee may prepare a private briefing in a normal employee task; the owner explicitly attaches an approved version to the meeting before it is shared. This prevents indirect leakage through a participant's prompt.

Start with a deterministic speaking order and structured turns: each participant contributes, one bounded response round, then an outcome proposal using the remaining turn allowance. The chair's synthesis counts as a turn. Default ceiling: five AI participants including chair, two turns per participant, ten total model calls, 15-minute meeting deadline, and an explicit aggregate reservation budget. A one-participant meeting has at most two turns; no special unlimited chair allowance. Run turns serially through the same account gate. Owner notes do not consume model turns but remain size-bounded.

Outcome schema: decisions, cited evidence versions, dissent/uncertainty, open questions and proposed actions with employee, skill, dependencies and acceptance criteria. Acceptance of minutes closes the discussion only. Dispatch of follow-up tasks is a separate exact-plan approval or a demonstrably applicable existing mandate, with remaining root budget. It cannot grant external effects. A canceled or revoked meeting cannot create follow-ups. Unknown turn outcomes pause for reconciliation; never resume them automatically after restart.

Persist `co_meetings`, `co_meeting_participants`, `co_meeting_evidence`, `co_meeting_turns`, `co_meeting_outcomes`, and outcome reviews in staged migrations. API: `/api/company/meetings`, `/{id}/commands`, `/{id}/evidence`, `/{id}/turns`, `/{id}/outcomes`, and `/outcomes/{id}/reviews`. Each command uses exact version/digest and an idempotency key. Company feed receives start/pause/outcome/action status summaries with drill-down, not the entire meeting transcript or private preparation.

## Implementation stages and verification gates

### Stage 0 Establish the executable baseline

Inspect git status and checkpoint first; do not reset away the prototype. Confirm free disk space before builds and keep at least 10 GiB free. Use a new temporary data root for every synthetic integration scenario. Record exact commands, SDK, return codes and compact receipts. No live providers, credentials, production state or GitHub Actions.

Known environment issues from prior work: installed SDKs were 9.0.305, 10.0.103 and 10.0.203, while global.json requests 10.0.302 with latestFeature. The existing 10.0.203 MSBuild was successfully invoked directly; do not rewrite global.json or install a new SDK silently. The frontend offline install stopped at uncached `zwitch@2.0.4`, leaving ignored partial node_modules. Resolve dependency availability explicitly before claiming frontend validation; no manifest upgrades solely to bypass the cache. No tooling changes were made in this planning turn.

If the installed SDK is still the same, the previously successful bounded invocation was:

```powershell
$env:MSBuildSDKsPath = 'C:\Program Files\dotnet\sdk\10.0.203\Sdks'
& 'C:\Program Files\dotnet\dotnet.exe' exec 'C:\Program Files\dotnet\sdk\10.0.203\MSBuild.dll' tests/HireZero.Core.Tests/HireZero.Core.Tests.csproj -restore -nologo -verbosity:minimal -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false
```

Use that only after rechecking availability, and execute the resulting test DLL separately with the installed runtime. RestoreIgnoreFailedSources does not make a command offline; constrain package sources/network explicitly when an offline run is intended. Audit was disabled in the prior cached restore only; it is not a dependency security approval.

### Stage 1 Deliver the company workspace slice

Implement schema and domain boundaries, instance hiring/onboarding, private threads/notes, task admission/attempt fences, context snapshots, manual artifacts, review/handoff, transactional feed, typed API and UI. Add the explicit unavailable-provider local profile. Update solution entries for Marketing and new tests; keep dependencies minimal and existing lockfile versions intentional.

Demonstration gate, using fictional content: hire Marketing Strategy and Marketing Copy plus Developer Backend and Developer Frontend; approve one Marketing brief and one Engineering brief; show the second hire inherits only approved department knowledge; assign separate work; create/review/share an owner-authored product fact artifact to a targeted Marketing task; refresh/restart and show consistent private histories, queued/blocked work and company events. In deterministic tests, generated artifacts traverse the same flow through a fake provider. In normal preview, the UI clearly says model execution is unavailable.

This is the first reviewable deliverable: usable management UI, persistent data, source changes, passing offline checks and an honest capability status. It must not be reported as live autonomous multiemployee execution.

### Stage 2 Add bounded CEO planning and task control

Implement CEO role, typed plans, mandates, approvals, dependency validation, aggregate budgets and owner overrides. Use the existing task and event path. Gate: a scripted fake CEO proposes four correctly assigned tasks; the owner approves; task creation is idempotent; children cannot expand their own authority; a pause/revoke during dispatch prevents result publication; root accounting survives restart and retries.

### Stage 3 Add Board Meetings

Implement meeting ledger, selected evidence/context, finite speaking turns, structured outcome review and separate follow-up authorization. Gate: two departments with several instances hold a deterministic fake-provider meeting; unselected private sentinels never appear in any participant request; turn/budget/deadline limits stop the meeting; stale outcome approval and cancellation cannot dispatch actions; restart never replays an unknown turn.

### Stage 4 Qualify live text execution and Developer bridges separately

First qualify the adapter with fake transport and recorded synthetic receipts. Actual runtime compatibility/authentication is unresolved, so a small live pilot needs a later owner-approved route and budget. No sign-in, runtime repair, live agent execution or credential copying belongs in the present implementation stages. Preserve per-login serialization. Do not claim simultaneous model execution because independent workers exist.

Qualification must prove: no ambient history or global retrieval, no tools, isolated employee/thread/meeting requests, exact provider routing with fallback disabled, bounded complete usage, conservative unknown outcome handling, cancellation/result fencing, and no interference with unrelated runtime sessions. Test cross-employee sentinel prompts and inspect actual request envelopes; a model's claim that it forgot something is not isolation evidence. If the installed OpenClaw route cannot meet this contract, keep it unavailable and report the precise missing capability. Do not solve it by silently creating per-employee runtimes.

Bridge Developer execution only after scoping its tools, repository grants, memory and accounting to a specific employee task. Existing Developer workflows remain available through their legacy routes throughout. A source link to a legacy request is safe earlier; dispatch is not. An adapter that calls the unrestricted legacy responder or owner cell does not meet this gate.

## Test matrix

Add meaningful checks to the existing executable suites, with synthetic clocks, controllable providers and independent database connections. Do not count simple object construction or tests duplicating the implementation as security evidence.

| Area | Required failure-oriented checks |
| --- | --- |
| Identity and onboarding | Two same-role hires have distinct persisted IDs; concurrent first hires create one onboarding; second hire sees only approved version; retired guide does not delete department knowledge; role-module toggle does not fire instances. |
| Ownership and isolation | Wrong company/employee/thread/task/artifact IDs denied; alternate owner fixture denied; private sentinels excluded from sibling/CEO/meeting contexts; targeted handoff not visible in unrelated tasks; revocation blocks queued and waiting dispatch. |
| Context | Atomic snapshot under concurrent note/knowledge edits; correct history watermark and preceding reply; deterministic truncation; mandatory evidence never silently dropped; new responsibility/assignment invalidates old fence. |
| Concurrency | Two claimants cannot both own one employee; separate employees have independent queues; account gate serializes fake dispatch; fairness under a long queue; pause while waiting gate is rechecked; stale worker cannot commit. |
| Operations and feed | Same request ID returns same receipt; changed body conflicts; atomic rollback leaves no orphan message/job/feed event; crash after commit still yields one event; pagination/reconnect has no gaps or duplicates; feed excludes private content. |
| Retries and restart | Pre-send crash recoverable; after-send crash unknown; no automatic replay on timeout; missing provider usage held; cancel after response before commit withholds content but accounts for use; expired lease cannot resurrect dispatched work. |
| Review and handoff | Exact digest/version required; changed brief/withdrawn evidence invalidates current approval; approved old artifact remains auditable but not silently current; creative approval never grants publication. |
| Budgets and CEO | Concurrent reservations cannot overspend; child/retry/reassignment share root cap; unknown usage not zero; plan cycles/duplicates/depth/task count rejected; model-supplied owner/approval/grant fields rejected; owner override wins. |
| Meetings | Revisions invalidate old outcome approval; private preparation excluded; chair synthesis counts; participant/turn/time/token caps enforced; canceled meeting cannot create children; accepted minutes alone cannot dispatch. |
| HTTP and UI | Wrong/missing Origin, header, host, spoofed forwarding, non-owner and oversized body denied; role/employee IDs never confused; stale UI fetch cannot cross employee panels; Markdown/script links safe; lost response reconciles operation rather than duplicating work. |
| Compatibility | Existing Core, Developer, Policy and Integration checks relevant to touched contracts pass; Host held/recovery tests still prove no unintended runners, provider preparation or replay; existing frontend approval/feedback/session-hook tests pass. |
| Migration | Fresh/repeated migration; prior schema upgrade with fictional records; newer-version refusal; interrupted transaction rollback; prototype rows preserved/held; legacy table sentinels unchanged. |

Run smallest targeted suites during each change, then the touched regression suites at each gate. Typical existing commands are `dotnet run --project tests/HireZero.Core.Tests`, `...HireZero.Host.Tests`, `...HireZero.Developer.Tests`, `...HireZero.Policy.Tests`, `...HireZero.Integrations.Tests`, and `node --test tests/approval-ui.test.mjs tests/execution-feedback.test.mjs tests/session-hook.test.mjs`, subject to the SDK workaround above. Build the frontend with its existing `tsc -b && vite build` script after locked dependencies are available. Test new UI behavior with deterministic fixtures; a TypeScript build alone does not validate interactions. Do not introduce a large test framework just to render one screen if existing Node/React tooling and a focused browser check suffice.

## Remaining unknowns and reporting

No product clarification blocks the offline slice. Treat the proposed limits and lifecycle rules as conservative initial implementation defaults and make them typed/configurable by the owner within server ceilings. Report substantive deviations before changing the approved model.

Open issues: installed gateway compatibility and auth/access refusal; trustworthy provider usage/limits; safe sharing of a global account gate with legacy calls; frontend package availability; the installed SDK mismatch; eventual scoped Developer execution; and the suitability of the current browser tooling for UI verification. These limit validation/live activation, not the ability to implement and test the company domain offline.

At each stage deliver the commit/reference, files changed, exact checks run with results, a short demonstrated user flow, current capability status, and remaining blockers. Preserve test roots only when needed to inspect a failure; otherwise clean up owned disposable outputs after processes exit. Never remove user data or another task's files/processes. Do not publish, deploy, start paid work, sign in, or migrate existing production data as part of this handoff.
