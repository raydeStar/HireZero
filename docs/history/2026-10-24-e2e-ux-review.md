# E2E and UX review — 2026-10-24

The core journeys worked in the initial browser pass, but several navigation and status cues made the product
harder to use than those green checks suggested. This pass fixes reproduced friction and extends the existing
browser regressions. It does not change execution authority or provider configuration.

## Changes

- Empty Home explains how to start, with one primary hiring action instead of zero-valued metrics and repeated
  hiring prompts. The checklist stays readable on a phone.
- Home resumes the actual unfinished hire, including a saved context draft after browser draft loss. Onboarding
  employees have a direct continuation action on their team card.
- Explicit Chat buttons open Chat after using Work. Company sections now follow browser Back, Forward and reload.
  Escape closes the phone conversation drawer.
- Today items open the place to act. Failed activity reads offer an in-place retry, and stale responses cannot
  replace a newer day. Calendar shifting uses UTC date arithmetic.
- A prepared model job or reply appears under Needs you, waiting for authorization, instead of falsely appearing
  to be running. It leaves the authorization queue after dispatch or cancellation. The persisted execution state
  and approval gates remain unchanged.
- Opening a reply from the cockpit focuses its actual review card in Chat. Company replies restore the matching
  recipient, including after reload.
- Composer buttons visibly identify Prepare reply, Save message, Save company note and Assign work. An empty
  allowance has an explanation, and an empty evidence picker explains how approved results become available.
- Mobile Chat/Work controls have their own header row, avoiding overlap with the cockpit button. The conversation
  layout reserves space for the required allowance and composer while retaining a readable transcript. Action
  chips occupy a horizontal scrolling row with their full height preserved.
- Completed-work history initially shows twelve items and provides Show earlier work to reach older items.
- Assistant Open setup links reveal and focus the matching integration instructions.
- Google property/site pickers preserve saved selections when available. Missing or unconfigured selections stay
  unselected, with an explanation for unavailable saved choices; opening the picker never saves a replacement.
- Plans and Meetings select the CEO role when their hiring shortcut opens a fresh guide. An edited or unfinished
  hire remains intact, with an explanation instead of a silent replacement.
- Each loaded page retains its company for API requests. Selecting another company in a second tab cannot redirect
  the first tab's commands or receipt checks.
- Hiring drafts and uncertain-operation IDs now use company-scoped browser storage. Older drafts migrate only
  when their saved references identify the current company; unassigned profile text requires explicit import.
  Older uncertain receipts stay available for checking, with no automatic replay. Delayed legacy checks update
  only the older draft, preserving a newer form. See [ADR 0050](../adr/0050-company-scoped-browser-recovery.md).

## Validation

The browser harness launches isolated Chrome profiles against disposable local companies with fictional data.
Model execution uses the compiled fake provider where required. Google picker responses are mocked; no real
account sign-in or configuration writes occur in those checks.

The initial seven browser suites passed before changes. The expanded pass covers these nine suites:

- `company-browser-smoke.mjs`: departments, hiring, approved shared knowledge, employee chat and saved messages.
- `company-onboarding-browser-smoke.mjs`: first-run Home, hiring recovery, duplicate submit, lost responses,
  onboarding isolation, Today retry/navigation, desktop/mobile Home and Google selection preservation.
- `company-chat-browser-smoke.mjs`: scoped drafts/history, navigation, stale replies and explicit cancellation.
- `conversation-reply-browser-smoke.mjs`: preparation, authorization, correct recipient/review navigation,
  cancellation, reload continuity and no replay.
- `employee-review-browser-smoke.mjs`: review flow and usage settlement.
- `measured-browser-smoke.mjs`: assignment, preparation, one-job authorization and result review.
- `task-handoff-browser-smoke.mjs`: approved cross-department handoff, scoped revocation, visible composer labels,
  older-work access and mobile control geometry.
- `walkthrough-browser-smoke.mjs`: support-message redaction, review unlocking and readable 1024×768 layout.
- `muse-layout-browser-smoke.mjs`: Home, Chat/Work, shortcuts, themes, cockpit, mobile drawer, section history and
  assistant setup navigation. The second pass also covers CEO role selection and preserving an existing draft.
- `company-scope-browser-smoke.mjs` (second pass): two real browser tabs, two real fixture companies, stable command
  routing, separate draft round trips, legacy import/migration, restricted storage and uncertain receipt recovery.
  A delayed real receipt is released after navigating away and editing a newer profile; only the older record is
  updated, the newer visible/stored draft survives, and no command is replayed.

Additional checks: frontend production build/typecheck, test-host build with zero warnings/errors, all 13 Node
checks, all five CompanyToday xUnit facts (including two added authorization-state facts), frontend formatting,
formatting of the changed C# files and `git diff --check`.

All nine suites passed. After the final mobile adjustments, the conversation suite was rerun and passed checks for
the visible allowance, at least 120px of mobile transcript, non-overlapping header controls, the first action chip
fully within its strip, and the More actions menu visible and reachable after horizontal scrolling. Desktop
conversation reading space is separately covered at 1024×768. Disposable walkthrough hosts were stopped and their
temporary data removed; the scripts clean up their own fixtures.

Screenshots and compact test receipts are under ignored `test-results/`, including `ux-home-desktop.png`,
`ux-home-mobile.png`, `conversation-reply-prepared.png` and `conversation-reply-mobile.png`.

Windows sandbox restrictions initially prevented fixture loopback access and temporary key-file renames. The same
synthetic checks passed outside that sandbox. This was test infrastructure, not an application failure.

## Third pass: input preservation and recovery

- The cockpit no longer turns a failed needs read into “Nothing needs you.” It shows an unknown count, retains
  last-checked items when available, and offers a read-only retry independent of company activity. Home shares
  that loading/error state and retry; successful reads clear the error.
- Plan and meeting drafts clear only after a confirmed creation. Rejected or uncertain responses keep the form.
  Submitted fields are disabled during the request, so a later success cannot erase newer edits. An empty plan
  explains that it needs a task before it can be proposed.
- Plan tasks have a Remove control. Removing one drops dependencies on that task and renumbers the remaining
  dependency indexes, so an accidental blank task is recoverable without losing the plan.
- Member invitations, budget edits and integration secret fields retain input after failed saves. Confirmed
  success clears only submitted text, preserving newer edits. Budget editing also has an explicit Cancel action.
- Companies has separate Rename and Create forms. Rename starts with the saved name; pressing Enter there renames
  the current company without creating or switching to another. Creation retains its own input. Duplicate submits
  are guarded, and switching companies is disabled during the request.
- A confirmed employee action followed by a failed view refresh is labeled “Action saved.” Retry refresh only
  reads saved state; it never starts another action. Recovery controls appear immediately, with retained submitted
  inputs in a collapsed details section. A fresh attempt requires Start another run.

Validation: production build/typecheck, all 13 Node checks, frontend formatting and `git diff --check` passed.
Seven browser suites passed for this pass: expanded Muse layout, company scope, task handoff, support walkthrough,
and the new planning-recovery, settings-recovery and action-recovery suites. The action and support suites were
rerun after the final action-panel layout adjustment.

The new tests verify definitive refusals, uncertain saved responses, duplicate submits, matching receipt recovery,
retained newer edits, task dependency repair, and Enter-key form behavior. The action test confirms exactly one
action POST and one saved task across failed and successful refresh retries. The settings test injects secret-save
failures using fictional values in the disposable store. No provider starts or external service calls are required.

Compact receipts: `test-results/planning-recovery-browser.json`, `test-results/settings-recovery-browser.json`,
and `test-results/action-recovery-browser.json`. The inspected screenshots `settings-companies-mobile.png` and
`action-recovery-desktop.png` show the separate mobile forms and the visible recovery controls. Initial new-test
startup failures were a missing fixture Origin header and an unsupported temporary-directory prefix; correcting
those test harness details required no application changes.

## Limits and follow-up

This is local synthetic E2E evidence. It does not establish live provider quality, real Google/OIDC sign-in,
hosted multi-user acceptance, or publishing behavior. No deployment, real company mutation, live model call,
GitHub operation or GPU inference was performed.

The second pass addresses both initially deferred findings: company-scoped draft/recovery storage and the CEO
shortcut. It also closes the cross-tab request-routing bug discovered while tracing company selection. The
frontend build, all 13 Node checks, onboarding, Muse layout, company chat and conversation reply browser suites
passed after these changes; the new company-scope suite also passed and exercises the two-company boundary
directly. Its compact receipt is `test-results/company-scope-browser.json`.

Further source-audit findings remain for a separate focused pass: unsent conversation drafts are held in React
state and do not survive reload; a new saved context version can replace unsaved ContextEditor fields; keyboard
shortcut help lacks focus containment; and safely blocked meetings lack the explicit resume affordance supported
by the backend. These have not been changed or claimed as browser-verified in this pass.

The larger product opportunity remains reducing the number of decisions a new owner must make before their first
useful result. This pass repairs the signposting and status accuracy while preserving the explicit approval model.
