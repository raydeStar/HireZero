# Goal-based team setup — 2026-10-25

Scope: context-first team assembly in P1.7/P2.3. Decision:
[ADR 0074](../adr/0074-goal-based-team-setup.md). The full product goal remains in progress.

## User-visible result

The approved brief no longer always sends the owner to Marketing. Role-owned goal matchers can suggest
Marketing, Developer or CEO, with a concrete reason and a way to browse every role. Matching uses only confirmed
or sourced goals in the current approved brief; it is a deterministic word heuristic, not semantic research.
When no goal matches, Marketing is explicitly presented as a general starting option. Suggestions neither hire
employees nor execute work.

Existing employees of a suggested role are offered first, including a route to finish onboarding. Retired employees
do not suppress a new setup. The owner can still intentionally hire more employees through the ordinary catalog.

Developer and CEO onboarding now supply editable responsibilities and department context from the approved brief.
The Developer can prepare a first technical plan; the CEO can prepare a first priority plan without needing other
employees. Both use the existing bounded deliverable policy, exact context/allowance authorization and artifact
review before completion. Technical advice does not imply a website visit, repository inspection or executed test.
Priorities do not become delegation authority. First-assignment descriptions now belong to their role.

## Verification

- Free disk before/after local work: approximately 316.6 GiB. Company test build passed with zero warnings/errors
  in 19.15 seconds. Final frontend type check and Vite build passed in 2.12 seconds.
- `CompanyAssignmentTests` plus `CompanyDelegationTests`: **12 facts passed**, 2.140 seconds. Two new facts verify
  Developer and CEO starter assignments, unchanged 22,000-token deliverable planning policy, exact company knowledge,
  no repository resource, no delegation plan, and refusal to complete an unreviewed result. The CEO case begins
  with no other employees. Existing assignment and delegation approval/revocation/budget tests remain passing.
- The new team Chrome journey passed against actual local routes, the store and built frontend with a fictional
  provider. A draft brief has no suggestions; approval of a technical goal offers Developer, preserves the goal
  in editable setup, fills department context, and leads through separate model authorization to reviewed
  completion. Preparation creates no provider job. The existing Developer link opens the same employee.
- Revoking the brief removes suggestions. Approving a replacement coordination goal offers CEO and completes
  a second reviewed assignment. A goal marked unknown, even when its text mentions a website and team, does not
  trigger those suggestions. The final company has exactly two employees, two completed tasks and no delegation
  plans. No JavaScript exceptions or horizontal mobile page overflow were recorded.
- Existing business-brief identity/correction/recovery and Marketing first-assignment/progress browser journeys
  passed. Shared company UI recovery checks passed. Routine tests used no live inference or external accounts.
- Initial new-fixture issues were corrected: its temporary directory now uses the test host's existing allowed
  prefix, and its policy assertion reads content inside collapsed instructions instead of requiring that text
  to be visibly expanded. These did not relax product checks.
- Desktop and 390-pixel mobile suggestion screenshots were inspected. The suggested-role action remains visible
  above mobile navigation. CSharpier checked four changed C# files; focused Prettier and whitespace checks passed.
- Final assets: `index-IFFWIZay.js`, `index-C92hLdNW.css`. The existing nonfatal bundle-size warning remains:
  507.82 kB minified entry script / 148.09 kB gzip. The threshold was not increased.

Ignored receipts: `test-results/team-build.log`, `team-tests.log`, `team-web-build.log`, `team-browser.log`,
`team-browser.json`, `team-brief-regression.log`, `team-marketing-regression.log`, `team-ui-tests.log`,
`team-suggestions-desktop.png` and `team-suggestions-mobile.png`. Browser fixtures cleaned up their own hosts
and temporary roots. No live model calls, charges, deployment, runtime upgrade or existing user-host restart occurred.

## Remaining acceptance

Keyword matching can miss nuance and does not prove a team recommendation is commercially useful. Real onboarding
and task quality need observed pilot sessions. Browser execution, paid entitlements/billing, premium environments,
broader connectors and the other remaining product requirements retain their separate acceptance gates.
