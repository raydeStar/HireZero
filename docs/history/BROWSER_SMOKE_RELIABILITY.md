# Browser smoke reliability

Date: 2026-10-18. Branch: `feature/employee-contexts`. Follows the Checks section of the
[Phase 3 report](PHASE3_IDENTITY_REPORT.md).

The headless Chrome smokes timed out intermittently while other builds ran. The code under test was fine; each wait
raced the app or the fixture. The fixes are all in the tests and one small shared helper,
[`tests/browser-smoke-wait.mjs`](../../tests/browser-smoke-wait.mjs). No assertion was removed or loosened.

## Causes

- **The fake provider was not built.** `dotnet build tests/HireZero.Company.Tests` did not build FakeCodex, which
  the browser fixture runs from its own output folder. In a fresh checkout every smoke that authorizes a fake turn
  waited forever. The company test project now has a build-order-only reference to FakeCodex.
- **Clicking a button that was still disabled.** "Review reply and context" is disabled while the send that created
  it is still refreshing. The smokes clicked it as soon as it appeared, so the click did nothing and "sealed reply
  packet visible" timed out (conversation-reply, employee-review).
- **Clicking while a command held the workspace lock.** `run()` ignores a click while the previous command is still
  refreshing. The smokes polled the API, saw the new state and clicked straight away, before the UI had finished its
  own refresh. "Complete after review" was dropped, so "reviewed task completed" timed out (task-handoff; measured
  has the same step).
- **Acting on a stale version.** After a message is saved, the scheduler claims its task and saves it again
  (`queued` → `blocked`, version + 1) after the UI has first drawn it. The UI only sees that on its 3-second poll, so
  company-chat's inline Cancel sent the old version. The server answered "This record changed" and "owner canceled
  inline" timed out. The double click itself was harmless: `run()` sends one command.
- **Reading the page before it caught up.** Some checks ran straight after a command or an API poll, so under load
  they read the step before it rendered:
  - measured and task-handoff checked that "Authorize and run one job" was disabled before the proposal was drawn;
  - onboarding read the context step, and clicked its approval checkbox, while the save was still running;
  - conversation-reply checked for the fake's partial-delta marker the moment "Reply pending" appeared.
- **A shared fixture port.** Every smoke listened on 127.0.0.1:5067, so two smokes running at the same time (for
  example from two sessions) fought over the listener.
- **A try-count budget.** `until()` gave up after 150 tries. Under load each probe takes longer, so the real budget
  varied, and a timeout said nothing about what the page showed.

## Fixes

- `until()` waits against a 30-second wall-clock deadline (`HIREZERO_SMOKE_WAIT_MS` overrides it). On timeout it
  reports the probe's last error, the route and any alert the page shows.
- A tracker installed before the app loads counts the page's own requests. `click()` waits until its button is
  enabled and no request is in flight, and the double-submit helpers wait for the same. "Stop reply" opts out,
  because it is meant to be clicked while the reply is pending.
- company-chat waits for the scheduler to settle the new task and for the card to show that state before it clicks
  Cancel twice.
- The checks above now wait first for the element to render, the page to go idle or the marker file to appear. Each
  then asserts the same thing as before.
- Each run picks a free loopback port unless `HIREZERO_TEST_URL` is set.

## Checks

- Built with `npm run build --prefix web` and `dotnet build tests/HireZero.Company.Tests`. FakeCodex now builds
  with it.
- Before the fixes, in a fresh checkout on an idle machine, all five named smokes failed every time.
  With FakeCodex built, company-chat, conversation-reply, employee-review and task-handoff still failed at the
  waits above.
- All seven smokes pass on an idle machine.
- Load run: each smoke ran 5 times in a row while two `dotnet build HireZero.slnx --no-incremental` loops ran in
  separate clones, 56 builds in all.
  - The first round passed 31 of 35 runs and exposed the immediate reads.
  - After those fixes, 35 of 35 passed: company, company-onboarding, company-chat, conversation-reply,
    employee-review, measured and task-handoff, 5 of 5 each.
- company-chat, conversation-reply and task-handoff also passed while running at the same time.
- No live model, GitHub or GPU calls.
