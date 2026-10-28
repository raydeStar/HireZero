# Relevant team suggestions and result handoff

## Findings and changes

The previous fixed priorities ranked CEO's incidental workflow match and Developer's incidental website match
above Marketing's content match. A weekly content-plan goal could therefore recommend the wrong starting role.
[ADR 0161](../adr/0161-recommend-the-requested-work.md) gives role-owned rules a shared specificity scale: requested
work, named deliverable, topic, general option. Only the strongest level is shown; equal matches follow goal order.
Explicit same-clause exclusions do not supply a match. The current approved goal remains the authority.

The installed walkthrough also showed that the existing-employee link opens its oldest conversation, which can
bury a useful newer result beneath earlier drafts. A separate latest-result shortcut now opens the exact saved
task and thread in Work review. It chooses the most recently updated awaiting-review or completed task with an
artifact, skipping requests that have not produced a result. The conversation link remains available.

## Verification

- The expanded fictional browser journey passes ten contrasting goal cases: content grounded in a website,
  accessibility for a marketing team, cross-team coordination, website copy versus website building, posts about
  software, content-report automation, prioritization, a named content-plan deliverable, and an explicit exclusion.
- The same journey completes Developer and CEO onboarding, reviews their exact context packets and saved first
  results, and completes both tasks after approval. Existing-employee reuse, revocation, unknown-goal exclusion,
  desktop/mobile layout and role-owned setup remain covered.
- A newer saved result in a second conversation opens directly even when a later request has no result yet.
  The task, thread, Work mode and visible result are checked; no duplicate employee or delegation plan is created.
- The first run exposed a stale test expectation for a removed Read result button. The test now asserts the
  automatically opened exact artifact and task. Its role-specific word-limit assertion also matches the current
  Developer and CEO contracts. The final journey passes with the actual review controls.
- The web build and required web/C# formatters pass. These checks make no live model calls or external requests.

Both local installations have the final web build with independent application and data backups. Each installation
matches all 162 staged files; all 146 non-web files remain byte-identical, preserving runtime and schedule bindings.
Accounts, approved briefs, employees, tasks, artifacts, model receipts and the exact two-occurrence schedule are
unchanged. Audit chains remain intact. The installed original pilot recommends Marketing, offers its existing
employee, and opens the accepted Draft 3 in the exact saved task and conversation through the new shortcut.
No real approval was changed. Temporary test tabs and disposable staging were cleaned up after completion;
backups and compact private receipts remain available. Total live model calls for this checkpoint: zero.

## Limits

This is a bounded English-language heuristic and a navigation improvement. It does not establish general intent
understanding, useful unedited model output or customer acceptance. Owners can choose another role from the
catalog. No new model request, knowledge approval, publishing or scheduled run is authorized by these controls.
