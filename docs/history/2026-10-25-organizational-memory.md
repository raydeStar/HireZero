# Structured organizational memory — 2026-10-25

Scope: another increment of the full three-phase product goal. No complete product phase is claimed.
Decision: [ADR 0060](../adr/0060-structured-organizational-memory.md).

## Delivered

- Owner-reviewed decisions, goals, preferences, project notes, feedback and prior work with explicit audiences,
  review dates and exact citations to approved artifacts. Reviewed statements remain immutable.
- Explicit, atomic replacement of earlier decisions. History remains readable; stale replacement requests and
  circular supporting evidence fail without retiring the earlier record.
- Bounded keyword retrieval in task context, readable packet provenance, immediate stale/expired exclusion and
  dispatch/late-output fences. Existing request allowances and usage settlement remain intact.
- Deterministic maintenance on host startup and once per minute, durable review alerts, exact inbox/Today
  navigation and explicit renewal. Existing schedules require fresh approval for the expanded retrieval scope.
- A Memory page with company-scoped draft and receipt recovery, citations, retrieval preview and mobile layout.
  Active records are shown first; retired duplicates move into history. Raw state/type labels were replaced
  with readable labels, and refresh clears stale approval and preview state.

## Verification

317.3 GiB was available before the build work. Backend build passed with zero warnings/errors; frontend
production build passed. No dependency or runtime pin changed for this increment.
Scoped Csharpier checks passed for 19 files, scoped Prettier passed and `git diff --check` passed.

Focused backend classes: **55 passed**, zero failures/skips, 8.433 seconds across `CompanyMemoryTests`,
`CompanyDocumentTests`, `CompanyScheduleTests`, `CompanyNotificationTests`, `BusinessFreshnessTests` and
`BusinessContextTests`. The nine memory facts cover:

- Exact owner approval, immutable revisions, duplicate refusal and revocation.
- Company, department, employee and company-conversation retrieval isolation.
- Atomic replacement and stale earlier-version refusal.
- Immediate review-date expiry, persistent maintenance state and one durable notice.
- Exact approved-work citations, source revocation and metadata-only work listing.
- Prepared-request refusal, late-output withholding and actual usage settlement.
- Transitive source-context invalidation and circular replacement rollback.
- Retrieval/packet bounds and unchanged null-field serialization for old contexts.
- Migration 18 preserving prior data and earlier migration checksums.

Existing `CompanySuite.Security`, `IntegrationPath`, `MeasuredQueue` and `Conversation`: **4 passed**, zero
failures/skips, 47.546 seconds. Total non-overlapping backend results: **59 passed**. The existing
`company-ui.test.mjs` recovery/scope checks passed.

Browser journeys passed against the final frontend:

- `memory-browser-smoke.mjs`: unsaved text survives reload; a lost save reply and unavailable receipt recover
  after reload without another save; drafts remain excluded until explicit approval; replacement preserves
  earlier history and switches the active result; prior-work quotes are inspected and their result opens;
  expiry excludes memory and creates an inbox alert that opens the exact record; renewal needs explicit
  approval; a 390 × 844 viewport has no horizontal overflow and a properly stacked introduction.
- `schedules-browser-smoke.mjs`: recurring employee work, source checks, pause/resume, allowance settlement and
  exact inbox navigation still pass with the revised consent text and authority fingerprint.

The mobile screenshot was visually inspected before and after the history/default-view refinement. Both
browser fixtures use disposable data and profiles, fictional providers and zero live model calls.

Compact ignored evidence: `test-results/memory-build.log`, `memory-web-build.log`, `memory-regression.log`,
`memory-security-regression.log`, `memory-browser-run.log`, `memory-browser.json`, `memory-mobile.png`,
`memory-schedules-browser.log` and `memory-ui.log`.

## Remaining

Keyword matching can miss paraphrases and does not automatically resolve conflicting decisions. Real retrieval
quality, actual employee usefulness, semantic maintenance, feedback-driven suggestions, retention/deletion,
private connectors, billing and advanced execution remain in the full acceptance ledger. Background review
notifications require a running host; retrieval and dispatch exclusions do not wait for that background pass.

No live model request, external message, deployment, publication, commit or push occurred.
