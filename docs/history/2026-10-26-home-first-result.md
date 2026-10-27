# Home to the first completed result — 2026-10-26

Implemented [ADR 0112](../adr/0112-home-guides-the-first-completed-result.md) while continuing the full
context-first product scope. Home previously marked the request step done for any task, including chat and
setup drafts. Its last step could have no usable action while work awaited authorization, and approval of an
artifact could hide the checklist before the assignment was completed.

Home now offers the existing role-owned first-request form for an active employee with a private thread and
the current approved company brief. Returning from onboarding or reloading preserves the same tab-scoped draft.
Preparation still only saves a request; exact context and allowance review precede execution. A saved assignment
replaces the preparation form with a direct work link. Result approval and task completion remain separate, and
both are required to finish the guide. Home opens assignment results in focused work mode.

Chat and setup-context drafts are excluded from the milestone, consistently with the Progress report. The
current-profile selector also checks the company-knowledge binding. Retired employees do not satisfy hiring,
and viewers do not receive the new preparation form. Roles without a business setup contribution retain their
ordinary work entry point. No role implementation or backend authorization rule changed.

## Verification

- TypeScript/Vite build passed. The existing main-bundle size advisory remains.
- The current .NET browser fixture build passed with zero warnings/errors.
- The extended fictional `business-drafting-browser-smoke.mjs` journey passed. It prepares and cancels a real
  fixture chat and setup-context draft, returns Home, reloads, and verifies that the first assignment is still
  offered. Neither prepared request runs a model.
- The same journey edits the Home request, reloads, checks persistence, injects a lost save response and verifies
  that exactly one ordinary assignment exists. Returning Home before execution opens the saved assignment
  instead of displaying another preparation form.
- Exact approved context, source citations and the bounded deliverable policy remain checked. The generated
  fictional result stays readable in focused work mode. Approving it alone leaves the guide active; reopening
  it from Home and completing the task finishes the guide, including after reload. Progress still records one
  assignment and one completion despite the two non-deliverable tasks.
- The form was captured at 390px and 1440px with no horizontal overflow. Desktop/mobile captures were visually
  inspected. Existing result, progress, correction-history and unknown-usage assertions remain in the journey.
- Shared company-client recovery checks passed. Targeted Prettier, Node syntax and diff whitespace checks passed.

The first browser launch was blocked by the shell's loopback network restriction and ended before the journey.
The authorized local rerun reached the result but exposed an obsolete test click: Home already opened the result
in work mode, so the old `Read result` button was absent. The assertion was updated to verify the focused view
directly. The full journey then passed, and passed again after adding setup-context exclusion.

Compact logs, screenshots and a verification summary are retained under ignored `test-results/first-result-home*`;
the full journey receipt is `test-results/business-drafting-browser.json`. The fixture used fictional HTTP/model
responses, reported three fictional model calls, zero live model calls and zero external requests. Its owned
host/browser processes exited and its temporary directory was removed. About 319 GiB was free before the builds.

This strengthens P1.7/P1.8/P2.2. It does not prove real output usefulness, hosted onboarding, customer retention
or completion of all three phases. Application browser execution remains disabled; no Docker/WSL runtime,
production data or provider account was changed by this UI work.
