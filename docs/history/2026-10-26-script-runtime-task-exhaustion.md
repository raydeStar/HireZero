# Script runtime task exhaustion — 2026-10-26

**The intermittent exit-code-2 failure is now diagnosed; a reliable fix is not yet qualified.**
Application execution remains disabled. This follows the earlier
[standard/extended qualification](2026-10-26-local-script-qualification.md), which passed once after failed
attempts. All following jobs used fictional CPU loops in the dedicated qualification distribution.

Bounded observations temporarily added a private gVisor panic log on the idle dedicated daemon. They retained
the approved one-CPU, 32-host-task, 256 MiB and isolation settings, stopped after the first unexpected exit,
removed each owned container and restored the original daemon configuration. These changed-profile
observations cannot issue a qualification receipt.

| Observation | Outcomes | Evidence |
| --- | --- | --- |
| Original worker, private panic capture | One clean timeout, then exit 2 | Sentry `newosproc`: failed to create an OS thread, `errno=11` |
| Original worker, cgroup sampling | One clean timeout, then exit 2 | `pids.current=32`, `pids.max=32`, `pids.events` reports 20 rejected allocations; no memory/OOM events |
| Worker protocol 2, V8/libuv pools of one | First case exits 2 | Same panic and host PID ceiling/rejections; no OOM event |

The supervisor printed a timed-out result in the failed cases. The adapter correctly refused to accept it
because the container did not exit cleanly. Samples include the runtime's own threads and platform processes;
the host PID ceiling is not an allowance of 32 additional customer processes.

The smaller-pool candidate under [ADR 0119](../adr/0119-script-worker-thread-budget.md) is
`sha256:5f9b91d402281b042f4f9dc3f45928517866456595e68845a4696a2aa09b745b`.
Its build inventory verified the unchanged Node/runtime pins and worker protocol 2. Five offline worker/image
checks and 27 focused adapter/qualification facts passed; the Linux build had zero warnings or errors. Those
offline results did not predict runtime reliability. No full qualification was attempted after its short probe
failed, and no receipt qualifies this image. The earlier worker's receipt is not transferred to it.

The diagnostic helpers verified the restored original engine/configuration, executable and browser-receipt
hashes, network/forwarding snapshot, and zero remaining containers and volumes. Host free space remained
above 317 GiB. Private logs, cgroup samples, panic output, inventory and source supplement eight are retained
under ignored `test-results/script-runtime-diagnostics/`. They are not copied into public documentation.
The final read-only audit verified all 567 Linux source hashes in the diagnostic snapshot and matched the
external Docker Desktop container/volume inventory and existing WSL network/firewall baseline. Later local
UI/documentation edits are separate from that tested Linux snapshot.

The [next proposal](2026-10-26-script-task-budget-plan.md) separates a bounded runtime allowance from a strict
workload limit. It changes the approved host ceiling and therefore awaits owner approval. The source change
to increase that ceiling was rejected by automatic approval review and was not applied. The current adapter
continues to require 32 host tasks. This diagnosis improves the evidence for P3.2/P3.6; neither reliability,
activation nor production acceptance is complete.
