# 0119 — Script worker thread budget

Status: candidate failed its first runtime observation; not qualified. Execution remains disabled.

The approved local script qualification exposed intermittent exit-code-2 failures. A bounded diagnostic
captured gVisor's `newosproc` failure while the cgroup had 32 of 32 host tasks and increasing PID-limit rejection
events, with no OOM events. The process ceiling includes isolation-runtime overhead; it is not a budget of
32 additional customer processes. A later passing run alone did not resolve that failure.

Keep the existing one-CPU, 32-task, memory, storage, time, capability and network limits. Reduce the supervisor's
and submitted Node process's V8 background pool and libuv pool to one each. The
[pinned Node CLI](https://github.com/nodejs/node/blob/v22.23.3/doc/api/cli.md#--v8-pool-sizenum)
provides the V8 control. These are default worker concurrency settings, not a new security boundary; arbitrary
submitted code remains constrained by the unchanged outer resource envelope.

Record worker protocol 2 and the pool values in the image pin/inventory. Require the new image label before
admission, pass and inspect the exact ordered Node arguments, and require the pool settings in the supervisor.
Advance adapter identity so saved proposals bind the changed execution contract. Runtime versions, the Node
base digest and daemon configuration are unchanged. Existing image receipts do not qualify the new image;
retain original evidence and independently requalify this candidate. Owned-resource cleanup remains available.

Use focused offline checks, the same standard/extended operator suite, short repeated deadline observations
with PID-limit events, and receipt checks across a dedicated-daemon restart. No additional permission, larger
PID ceiling, host access or automatic replay may be used to obtain a pass. If reduced overhead is insufficient,
record that failure and review another candidate rather than silently increasing the approved limits.

## Observed result

The candidate built successfully and passed five offline worker/image checks and 27 focused .NET facts.
Its first two-second deadline observation still exited with code 2 and the same `newosproc` panic. The host
cgroup reached 32 tasks and recorded PID-limit rejections, with no OOM event. Smaller pools did not resolve
the runtime failure. No qualification receipt was issued for this candidate. The exact original daemon
configuration was restored and all diagnostic containers were removed.

The [diagnosis](../history/2026-10-26-script-runtime-task-exhaustion.md) retains the failures. A separate
[resource-profile proposal](0120-script-runtime-and-workload-task-limits.md) requires owner review before
changing the approved host ceiling. Pool sizing remains a candidate optimization, not a proven remedy.
