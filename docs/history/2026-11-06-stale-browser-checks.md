# Nine stale browser checks · November 6, 2026

Nine browser checks had been failing since before the morning desk (browser-review, business-drafting, company,
company-model, company-onboarding, freshness, muse-layout, task-cost, walkthrough). Most had fallen behind
intentional UI changes; three found real product problems, which are fixed here.

## Product fixes

- **A first visit led with nothing.** `GoalStart` counted a cancelled onboarding context draft as earlier work, so a
  company that had never started real work saw "Your earlier work is saved above" with nothing above it, instead of
  the suggested first assignment. Work now means a task the owner started: not a conversation turn, a context draft or
  something cancelled (`startedWork`). The desk uses the same rule.
- **The cockpit sheet focused the wrong button.** On narrow screens the cockpit opens as a sheet, and focus went to
  New outcome rather than Hide cockpit (ADR 0122), so Enter right after opening started a new outcome. `Modal` now
  focuses a control marked `data-autofocus`; the cockpit marks its close button.
- **The hiring guide repeated itself on phones.** On People, the live strip showed "1 item needs you · finish
  onboarding" above the open guide for that same item, pushing the form below the first screen. The strip leaves out
  onboarding and context items on People, and the guide's context help is tighter on phones.

## Checks brought up to date

Departments, Activity and Integrations moved under Settings; the Chat rail button is gone; Home opens on the desk
(checks that read Home's cards use the `home-start` preference); needs open in Work mode; the review packet loads
folded; Approve brief is disabled while an approved brief exists; Send is one sign-off by default; job costs sit inside
"Task costs and reconciliation"; the goal form gives way to progress while saving; a conversation with one request is
labelled by its title alone.
- **Show in chat lost the work right after a resize.** Widening past 1600 px and pressing Show in chat could close the
  work instead of opening chat beside it, because the media listener had not reported the new width yet. The button
  now reads the width when it is pressed.

## Full run

All 44 browser checks pass: 40 in one full run, and the other four after their updates (employee-review and
task-handoff now choose review-first explicitly where they test it, since Send is one sign-off by default;
conversation-reply's held reply does the same; schedules after the Show in chat fix, twice in a row).
