# Script outcome recovery UX — 2026-10-26

An unknown script outcome kept telling the owner to check its resource even after cleanup was confirmed and
the recovery button had disappeared. The message now follows the saved cleanup state: unresolved resources
offer the available recovery action, while released jobs explain that no resource remains to check. It still
states that the job will not run again. Member and unavailable-recovery messages avoid suggesting an action
the current viewer cannot perform. Raw runtime reasons remain available inside collapsed execution details.

The existing fictional browser journey now covers an uncertain result with a failed first cleanup, recovery
while new execution is disabled, confirmed release, and the same state after navigation/refresh. It verifies
that recovery guidance and its button disappear together, the execution count stays at three, held allocation
returns to zero, and the prior output download, approval, completion and handoff still work. The additional
fixture returns saved fictional metadata and never evaluates submitted code or starts a real container.

Verification:

- Web production build and TypeScript check passed; the existing bundle-size advisory remains.
- The locked company-test build passed with zero warnings/errors after the web build completed. An initial
  concurrent build hit stale generated asset references; serializing the dependent builds resolved it.
- The expanded browser journey passed, with zero browser errors, no mobile horizontal overflow, and zero
  real container or model executions. Desktop and 390-pixel mobile captures were visually inspected.
- Selected Prettier checks and diff whitespace checks passed. C# fixture formatting was applied.

Private evidence is in ignored `test-results/sandbox-browser.json`, `sandbox-desktop.png` and
`sandbox-mobile.png`. The initial restricted-network attempt was unable to reach localhost; the authorized
loopback-capable run supplied the browser evidence. This change does not qualify or activate the real runtime;
the separate [task-budget proposal](2026-10-26-script-task-budget-plan.md) still awaits owner approval.
