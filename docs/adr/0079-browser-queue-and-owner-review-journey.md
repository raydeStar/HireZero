# 0079 — Browser queue and owner review journey

Status: accepted; production runtime qualification and activation pending. Date: 2026-10-26.

## Decision

Connect the durable browser contract from [ADR 0078](0078-durable-browser-authority-and-reviewed-captures.md)
to the host and task UI without treating a fictional executor or a structural Docker probe as a production runtime.

- The browser has a separate single-worker host service. At startup it snapshots unresolved jobs across every
  saved company, including companies nobody has opened, and reconciles those jobs without replay. New approvals
  use the durable dispatch gate. Manual reconciliation shares the worker gate and requires the original runtime
  identity. Exceptions fence the job for recovery instead of re-enqueuing execution.
- Execution availability is explicit and separate from recovery availability. The host reports browser readiness
  and recovery capabilities independently from scripts. `IBrowserRuntimeAvailability` supports bounded periodic
  readiness refresh. Missing or unqualified runtimes do not enable execution. Only an explicitly supplied test
  executor with a fixture URL is composed at this stage; no production setting, environment flag or structural
  probe activates a real browser.
- Owner-only HTTP routes prepare exact itineraries and captured-text artifacts, with 128 KiB request limits.
  Run/revoke use the existing company operation ledger. A company-scoped recovery route preserves original
  runtime binding. Task details expose saved jobs; dashboard task IDs keep saved browser work visible after new
  execution becomes unavailable. Running browser task IDs make the task board show actual browser progress.
- Finishing the parent task removes permission for pending and active browser execution. Late captures are
  withheld, while resource allowance stays held until cleanup is confirmed. Previously approved captured
  evidence remains usable under its normal source-provenance checks.
- Task controls start with a website address, review name, screenshot choice and desktop/mobile page size.
  Advanced options expose the exact ordered navigation/click/wait/read steps, CSS selectors, additional allowed
  origins and request/byte/time limits. Preparation makes no visit. The saved review shows the exact websites,
  itinerary, screenshot permission and overhead before an explicit one-time approval.
- Proposed, held and settled allocations use different wording. An unapproved draft never calls itself approved
  or claims that allowance is already reserved. Finished tasks cannot approve an unused proposal. Revocation is
  available during running work and after output; uncertain recovery never offers a replay action.
- Captures show the page URL, date, plain text and optional saved JPEG. Partial evidence, blocked resources,
  shortened text and cleanup uncertainty remain visible. Page-provided text is rendered as text. Unsupported
  link schemes never become clickable anchors. Owner downloads include plain text with source/date/partial
  status, the saved screenshot, and the full JSON receipt.
- Exact selected text enters the existing artifact review flow, with focus moved to the resulting review card.
  Output preparation continues to work when new execution is disabled. Existing command recovery handles lost
  approval/output replies without creating another execution or artifact. Source date and partial-evidence
  provenance remain visible when the artifact is reviewed and the task is completed.
- Load browser controls as a separate UI chunk only when a task can use them or has saved browser history.
  Keep script and browser readiness distinct in the shared compute screen.

## Verification boundaries

The host fixture returns bounded fictional captures and never visits websites, launches an execution browser or
starts a container. Its JPEG is a small saved screenshot of the fictional page from the earlier real-Chrome worker
fixture; it is embedded in the test executable, not loaded from personal browsing or production storage.

Local host tests cover all-company startup recovery, exact HTTP approval, operation replay, disabled-runtime
recovery, selected-output review, tenant isolation and missing/replacement runtimes. A real UI Chrome fixture
exercises the whole owner journey with the fictional executor, including advanced steps, screenshot rendering,
partial evidence, downloads, lost replies, failed cleanup, cancellation, artifact review and task completion.
This verifies product composition; real broker/driver process evidence remains in ADR 0076. It does not prove
production image compatibility or OS containment.

Still required: production image/dependency pins, actual containment qualification and receipt-bound activation,
browser-data retention/deletion, plan/schedule composition, real-site usefulness and commercial validation.
