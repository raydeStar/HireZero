# 0120 — Separate script runtime and workload task limits

Status: accepted and implemented, 2026-10-26. First qualification case failed; no new receipt issued.

The internal 32/32 setting is superseded by [ADR 0146](0146-script-initial-task-accounting.md), which accounts for
the initial task while preserving the 32 actual workload / 128 host ceilings. The history below records the
original decision and failures.

The [32-task qualification diagnostics](../history/2026-10-26-script-runtime-task-exhaustion.md) captured a
gVisor Sentry `newosproc` panic and host PID-limit rejections. The host cgroup includes Sentry, filesystem
service and platform overhead. Reducing Node background pools did not resolve the failure. A passing run
under the original profile cannot erase that evidence.

Use a fixed **128-host-task ceiling** for the whole script container and a separate **32-task per-UID
workload limit**, with both soft and hard `RLIMIT_NPROC` set to 32. Submitted code retains UID/GID 65534,
no capabilities and no privilege escalation. The fixed supervisor has a separate UID. These are different
accounting boundaries: this proposal increases permitted host tasks and must not be described as keeping
the original resource envelope unchanged. The 128 ceiling is a bounded candidate to measure, not a proven
minimum or an already qualified value.

The pinned gVisor implementation increments the UID counter for each new task and returns `EAGAIN` above
the process-count limit unless the caller has a privileged capability. See
[the counter implementation](https://github.com/google/gvisor/blob/release-20261019.0/pkg/sentry/kernel/kernel.go)
and [task creation](https://github.com/google/gvisor/blob/release-20261019.0/pkg/sentry/kernel/task_start.go).
This source review motivates the proposal; it does not replace an actual workload-exhaustion test.

Require exact host PID and complete `nproc`, `nofile`, and `core` limit inspection before
starting a container. Missing, duplicate, extra or altered entries must refuse execution and retain owned
cleanup. Advance adapter identity and qualification protocol so earlier receipts cannot qualify the new
contract. Require a new workload process/thread exhaustion check in every qualifying receipt. Retain the
worker-protocol-2 image candidate and existing runtime pins; changing either requires a separate review.

One CPU, 256 MiB default memory with no additional swap, network denial, read-only image/root, bounded
temporary storage, output, time, capabilities and permission checks remain as before. No application is
enabled by this proposal. A failed or uncertain result is retained, never silently replayed or accepted from
printed output alone. The [execution plan](../history/2026-10-26-script-task-budget-plan.md) defines the bounded
local experiment and stop conditions. Production deployment and hostile-workload acceptance remain separate.

The [first attempt](../history/2026-10-26-script-task-budget-attempt.md) stopped after its workload fixture failed.
Observed host PID/OOM counters were zero and cleanup passed, but no qualification follows from those partial
observations. The separately approved [one-job diagnostic](../history/2026-10-26-script-task-limit-diagnostic-plan.md)
stopped at a workload count of 33 where 32 were required. Cleanup passed; the discrepancy remains unresolved
and the revised profile is unqualified.
