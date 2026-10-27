# Architecture decision records

Short, numbered records of decisions that shape HireZero. Each has a status (Proposed, Accepted, Superseded) and
is never rewritten after acceptance; a changed decision gets a new record that supersedes the old one.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-three-layers.md) | Core, roles and integrations stay separate | Accepted |
| [0002](0002-employee-module-contract.md) | One employee module contract | Accepted |
| [0003](0003-owner-approval-safety-model.md) | Owner approval and bounded execution | Accepted |
| [0004](0004-pinned-agent-runtimes.md) | Agent runtimes are pinned and verified | Accepted |
| [0005](0005-offline-checks-on-xunit.md) | Offline checks on xUnit | Accepted |
| [0006](0006-compiled-in-module-discovery.md) | Employee modules are compiled in and discovered at startup | Accepted |
| [0007](0007-tool-registry.md) | Employees reach the world through registered tools | Accepted |
| [0008](0008-model-routes.md) | Model routes come from one catalog; one provider contract follows | Accepted, partly superseded by 0010 |
| [0009](0009-legacy-features-are-optional-integrations.md) | Every legacy feature is kept, as an optional integration | Accepted |
| [0010](0010-model-provider-contract.md) | The company provider contract is the model provider contract | Accepted |
| [0011](0011-employee-actions.md) | Employee actions gather evidence the owner reviews | Accepted |
| [0012](0012-company-publication.md) | Publishing reviewed execution through exact calls | Accepted |
| [0013](0013-execution-limits-approved-with-the-grant.md) | Execution limits are approved with the grant | Accepted |
| [0014](0014-pinned-codex-runtime.md) | Live company coding runs on a pinned Codex runtime | Accepted |
| [0015](0015-publishing-an-approved-artifact.md) | Publishing an approved artifact | Accepted |
| [0016](0016-publishing-one-approved-call.md) | Publishing one approved external call | Accepted |
| [0017](0017-publishing-a-step-from-an-action.md) | Publishing a step from an action | Accepted |
| [0018](0018-module-storage-migrations.md) | Module storage migrations | Accepted |
| [0019](0019-retiring-the-legacy-profiles.md) | Retiring the legacy profiles | Accepted |
| [0020](0020-members-roles-and-sign-in.md) | Members, roles and sign-in | Accepted |
| [0021](0021-companies-per-data-root.md) | Companies per data root | Accepted |
| [0022](0022-tamper-evident-audit-log.md) | Tamper-evident audit log | Accepted |
| [0023](0023-trusted-postgres-in-live-coding-runs.md) | Trusted PostgreSQL in live coding runs | Accepted |
| [0024](0024-opentelemetry-traces-and-metrics.md) | OpenTelemetry traces and metrics | Accepted |
| [0025](0025-one-schema-two-databases.md) | One schema, two databases | Accepted |
| [0026](0026-container-and-releases.md) | Container, releases and advisories | Accepted |
| [0027](0027-secrets-from-files.md) | Secrets from files, vault-neutral | Accepted |
| [0028](0028-typed-settings.md) | Typed settings | Accepted |
| [0029](0029-marketing-analytics-reads.md) | Marketing analytics reads | Accepted |
| [0030](0030-ab-test-design-and-significance.md) | A/B test design and significance | Accepted |
| [0031](0031-ads-read-contracts.md) | Ads read contracts | Accepted |
| [0032](0032-google-ads-provider.md) | Google Ads provider | Accepted |
| [0033](0033-approved-ad-changes.md) | Approved ad changes | Accepted |
| [0034](0034-support-employee.md) | Support, the third employee | Accepted |
| [0035](0035-email-and-crm-read-contracts.md) | Email and CRM read contracts | Accepted |
| [0036](0036-api-model-provider.md) | API model provider | Accepted |
| [0037](0037-loaded-action-choices.md) | Loaded action choices | Accepted |
| [0038](0038-integration-setup-in-the-app.md) | Integration setup in the app | Accepted, partly superseded by 0039 |
| [0039](0039-integrations-start-without-a-restart.md) | Integrations start without a restart | Accepted |
| [0040](0040-the-muse-layout.md) | The Muse layout for the company workspace | Accepted |
| [0041](0041-context-drafted-from-sources.md) | Department context drafted from sources | Accepted |
| [0042](0042-openrouter-low-effort-and-a-price-ceiling.md) | OpenRouter calls use low effort and a price ceiling | Accepted |
| [0043](0043-context-after-onboarding-and-company-facts.md) | Context after onboarding, and company facts | Accepted |
| [0044](0044-local-company-backups.md) | Local company backups and staying running | Accepted |
| [0045](0045-offline-packages-for-coding-runs.md) | Offline packages for coding runs | Accepted |
| [0046](0046-connect-cards.md) | Connect cards in the conversation | Accepted |
| [0047](0047-conversation-packet.md) | A larger packet for conversational replies | Accepted |
| [0048](0048-assistant-connect-requests.md) | Assistants can ask for connections, and point at keys | Accepted |
| [0049](0049-google-sign-in.md) | Sign in with Google for Analytics and Search Console | Accepted |
| [0050](0050-company-scoped-browser-recovery.md) | Company-scoped browser recovery | Accepted |
| [0051](0051-context-first-company-records.md) | Context-first company records | Accepted |
| [0052](0052-pre-hire-company-drafting.md) | Company drafting before hiring | Accepted |
| [0053](0053-brief-to-first-assignment.md) | Approved brief to first assignment | Accepted |
| [0054](0054-bounded-business-discovery.md) | Bounded business website discovery | Accepted |
| [0055](0055-business-history-and-provenance.md) | Business brief history and employee provenance | Accepted |
| [0056](0056-private-documents-and-retrieval.md) | Approved private documents and bounded retrieval | Accepted |
| [0057](0057-source-freshness-and-owner-review.md) | Source freshness and explicit owner assessments | Accepted |
| [0058](0058-bounded-recurring-work.md) | Finite recurring work with explicit owner authority | Accepted |
| [0059](0059-durable-inbox-and-bounded-notices.md) | Durable inbox and bounded owner email notices | Accepted |
| [0060](0060-structured-organizational-memory.md) | Structured organizational memory and explicit renewal | Accepted |
| [0061](0061-monthly-usage-admission-and-cost-receipts.md) | Monthly usage admission and immutable price snapshots | Accepted |
| [0062](0062-company-owned-private-connections.md) | Company-owned private connections and consent callbacks | Accepted |
| [0063](0063-explicit-runtime-release-receipts.md) | Explicit runtime stop and temporary-file cleanup receipts | Accepted |
| [0064](0064-cross-company-checkout-leases.md) | Cross-company physical checkout leases | Accepted |
| [0065](0065-bounded-sandbox-provider.md) | Bounded on-demand sandbox provider and required composition | Accepted; implementation in progress |
| [0066](0066-durable-sandbox-authority-and-allocation.md) | Durable sandbox authority, recovery and runtime allocation | Accepted; lifecycle and host fixtures verified |
| [0067](0067-script-review-ui-and-recoverable-queue.md) | Script review UI, owner revocation and recoverable host queue | Accepted; fictional E2E verified |
| [0068](0068-qualified-runtime-activation.md) | Qualified runtime activation and independent recovery | Accepted; real qualification pending |
| [0069](0069-reviewed-sandbox-output-reuse.md) | Reviewed sandbox output reuse and revocable provenance | Accepted; fictional E2E verified |
| [0070](0070-unapproved-document-erasure-and-retention.md) | Unapproved document erasure and opt-in discarded-draft retention | Accepted; fictional E2E verified |
| [0071](0071-tracked-first-deliverable.md) | Tracked first deliverable with bounded output and owner-reviewed completion | Accepted; fictional E2E verified |
| [0072](0072-company-progress-from-saved-history.md) | Company progress from saved milestones, field edits, work and cost coverage | Accepted; fictional E2E verified |
| [0073](0073-measured-delegation-and-approved-dependencies.md) | Measured delegation, exact proposal authority and approved dependency results | Accepted |
| [0074](0074-goal-based-team-setup.md) | Goal-based team suggestions, role-owned setup and first assignments | Accepted |
| [0075](0075-bounded-public-browser-foundation.md) | Bounded public-browser broker and driver foundation | Accepted; durable provider and qualification pending |
| [0076](0076-browser-worker-protocol-and-output-fence.md) | Bounded browser worker protocol and returned-output fence | Accepted; fictional process-boundary E2E verified |
| [0077](0077-browser-resource-execution-and-recovery.md) | Browser resource execution and non-replaying recovery | Accepted; offline adapter checks, real qualification pending |
| [0078](0078-durable-browser-authority-and-reviewed-captures.md) | Durable browser authority, shared compute and reviewed captures | Accepted; host/UI composed in 0079, production qualification pending |
| [0079](0079-browser-queue-and-owner-review-journey.md) | Browser host queue, owner itinerary controls and captured-page review | Accepted; fictional product journey, production qualification pending |
| [0080](0080-browser-capture-retention.md) | Owner capture deletion, optional expiry and preserved excerpt proofs | Accepted; current-record deletion, backup lifecycle separate |
| [0081](0081-stripe-billing-and-purchased-allowances.md) | Stripe adapter, durable checkout, verified payment and purchased allowance admission | Accepted; host/UI composed in 0082, launch terms pending |
| [0082](0082-billing-host-and-owner-journey.md) | Billing host, durable signed events, portal and owner checkout journey | Accepted; fictional desktop/mobile E2E, merchant and hosted acceptance pending |
| [0083](0083-reviewed-scripts-in-delegated-plans.md) | Selected predecessor evidence, exact script approval and reviewed plan completion | Accepted; production runtime qualification remains pending |
| [0084](0084-reviewed-browsers-in-delegated-plans.md) | Plan browser authority, reviewed browser-to-script evidence and per-task execution admission | Accepted; fictional desktop/mobile E2E, production qualification pending |
| [0085](0085-company-owned-model-connections.md) | Company-owned API model connections and funding receipts | Accepted; fictional backend and owner journey verified |
| [0086](0086-paid-extended-scripts.md) | Paid extended-script entitlement, separate runtime qualification and durable allocation | Accepted; fictional lifecycle and owner journey verified, production qualification pending |
| [0087](0087-recurring-reviewed-scripts.md) | Finite recurring script reports, approved input revisions and separate schedule authority | Accepted; fictional lifecycle and desktop/mobile journeys verified, production qualification pending |
| [0088](0088-result-feedback-memory.md) | Result-linked owner feedback, employee-scoped drafts and current citation recovery | Accepted; fictional lifecycle and owner journey verified locally |
| [0089](0089-reviewed-word-text-import.md) | Bounded Word body-text import into private drafts with separate owner approval | Accepted; fictional extraction, authority and owner journey verified locally |
| [0090](0090-selected-drive-document-copies.md) | Company-scoped selected Drive reads and separately approved document copies | Accepted; fictional lifecycle and owner journey verified locally; live Google acceptance pending |
| [0091](0091-owner-reviewed-memory-consistency.md) | Owner-authorized memory consistency reports with exact citations and measured recovery | Accepted; synthetic verification, real model quality acceptance pending |
| [0092](0092-explicit-purpose-model-routing.md) | Optional analysis model declared by measured task purpose with exact routing approval | Accepted; fictional verification, real cost and quality acceptance pending |
| [0093](0093-recurring-measured-deliverables.md) | Repeat accepted measured work with its skill, selected references and finite approval | Accepted; fictional lifecycle and desktop/mobile journeys verified, real usefulness and retention pending |
| [0094](0094-owner-reviewed-memory-revisions.md) | Cited memory suggestions become editable drafts with separate replacement approval | Accepted; fictional lifecycle and desktop/mobile journeys verified, real suggestion quality pending |
| [0095](0095-qualified-browser-activation.md) | Browser-specific qualification, default-off registration and independent readiness monitoring | Accepted; offline and fictional owner-journey verification, real runtime qualification pending |
| [0096](0096-recurring-website-reviews.md) | Finite recurring public itineraries, exact consent and separately reviewed captures | Accepted; fictional lifecycle and desktop/mobile journeys verified, production runtime and usefulness pending |
| [0097](0097-task-cost-reconciliation.md) | Owner-reviewed task costs, explicit unknowns and immutable corrections | Accepted; fictional accounting and desktop/mobile recovery verified, actual cost inputs pending |
| [0098](0098-reviewed-pdf-text-import.md) | Process-bounded selectable PDF text previews with separate document approval | Accepted; fictional parser and owner journey verified, OCR and visual fidelity excluded |
| [0099](0099-revoked-document-library-deletion.md) | Exact owner deletion of revoked library sources with retained task-copy boundaries | Accepted; local verification recorded in history, full erasure remains separate |
| [0100](0100-browser-image-build-candidate.md) | Digest-pinned Linux browser build candidate with protected files and an explicit non-qualification inventory | Local build and fictional runsc qualification recorded; production review and activation remain separate |
| [0101](0101-completed-task-cost-progress.md) | Owner-only completed-task cost coverage and exact reconciliation totals in Progress | Accepted; local verification recorded separately, actual cost inputs and launch pricing pending |
| [0102](0102-private-pilot-observations.md) | Private pilot protocol and offline reporting with separate fictional/customer evidence | Accepted; preparation and fictional verification only, actual customer observations pending |
| [0103](0103-unapproved-memory-deletion.md) | Exact owner deletion of never-approved memory drafts and saved command responses | Accepted; retained audit/source/backup boundaries are explicit; verification recorded separately |
| [0104](0104-verified-backup-retention.md) | Verify source ownership and contents before backup retention; preserve uncertain folders | Accepted; fictional destructive-regression checks recorded separately; full company retirement remains open |
| [0105](0105-company-retirement-review.md) | Owner-only inventory of company records and unresolved work before retirement | Accepted; read-only review, never deletion authorization; coordinated closure remains open |
| [0106](0106-company-work-admission-pause.md) | Durable company-level pause on new execution admissions with preserved recovery | Accepted; not a kill switch or write freeze; verification recorded separately |
| [0107](0107-atomic-company-creation-and-recovery.md) | Atomic company creation with actor-bound request identity and read-only lost-response recovery | Accepted; verification recorded separately; hosted acceptance remains open |
| [0108](0108-local-qualification-host-proposal.md) | Separate pinned WSL host for the existing browser qualification suite | Subsequently owner-approved and installed; local qualification recorded separately |
| [0109](0109-qualification-engine-without-bridge-networking.md) | Configure the approved dedicated engine before first start, without bridge or firewall changes | Accepted for local fictional qualification; application execution remains disabled |
| [0110](0110-browser-startup-within-existing-memory-budget.md) | Avoid unused browser startup work while retaining the existing containment and resource limits | Accepted for candidate rebuild; full qualification recorded separately |
| [0111](0111-stable-runtime-capability-fingerprint.md) | Normalize supported-capability order while preserving configuration changes in qualification bindings | Accepted; local receipt survives dedicated daemon restart and execution remains disabled |
| [0112](0112-home-guides-the-first-completed-result.md) | Resume the role-owned first assignment from Home and require an approved, completed work result | Implemented; local browser evidence recorded separately, customer usefulness remains pending |
| [0113](0113-script-image-inventory-and-local-qualification-plan.md) | Inventory the existing pinned script image and prepare standard/extended qualification on the dedicated host | Owner-approved local suite and receipt checks passed; host task exhaustion diagnosed, reliable fix pending and execution disabled |
| [0114](0114-member-bound-browser-sessions.md) | Member-scoped hosted drafts, stale-account request refusal and confirmed sign-out | Accepted; fictional middleware and UI evidence recorded separately; production acceptance remains open |
| [0115](0115-authenticated-company-entry-recovery.md) | Authenticated company selection for secondary invitations, stale links and revoked membership | Accepted; fictional middleware and UI evidence recorded separately; production acceptance remains open |
| [0116](0116-coordinated-work-inbox-destinations.md) | Durable plan/meeting decision alerts with exact destinations and quiet discussion turns | Accepted; local fictional verification recorded separately; real delivery and usefulness remain open |
| [0117](0117-accepted-minutes-as-reviewed-memory.md) | Explicit accepted-minutes handoff into separately approved, cited organizational memory | Accepted; local fictional verification recorded separately; customer usefulness remains open |
| [0118](0118-private-document-source-receipts.md) | Company-bound private import receipts and explicit source comparison with owner-reviewed replacement | Accepted; local fictional verification recorded separately; live source acceptance remains open |
| [0119](0119-script-worker-thread-budget.md) | Smaller Node background pools within the original host task budget | Candidate failed its first runtime observation; no qualification or activation |
| [0120](0120-script-runtime-and-workload-task-limits.md) | Separate bounded runtime overhead and per-UID workload task limits | Implemented; one approved diagnostic found 33 workload tasks instead of 32; stopped, unqualified, cleanup confirmed |
| [0121](0121-retired-memory-source-deletion.md) | Exact owner deletion of revoked/replaced saved memory with retained historical evidence | Accepted; focused backend and desktop/mobile fictional verification recorded separately; full company erasure remains open |
| [0122](0122-modal-workspace-overlays.md) | Native modal focus and background isolation for compact cockpit and shortcut help | Accepted; fictional keyboard and responsive browser verification recorded separately |
| [0123](0123-drive-links-and-exact-file-identities.md) | Paste Drive file links and preserve case-sensitive allowlist identities | Accepted; bounded normalization retains selected-file authorization |
| [0124](0124-configurable-local-listener.md) | Preview a local build on another loopback port beside an installed workspace | Accepted; local owner mode remains loopback-only |
| [0125](0125-codex-0164-runtime-pin.md) | Review the installed Codex 0.164 bundle for the dedicated OAuth connection | Accepted pin update; metadata inspection is not live execution or coding qualification |
| [0126](0126-readable-internal-reports.md) | Render report formatting while preserving exact artifact text and approval | Accepted; raw HTML and remote images stay disabled |
| [0127](0127-explicit-text-model-accounts.md) | Retain the host default beside separately selected API text models | Accepted; exact provider terms, per-route usage and no fallback |
| [0128](0128-artifact-feedback-and-revision-requests.md) | Save requested changes with a separate, explicitly authorized revision | Implemented; backend and browser verified |
| [0129](0129-usable-weekly-deliverables.md) | Include complete useful assets and open revisions directly for review | Installed locally; native output reviewed and editorial correction saved; owner acceptance pending |
| [0130](0130-editable-draft-copies.md) | Edit a separate draft with tab recovery and unchanged model authority | Installed locally; fictional editing, recovery and navigation journey passed |
| [0133](0133-revisions-keep-original-requirements.md) | Keep original requirements in revisions unless feedback removes them | Accepted; instruction change only, no live model run |
