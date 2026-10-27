# Delegated browser and script journey — 2026-10-26

Scope: connect reviewed public website work to delegated assignments and a subsequent employee's script under
[ADR 0084](../adr/0084-reviewed-browsers-in-delegated-plans.md). This advances P3.1/P3.2/P3.3 and the execution
lifecycle requirement; it does not qualify or activate a production runtime.

Browser proposals now capture the exact parent and predecessor authority used by plan scripts. Each itinerary
still needs separate one-time owner consent. Browser requests contain only the approved public itinerary;
predecessor bodies and private employee context are not implicitly copied. Reviewed excerpts can complete an
assignment and become explicitly selected files for the next employee's script.

Model, script and browser admission now exclude competing execution on the same assignment. An unresolved
resource hold blocks further execution even when output is already available. The owner can keep another
draft, and its approval control explains the hold and becomes usable after confirmed cleanup. Completed tasks
disable unused proposals. A browser preparation conflict refreshes task state while preserving the itinerary.
Plan completion and the execution deadline do not erase valid history; source revocation still prevents reuse,
including through exact excerpt proofs retained after raw browser captures have been deleted.

Verification:

- **22 delegation tests passed**, including eight new focused browser/composition facts. They cover reviewed
  parent completion, unchanged token ceilings, history after reload/deadline, browser-to-script evidence,
  revocation through deleted captures, both model/browser admission orderings, competing isolated execution,
  cleanup holds, parent cancellation/expiry, changed predecessor/source revisions, late-output withholding and
  unused proposals after task completion.
- **44 browser/script/retention integration tests passed**. The shared quota regression now uses separate
  assignments so it continues to test aggregate limits independently of the new per-task exclusion rule.
- **Seven host tests passed** across browser and script composition. Four passed directly; three HTTP tests
  initially hit sandbox loopback restrictions and passed when rerun with local network permission.
- The new **Chrome browser-to-script plan journey passed**: one fictional browser execution, reviewed page
  evidence, one fictional script execution, reviewed output, parent completion, unchanged 44,000-token ceiling
  and 22 allocated compute seconds. It verifies a competing draft is disabled during uncertain cleanup,
  enables after confirmed release, and becomes disabled again when the task completes. It also verifies
  completed-history reload and downstream refusal after browser revocation and raw-capture deletion.
- Existing **standalone website-review and standalone script Chrome journeys passed**, including recovery,
  source revocation and runtime-disable behavior. Four new desktop/mobile screenshots were inspected; both
  assignments fit the 390px viewport without horizontal overflow or stale execution panels.
- Company UI recovery checks passed. Frontend typecheck/build, host build and both affected test-project builds
  passed. The existing main-bundle size warning remains. Ten affected C# files passed CSharpier checks;
  frontend/test formatting and whitespace checks passed.

Evidence is under ignored `test-results/plan-browser-*`, including the combined journey's JSON receipt and
screenshots. All launched test processes exited; Chrome fixtures removed their own temporary data/profile
directories. Free disk space was above 315 GiB. Unrelated dirty work and running applications were preserved.

All providers were fictional. No real website visit, model request, container execution, payment, publication,
commit or deployment occurred. Production containment/activation, recurring browser/script execution,
repository orchestration, premium environments and observed customer value remain pending. The full
three-phase product goal remains active.
