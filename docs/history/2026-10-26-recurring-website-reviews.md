# Finite recurring website reviews

The preceding browser-qualification turn was verified progress. This increment joins the existing scheduler to
standalone browser itineraries, advancing recurring workflows and scoped browser execution without claiming
customer retention or production qualification.

## Delivered behavior

Under **Company → Recurring work → New schedule**, choose **Repeat a website review** and a saved standalone
itinerary. The draft shows the exact websites, steps, screenshots, viewport, per-visit limits and total reserved
compute. Saving the draft makes no visit. A separate owner consent authorizes only its finite remaining slots.

Each occurrence creates its own work task and dated captures. Shared compute admission is atomic; a failed
admission leaves no partial task or reservation. The schedule waits for confirmed cleanup before another slot.
Paused or uncertain execution is never replayed. Saved results remain reviewable after future visits end, and
each task still needs a selected excerpt, result approval and explicit completion.

E2E testing exposed that the existing work view hid browser tools on every scheduled task. It now exposes saved
browser jobs there while keeping manual itinerary creation unavailable for occurrences. Scheduled website and
script tasks also have accurate titles instead of appearing as ordinary conversations. Review wording uses
readable step names and bounded download units. See [ADR 0096](../adr/0096-recurring-website-reviews.md).

## Verification

- Core, Host and Company Tests builds passed without warnings/errors. TypeScript/Vite passed with the existing
  large-chunk advisory.
- **39 backend facts passed**, zero failures/skips, across `CompanyScheduledBrowserTests`,
  `CompanyScheduledScriptTests`, `CompanyScheduleTests` (including measured-deliverable cases) and
  `CompanyBrowserHostingTests`. Eight new facts cover exact consent and company scope, two reviewed occurrences,
  pause/cancellation, cleanup/restart, unsent recovery, atomic allowance failure, missed slots and runtime withdrawal.
- **1 company UI contract check passed**.
- **2 local Chrome desktop/mobile journeys passed**: the new recurring-website journey and the existing
  recurring-script regression after the shared scheduling and task-title changes. Earlier browser-test selector
  errors were corrected to the rendered button labels before the final successful run.
- C# and frontend formatting and `git diff --check` passed.

The recurring-browser desktop/mobile journey is recorded in `test-results/recurring-browser-e2e.log` and its
content-free receipt, `test-results/recurring-browser-browser.json`. The journey checks exact itinerary review,
lost save/approval replies, two finite occurrences, recovery while new execution is disabled, two separately
approved results, 34 allocated seconds, missing-template draft preservation and no mobile horizontal overflow.
Desktop/mobile itinerary and mobile history screenshots were visually inspected. Both owned fixture processes
exited and their disposable test directories were removed by the runners; compact evidence remains.

## Limits

All executor data is fictional. No real site was visited by the employee executor, container started, model
called, runtime upgraded, payment made or deployment published. Actual browser containment and real-site
qualification remain required. Captures are evidence; automatic change analysis and updates to company facts
are separate operations. Real recurring usefulness, retention and willingness to pay remain unproven.
