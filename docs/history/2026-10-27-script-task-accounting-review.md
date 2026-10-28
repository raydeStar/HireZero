# Script task accounting: source review and one-job diagnostic proposal

Status: diagnostic prepared, not authorized or executed. The revised runtime remains unqualified and application
script execution remains disabled. The accepted content plan and its two authorized weekly runs are independent.

## What changed the next action

The prior [one-job diagnostic](2026-10-26-script-task-limit-diagnostic-plan.md) recorded 33 workload tasks with a
32-task limit. Its result retained the failed assertion but not the individual process/thread counts. Repeating
that exact diagnostic would add little evidence.

The pinned gVisor release provides a specific accounting hypothesis:

- [Task creation](https://github.com/google/gvisor/blob/release-20261019.0/pkg/sentry/kernel/task_start.go#L154)
  increments the selected user counter. [The counter](https://github.com/google/gvisor/blob/release-20261019.0/pkg/sentry/kernel/kernel.go#L95)
  rejects an unprivileged increment above the current limit.
- [Changing user IDs](https://github.com/google/gvisor/blob/release-20261019.0/pkg/sentry/kernel/task_identity.go#L158)
  updates credentials and capabilities without moving that task's user counter.
- [Creating a later child](https://github.com/google/gvisor/blob/release-20261019.0/pkg/sentry/kernel/task_clone.go#L297)
  selects the current real user's counter when it differs from the parent's recorded counter.

Our protected supervisor starts as root and spawns the workload with UID/GID 65534. The inferred explanation is
that this first child remains charged to the original user's counter after its identity changes, while its
later threads and children count against UID 65534. That predicts one visible workload task outside the 32-task
counter, matching the observed 33. This is source-based inference, not a new runtime observation or proof that
every other accounting path is safe. Neither the expected count nor the approved limits have been changed.

## Prepared change and exact proposed execution

Only the qualification fixture changes. It records the workload PID and bounded numeric process inventories
before exhaustion and at the failed count: PID, parent PID, real UID and thread count, plus spawned child PIDs.
Each inventory is limited to 48 rows, and spawning still stops at the first refusal within 40 attempts. Missing
fields, unreadable rows or excess inventory fail the diagnostic. No command lines, environment, application data
or credentials are collected. Both success and failure retain the inventory; the assertion still requires 32.

The proposed execution is one fictional job in the existing qualification distribution, using the same immutable
worker image and pinned stack. Transfer only the fixture as source supplement 11 after verifying supplement 10.
Build with existing locked dependencies and no restore. Allow at most 1 GiB incremental storage and retain at
least 10 GiB free. Verify the exact engine/configuration/image, idle resources and external workload baseline first.

Keep the approved 128-host-task ceiling, soft/hard workload limit of 32, one CPU, 256 MiB memory/no extra swap,
network denial, protected supervisor and all existing output/file/capability limits. The supervisor deadline is
15 seconds. Diagnostic-only mode stops after this one case on either outcome and cannot issue a qualification
receipt. Confirm cleanup and unchanged external workloads/configuration, then retain compact private evidence.
No download, image build, daemon restart, application activation or automatic repeat is proposed.

If 33 reappears, inspect the retained breakdown against the prediction before proposing a runtime fix. If the
count differs or an earlier assertion fails, retain that result rather than changing expectations. This diagnostic
does not approve a different process limit, runtime upgrade or replacement worker design.

The previous one-job authorization is consumed and required stopping. A fresh owner decision is therefore
required for this additional executable job; the ongoing Luna authorization does not cover runtime qualification.

## Offline preparation evidence

The updated integration-test project builds without restore, warnings or errors. The embedded JavaScript passes
Node syntax checking. A private offline probe of the actual snapshot function retains a fictional 33-task total,
refuses missing fields/unreadable rows/49-row inventories, and retains numeric provenance in a bounded failure
record. No container, application model request or runtime job was started. These checks cover diagnostic
instrumentation only; they do not test containment or turn the source hypothesis into a runtime result.
