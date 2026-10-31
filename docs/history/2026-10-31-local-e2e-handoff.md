# Local end-to-end handoff — 2026-10-31

The owner requested a thorough local walkthrough, repairs to discovered bugs, and completion of unfinished parts
of the daily workflow before handing the app back. This cycle covers goal entry, saved work, proposals, owner
feedback, recurring work, knowledge, documents, conversations and team handoffs. Hosted signup remains deferred.

## Repairs

- **Proposal recovery:** Do it now saves each step before dispatch, scoped to the company, member and employee.
  Reload can reconcile a lost preparation or run reply and continue the original, term-bound request. It cannot
  create another task or replay a dispatched request. A stopped request opens its saved work. Storage failure
  prevents dispatch, and another proposal waits while an outcome is unresolved.
- **Visible proposal terms:** The one-click action shows the provider, model and intended allowance. Effort,
  deadline, output target and scope are disclosed below it. Failed term loading has a visible reload action.
- **Kept lessons:** The existing one-click owner decision now saves and approves the lesson in one transaction.
  The owner-only command requires the displayed report's digest and quoted words. Replayed receipts and an
  identical already-approved lesson do not create duplicates. Reload checks a lost receipt without repeating
  the mutation. Existing approval scope and later-round lesson selection are unchanged.
- **Owner feedback:** Markdown emphasis no longer makes visible proposal words fail source binding. The report
  digest is still required, and unrelated text is refused. A recovered decision-list fetch clears its old load
  error without erasing errors from a separate owner action.
- **Mobile next steps:** An approved brief no longer repeats the completed onboarding instructions above the
  next action. Chat's secondary context panel takes less vertical room so the allowance stays above the bottom
  navigation, while the transcript retains reading space.
- **Attention wording:** A separate knowledge draft is labeled as proposed changes when approved context already
  exists. It no longer implies that the company has no approved facts.
  Its Needs you link selects, scrolls to and focuses the exact draft, with a reloadable destination.
- **PDF completion:** The worker may exit between its liveness check and memory probe. That completed-process
  race now proceeds to normal exit-code and bounded-reply validation; memory and deadline limits remain in place.

These repair existing decisions and workflows; no new architectural decision or model policy was introduced.

## Verification

- Release build and frontend build passed. Frontend checks: **54 passed**.
- Full company suite: **630 passed, zero failed, five skipped**. The five skips require PostgreSQL, outside this
  local SQLite walkthrough. New assertions cover atomic lesson approval, duplicate prevention, source binding,
  owner-only access, and the distinction between proposed and approved knowledge.
  The subsequent exact-draft navigation refinement passed all six focused Today tests and the expanded Home
  browser journey, including focus, visibility and reload of the selected knowledge version.
- **18 browser journeys passed**, using disposable fictional workspaces and fake providers:
  goal start/return visit, Marketing shift, schedules, conversational replies, action recovery, settings recovery,
  company isolation, business brief, memory, documents, employee reviews, inbox, team setup, delegation, goal
  ladder, PDF import, company chat and task handoff.
- The expanded shift journey refuses unavailable terms and failed recovery storage; loses both preparation and
  dispatch replies; reloads and reconciles one task; loses a Keep reply; and verifies one approved lesson reaches
  the next round. Correction, veto, undo, unchanged raw output and pause remain covered.
- Navigation checks now follow Settings and Company conversation. Split-chat checks use the actual wide-screen
  breakpoint. Home checks wait for asynchronously loaded terms before asserting fresh consent.
- A hands-on installed walkthrough opened an existing raw draft directly, copied it, opened Refine with focused
  feedback, and closed the form without altering the result. Receipts and costs stayed collapsed.

Initial failures are retained in private logs: the first company run had one intermittent PDF-worker failure;
the isolated PDF checks and final full suite passed. Old navigation/width assumptions and a terms-readiness race
were test defects. Mobile next-step/composer visibility were product defects. A restricted-process compiler CLR
crash and blocked Chrome debugger were infrastructure failures; the same checks passed with local process/HTTP
access permitted. Assertions were not relaxed to hide these failures.

**No live model calls, new recurring authority, publishing, purchases or outreach.** These checks establish local
workflow behavior, not raw-model usefulness, hosted readiness or real customer retention. Reworded equivalents of
a veto still rely on the model following standing owner direction; this cycle does not claim semantic enforcement.

## Installation and handoff

The release preserves unchanged integration binaries, particularly the pinned Codex connector, to avoid changing
runtime identity underneath approved schedules. Backup-first installation and post-install evidence are recorded
privately with the app/data recovery copies. The Developer employee remains available and idle; live coding and
automatic execution stay disabled.

Both local apps were updated with app and data backups retained. Existing tasks, artifacts, measured-job records,
accounts, model settings and audit chains were preserved. The fourteen-round weekday shift and the separate
two-occurrence weekly pilot still have current reviews, zero consumed occurrences and their original next dates.
