# Owner inbox and bounded notifications — 2026-10-25

Scope: another implementation increment of the full three-phase product goal. No product phase is complete.
Decision: [ADR 0059](../adr/0059-durable-inbox-and-bounded-notices.md).

## Delivered

- Transactional inbox alerts for employee results/review, blocked or uncertain work, recurring outcomes and
  source checks. Personal read/unread state, stable pagination, unread filtering and a persistent inbox button.
- Direct links to private results, exact schedules and source review. Company-specific links select the named
  company without overwriting another tab's preference; server membership still guards every request.
- Optional email notices to the current owner's verified sign-in address. Off by default, exact consent,
  selected categories, one to 20 attempts per UTC day and a 30-day authorization. No historical send on opt-in.
- Generic email content with no facts, output, document excerpts or company name. A role-independent SMTP
  adapter requires STARTTLS and normal certificate validation. Operator settings and a separate password secret
  compose the service only for hosted HTTPS workspaces; no employee gains a send-mail tool.
- Durable outbox claims and outcomes, pre-dispatch authority checks, daily allowance, stale/read suppression,
  deadline and restart recovery. Unknown delivery is never replayed; subsequent mail is held until its owner
  explicitly keeps that alert in the inbox without resending it.
- Company-scoped lost-response receipt recovery, preference refresh/discard controls and explicit distinction
  between mail-server acceptance and delivery to the recipient's inbox.

## Verification

317.7 GiB was available before the build work. Company test project build passed with zero warnings/errors;
frontend production build passed. Scoped Csharpier check passed for 18 files, scoped Prettier passed and
`git diff --check` passed. The lockfile diff adds the local notification project only; no package upgrades.

Focused backend classes: **46 passed**, zero failures/skips, 11.340 seconds across `CompanyNotificationTests`,
`CompanyScheduleTests`, `BusinessFreshnessTests`, `BusinessContextTests` and `CompanyDocumentTests`. Nine new
notification facts cover durable/per-member read state, tenant isolation, verified-owner consent, no historical
mail, generic content and exact company link, single send, daily limit, read/disabled/expired suppression,
changed service, revoked membership, unknown/interrupted send recovery, pagination, STARTTLS refusal before
credentials/content, and migration 17 preserving earlier records/checksums.

Existing membership/settings classes: **8 passed**, zero failures/skips. Existing `CompanySuite.Security`,
`IntegrationPath`, `MeasuredQueue` and `Conversation`: **4 passed**, zero failures/skips, 52.001 seconds.
Total non-overlapping backend results: **58 passed**. `company-ui.test.mjs` also passed, including company-bound
deep links that do not change another tab's preference.

Browser journeys passed against the final frontend:

- `inbox-browser-smoke.mjs`: explicit email consent, lost preference response reconciled once, no historical
  emails, three fictional background send attempts with unique envelope IDs, unknown-outcome hold and explicit
  acknowledgment without replay, disable preventing new emails, read state across reload, unread filter and
  direct private-result navigation. Zero real emails and zero browser exceptions.
- `schedules-browser-smoke.mjs`: two approved employee occurrences and one source check, pause/resume, finite
  allowance settlement, and opening the exact source schedule from its inbox alert before reviewing evidence.
  Fictional providers/pages only, zero live model calls and zero external requests.

The inbox screenshot at 390 × 844 was visually inspected. Its initial rendering exposed an inherited header
layout that split the introduction into cramped columns and wrapped the bell onto a separate row. The final
render stacks the introduction, aligns the bell and cockpit controls, keeps checkboxes beside their labels and
updates the badge with the displayed inbox count. Browser assertions cover stacked heading/copy, aligned
controls and absence of horizontal overflow. The fixture harnesses removed temporary data/browser profiles.

Compact ignored evidence: `test-results/notifications-build.log`, `notifications-web-build.log`,
`notifications-core.log`, `notifications-access-settings.log`, `notifications-regression.log`,
`notifications-ui.log`, `inbox-browser.log`, `inbox-browser.json`, `inbox-mobile.png`,
`notifications-schedules-browser.log` and `schedules-browser.json`.

## Remaining

Production SMTP acceptance, deliverability and actual owner/customer usefulness have not been tested. The local
SMTP fixture proves refusal without STARTTLS; successful deliveries in application tests use a fictional sender.
The host must remain available for background email. There is no SMS or closed-browser push delivery. These
alerts cover the outcomes listed above; additional orchestration-specific notification categories can extend
the same durable mechanism. Retention/deletion, subscriptions, broader organizational retrieval, advanced
execution and real customer validation remain in the full acceptance ledger.

No real message, live model call, public website access, publication, deployment, commit or push occurred.
