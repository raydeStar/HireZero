# Recurring reviewed script reports

The all-three-phase goal remains active. This increment advances P2.1 and P3.2; it does not qualify a
production runtime or establish customer repeat-use value. The decision is [ADR 0087](../adr/0087-recurring-reviewed-scripts.md).

## Implemented

- Finite daily/weekly reports from an employee's saved standalone script review. The owner reviews exact
  code, document families, outputs, runtime tier, limits and remaining allocation, then gives separate
  recurring consent. Executing or discarding the original one-off proposal does not cancel that schedule.
- Each occurrence resolves exactly one approved revision in each selected document's replacement lineage.
  Missing, ambiguous or inaccessible approvals block execution; draft text never enters a job. Jobs retain
  exact selected revision IDs, versions, digests and text, with existing in-flight revocation checks.
- Durable slot claim and atomic creation of one task and one approved script packet, dispatched by the
  existing sandbox worker. Scheduled script tasks cannot enter the model queue or gain ad hoc execution.
- Current subscription and monthly compute limits apply per occurrence. Resource holds prevent another
  occurrence of that schedule without starving unrelated schedules. Pause/end revokes active work;
  recovery settles the original dispatch without replay. Missed slots never cause catch-up bursts.
- Released reports enter owner review: select output, approve the artifact, then complete the task. History
  opens saved work. Finite execution completion remains distinct from owner acceptance.
- Clear scope and allocation copy, a readable section heading and aligned consent checkbox. Copying a
  schedule with a discarded original script explains the missing selection and requires an eligible script.
  Existing authorized schedules retain their own grant.

## Verification

- Ten new focused recurring-script facts cover exact consent, company isolation, finite repeated reports,
  approved replacements, missing/ambiguous approval, pause/resume, quota rollback, separate original-proposal
  authority, paid entitlement loss, startup fencing and cleanup recovery without replay.
- 114 distinct backend tests passed across recurring scripts, existing schedules, delegation, premium scripts,
  host readiness, billing, sandbox lifecycle, adapter boundaries and runtime qualification validation.
  An initial recurring premium fixture failed because its subscription used real time while the store used
  controlled time. The fixture now accepts the same clock; all ten recurring facts and the related billing/
  premium checks pass. No product entitlement rule was relaxed.
- Three Chrome journeys passed: the new recurring-script flow, existing recurring work and standalone scripts.
  The new journey verifies lost draft-response reconciliation, fresh consent after reload, two executions,
  approved replacement input, held cleanup recovered without rerun, two owner-completed tasks and finite
  history after reload. It also checks the unavailable-template explanation when copying a schedule.
- Desktop and 390px mobile screenshots were inspected. The mobile journey reports no horizontal overflow.
  Frontend typecheck/build, affected .NET builds, company UI checks and formatting passed. The existing
  frontend bundle-size warning remains. A stale static-asset manifest was resolved by building the host
  after the frontend and before the test project.

Compact logs, screenshots and the JSON journey receipt are retained under ignored `test-results/recurring-script-*`.
Providers and data are fictional. No live model requests, payments, GPU work, containers, runtime upgrades,
commits, publications or deployments occurred. Fixture processes remove temporary profiles and data after
exit. Free disk space remained above 312 GiB.

Production containment qualification, recurring browser work, real provider acceptance, launch commercial terms,
full compute-cost accounting and observed customer usefulness remain open.
