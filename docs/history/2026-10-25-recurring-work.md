# Finite recurring work — 2026-10-25

Scope: an increment of the full three-phase context-first product goal. No product phase is claimed complete.
Decision: [ADR 0058](../adr/0058-bounded-recurring-work.md).

## Delivered

- Company-scoped daily or weekly schedules with an explicit time zone, first date and one to 31 occurrences.
  The supported work is private employee replies or checks of one to three citations from an approved brief.
- Separate draft and exact owner authorization. Review retains company/department facts, employee notes,
  responsibility and instructions, source scope, provider identity, route and limits. Changed authority blocks
  execution. Each employee schedule owns a private thread and excludes conversation history from requests.
- Durable slot claims, unique occurrence ledger, finite shared token allowance, ordinary budget reservation and
  actual usage settlement. Pause/end fences late output. Unknown provider outcomes hold reservations and prevent
  resume until reconciled. An interrupted occurrence is never dispatched again automatically.
- Deterministic clock changes and missed slots. Spring gaps move forward to a valid minute; repeated autumn
  times run once. Overdue slots do not create catch-up bursts. A slot more than an hour late is skipped.
- All company workers restore at host startup, including companies not opened by a browser. Runtime creation is
  serialized so concurrent requests cannot start duplicate recovery workers.
- Recurring work in the Company hub: draft recovery, in-place draft edits, exact consent, pause/resume/end,
  remaining allowance, next dates, linked run history and direct private-result/source-review navigation. Today
  identifies drafts needing approval and blocked schedules. Schedules can be copied for new approval.
- Scheduled source checks use the existing guarded public reader and source-refresh quota/deadline. Results feed
  the existing comparison and owner-review workflow; approved facts are never silently replaced.

## Verification

317.7 GiB free before the final builds. Frontend production build and company test project build passed with
zero compiler warnings/errors. Scoped Csharpier check passed for 18 files; scoped Prettier and `git diff --check`
passed. The ten focused schedule facts passed again against the final build after copy corrections.

Focused backend checks: **48 passed, zero failed/skipped** across `CompanyScheduleTests`,
`BusinessFreshnessTests`, `BusinessContextTests`, `CompanyDocumentTests`, `CompanyTodayTests` and
`BusinessLookupTests`. Ten schedule facts cover clock transitions, exact approval, tenant isolation, replayed
command receipts, finite occurrences, private results, budget settlement, missed slots, restart/no replay,
authority changes, owner revocation, uncertain outcomes, pause during execution, late usage settlement, scheduled
source refresh, migration 16 and all-company host restoration.

Existing `CompanySuite.Security`, `IntegrationPath`, `MeasuredQueue` and `Conversation`: **4 passed**, zero
failed/skipped, 50.334 seconds. Total non-overlapping backend results for this slice: **52 passed**.
`company-ui.test.mjs` also passed.

Browser journeys passed:

- `schedules-browser-smoke.mjs`: unsaved draft reload, lost-response reconciliation without duplicate schedules,
  exact consent, editing a saved draft in place, two background employee runs, direct result navigation, pause
  preventing the next occurrence, explicit resume, finite allowance settlement and one background source check.
  Approved source facts remain unchanged, and the result opens the existing evidence comparison.
- Existing `freshness-browser-smoke.mjs`: manual refresh, lost-receipt recovery, comparison, explicit owner
  assessment, retained unsaved correction and deliberate replacement approval still pass.

The recurring-work screenshot at 390 × 844 was visually inspected: readable run history, dates and controls,
with no horizontal overflow. The final browser journey also asserts that a completed schedule no longer shows
a stale "Schedule active" success message. Ended schedules no longer advertise future work, and API-provider
allowances are labeled in US cents. The session label now describes required approval instead of claiming all
execution is manual.

Compact evidence is in ignored `test-results/`: `schedules-build.log`, `schedules-web-build.log`,
`schedules-focused.log`, `schedules-regression.log`, `schedules-final-core.log`, `schedules-ui.log`,
`schedules-browser.log`, `schedules-browser.json`, `schedules-mobile.png` and `schedules-freshness-browser.log`.
Both browser journeys reported success; the recurring journey reported no browser exceptions, live model calls
or external requests. Temporary fixture data and isolated browser profiles were cleaned up by their harnesses.

## Limits and remaining work

The host must be running for due work. There is no always-on deployment guarantee, external notification
delivery, subscription billing, monthly quota, sandbox lifecycle or general autonomous tool permission in this
slice. Employee replies use the existing bounded conversation policy; complex reports and real usefulness have
not been established. Source checks share the existing hourly company quota and can stop for review if it is
exceeded. Retention/deletion and semantic memory work remain separate requirements.

Verification uses fictional providers/pages and controlled clocks. No live model, paid search, external website,
publication, deployment, commit or push. Real customer repeat use and commercial value remain unverified.
