# Company progress and mobile navigation — 2026-10-25

Scope: part of X11 in the [full product delivery ledger](../CONTEXT_FIRST_DELIVERY.md).
Decision: [ADR 0072](../adr/0072-company-progress-from-saved-history.md).

## Delivered locally

- Company → Progress presents saved onboarding time, generated-brief field edits, completed work, first assignment,
  member request days, scheduled tasks and recorded model costs. Each measure has an explicit denominator or
  coverage boundary. Conversations do not count as completed work; automatic schedule occurrences do not count
  as later member requests. Missing historical dates stay unknown.
- The first assignment links to the existing focused work view. Business brief and usage links open their
  respective company sections. Refresh failures preserve the last checked snapshot and offer a normal retry.
- Download metrics saves the checked, versioned JSON summary, without business facts, prompts, results, employee
  names or actor identities. IDs, counts, states, dates and costs are included. Reads and downloads do not add
  workflow events or contact an analytics service.
- Provider-reported and estimated model charges remain separate. Unpriced and unsettled jobs are visible. The
  completed-work average stays unavailable if any completed task lacks usage records or has unknown attached
  model costs. Onboarding and chat costs remain in the all-job totals, outside that work average.
- The mobile company section navigation now occupies one horizontally scrollable row. The selected section
  scrolls into view when opened, resized or reloaded. This replaces the tall collection of wrapped buttons that
  pushed useful page content below the fold.

## Verification

- Free disk before builds and after fixture cleanup: approximately 316.8 GiB.
- Initial company test build: zero warnings/errors, 87.86 seconds. Final incremental build: zero warnings/errors,
  6.58 seconds. No build ran while a browser fixture held the backend assemblies.
- `CompanyProgressTests` and existing `CompanyUsageTests`: **13 passed**, 2.278 seconds. Four new focused facts
  cover empty/missing history, tenant isolation, persistence, request-day exclusions, idempotent edits, manual
  versus generated briefs, exact first-assignment completion, stable saved prices, unknown prices and missing receipts.
- During test development, the generated-profile fixture was corrected to edit the actual saved brief (which
  preserves owner-supplied goals), and replacement approval was made to explicitly revoke the old brief first.
  These were fixture errors; existing provenance and approval rules remained intact.
- The expanded business-drafting Chrome journey passed against the real local HTTP routes, store and frontend,
  with fictional pages and provider responses. It covers onboarding through approved/completed first work, checks
  progress values, reads the actual downloaded JSON file, verifies content exclusion, recovers from a failed read,
  reloads the mobile section and opens the completed task from Progress.
- A first browser edit placed the progress assertions before the onboarding journey ended. That sequencing error
  was corrected; the final run exercises the completed journey. No synthetic result is reported as a real customer trial.
- Inspected desktop and 390-pixel mobile screenshots, including the model-cost panel. The active mobile company
  section stays inside a single navigation row after resize and reload; no horizontal page overflow or browser
  exceptions occurred. Shared company UI recovery checks and TypeScript/Vite build passed.
- Affected C# and new/frontend test files were formatted; scoped whitespace checks passed. Final assets:
  `index-D1_kk5Du.js`, `index-BPN65mHA.css`.

Ignored compact receipts: `test-results/company-progress-build.log`, `company-progress-tests.log`,
`company-progress-web-build.log`, `company-progress-browser.log`, `company-progress-ui-tests.log`,
`business-drafting-browser.json`, `company-progress-desktop.png`, `company-progress-mobile.png`, and
`company-progress-costs-mobile.png`. The harness disposed its fixture host and temporary browser/data/download
root. No live model calls, external requests, real charges, deployment or existing user-host restart occurred.

## Remaining acceptance

This is workflow activity and partial model-cost measurement. It does not prove correct business matching,
useful output, customer retention, paid conversion or full operating cost. Real pilot observations and commercial
terms remain pending. No complete product phase is claimed.
