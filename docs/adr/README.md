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
