# Measured delegation and approved dependency results — 2026-10-25

Scope: advance context-first requirements P2.3 and P3.3 with an executable, reviewed coordination journey.
Decision: [ADR 0073](../adr/0073-measured-delegation-and-approved-dependencies.md). The complete three-phase
product remains in progress; these are local implementation and fictional-provider results.

## Changes

- The CEO's planning skill uses the existing bounded deliverable profile. Its company overview includes eligible
  skill IDs and private thread IDs, without exposing colleagues' conversations, notes or department knowledge.
  The Plans page can request a draft and opens its ordinary measured-work review. Generated JSON is presented as
  readable named assignments, dependencies and a shared allowance, with the exact proposal text expandable.
- After owner approval of the result, the owner can prepare and approve the exact plan. An artifact-derived plan
  retains its source artifact ID, version and digest. Only a planning-skill artifact can supply this authority.
  Revocation or replacement fences approval, dispatch and late provider results.
- Ordinary internal plan work can use separately approved measured jobs. New plan windows allow seven days after
  approval, while proposals still expire after 30 minutes. The form proposes 22,000 planning tokens per child;
  the backend retains strict-path minimums and permits up to 100,000 across at most four tasks.
- Measured admission never raises the approved parent ceiling. It checks remaining shared capacity and reserves
  the full job allowance atomically. A sibling's recorded usage no longer invalidates an otherwise unchanged
  prepared packet merely by changing the root revision.
- Dependent work waits for owner-reviewed completion and receives currently approved predecessor artifacts as
  explicit evidence. Private context stays separate. Missing or revoked evidence prevents preparation/dispatch
  and withholds late output while preserving reported usage settlement.
- An expired packet proven never sent can be prepared again within a valid parent. Unknown or dispatched outcomes
  cannot use this exception. There is no automatic replay.
- Plan cards link directly to each task, display its current work state and use readable status labels. Prepared
  work says it is awaiting authorization. Brief form guidance replaces the long introductory paragraph; full
  approval/sharing details remain available before submitting.
- A real browser race exposed a stale task version after a background status update. Conflict responses now load
  the latest workspace details and explain how to retry, preserving the entered allowance without replaying work.

## Verification

- Approximately 316.7 GiB free before builds. Core and company test builds passed with zero warnings/errors;
  the final fixture build took 7.22 seconds. Frontend type checking and Vite build passed in 2.00 seconds.
- `CompanyDelegationTests`, `CompanyAssignmentTests` and `CompanyProgressTests`: **14 facts passed**, 2.768 seconds.
  The six new delegation facts cover two-child completion, tenant/private-context isolation, unchanged ceilings,
  simultaneous prepared children, source/dependency revocation, late usage settlement and proven-unsent expiry.
- `EmployeeModuleTests`: **16 facts passed**, 0.102 seconds. Existing `CompanySuite.Inline`, `.Security` and
  `.MeasuredQueue`: **3 facts passed**, 32.999 seconds, including their internal identity, fencing and budget checks.
  Total for this slice: **33 backend facts passed**.
- The new delegation Chrome journey uses the real local HTTP routes, store and built frontend with fictional
  model replies. It covers readable CEO proposal review, a lost plan-creation response without duplication,
  explicit plan approval, premature dependency refusal, separate per-job review/authorization, approved results,
  both child completions, parent completion, persisted reload and the unchanged 44,000-token shared ceiling.
- The browser check deliberately submits a stale version, confirms the refreshed refusal preserves the allowance,
  then explicitly retries. It records three fictional model jobs and no held usage after completion. Private
  Marketing context is excluded from both CEO and Developer requests; Developer receives the approved premise.
- A fixture initially assumed one employee per role, despite its existing baseline seed. It was corrected to
  select the intended named fictional participants. The initial failed run correctly held uncertain accounting;
  no attempt was retried in that workspace.
- Existing planning/meeting draft and lost-response recovery passed after the conflict-refresh change. Existing
  business-drafting/first-assignment/progress browser regression passed after shared work-view changes. The
  company UI recovery checks also passed. No blanket suite or live inference was used.
- Desktop and 390-pixel mobile plan screenshots were inspected; task links, dependency text and completed states
  fit without horizontal page overflow. The browser recorded no JavaScript exceptions.
- Final assets: `index-CQISmjSM.js`, `index-DjalYyjw.css`. Vite reports a nonfatal entry-chunk size warning
  (501.50 kB minified, 146.28 kB gzip); the warning threshold was not raised. Route/component splitting remains
  a separate performance improvement.

Ignored evidence is in `test-results/delegation-*.log`, `delegation-browser.json`, `delegation-desktop.png`,
`delegation-mobile.png` and `delegation-proposal-desktop.png`. Each browser harness owns and cleans up its fixture
host, temporary profile and data root. No live model calls, real charges, public publishing, deployment, runtime
upgrade or restart of an existing user host occurred.

## Remaining acceptance

Real planning quality and usefulness need owner-observed work. Repository execution, browser automation,
sandbox orchestration and commercial billing remain separately governed and are not supplied by this change.
The seven-day plan window is not an unlimited recurring authorization. Existing plans keep their earlier window.
