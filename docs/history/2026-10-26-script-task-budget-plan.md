# Revised script task-budget qualification plan — 2026-10-26

Status: owner approved implementation and the bounded fictional qualification on 2026-10-26.
Application execution remains disabled. [ADR 0120](../adr/0120-script-runtime-and-workload-task-limits.md)
records the proposed decision; the [diagnosis](2026-10-26-script-runtime-task-exhaustion.md) records why it is needed.

## Exact proposed change

Keep the dedicated `HireZero-Qualification` distribution and pinned Docker/runsc/.NET/Node stack. Reuse the
already built worker-protocol-2 image by its immutable ID. No package or base-image download is planned.
Allow up to 1 GiB of additional build/test storage and retain at least 10 GiB free; the latest host check had
more than 317 GiB free.

Change only the script host task ceiling from **32 to 128**, and add a **32 soft / 32 hard per-UID workload
process/thread limit**. This permits additional host runtime overhead, so it is a real resource-policy change.
Retain one CPU, the default 256 MiB memory budget with no extra swap, network `none`, read-only root/image,
existing temporary-file/output/time limits, restricted capabilities and the protected supervisor. No browser
profile change, existing workload interruption or application activation is proposed.

The original [approved plan](2026-10-26-script-qualification-plan.md) states: “No weakening of limits,
privileges, network denial or file protections is permitted to obtain a pass.” It does not authorize raising
the host ceiling. Automatic approval review also rejected applying that source change before owner approval.
The owner subsequently approved both the source implementation and the bounded local experiment below.
The earlier automatic review rejection remains part of the history; this later approval changes the authorization.

## Implementation and offline gate after approval

1. Inspect the exact host PID limit and all three `nproc`, `nofile`, and `core` limits before start. Reject
   missing, duplicate, additional, malformed or altered values. Prove refusal and owned cleanup with focused
   fake-engine facts; these checks must never start Docker.
2. Advance adapter identity and qualification protocol, requiring workload process/thread exhaustion evidence.
   Prove that old receipts and receipts missing that evidence cannot authorize the new contract.
3. Add the fictional exhaustion fixture. It must verify UID 65534, zero effective capabilities, inability to
   regain UID 0 or raise the hard limit, and visible soft/hard limits of 32. It creates at most 40 sequential
   sleeping-child attempts, stopping at `EAGAIN`, and checks the guest task count does not exceed 32. While
   exhausted, one bounded Node worker-thread attempt must be refused. It then kills/reaps its children and
   writes only a small result. A memory failure, runtime panic or unrelated error is a failed test, not proof
   that the workload limit works. The container has a 15-second deadline and is independently removed.
4. Format and run the smallest covering offline checks and locked build. Record a new source supplement;
   verify existing Linux hashes before updating only reviewed files. Do not start an application host.

## Bounded real experiment after the offline gate

- Verify free space, exact distro/daemon identity/configuration and installed binary/image hashes, existing
  browser receipt, no unfinished containers, and external Docker/WSL network/inventory baselines.
- Run the new workload-limit case first. Stop this candidate on any failed assertion, unknown outcome, OOM,
  runtime panic or unconfirmed release; do not widen any further limit to continue.
- If that gate passes, run the five original cases: protected output/recovery, two-second CPU deadline,
  linked-output refusal, 125-second extended success, and 121-second CPU deadline. Issue a new seven-day
  receipt only after all six cases, release checks and binding checks pass.
- Run four additional, separately identified two-second deadline observations. Inspect host PID and memory
  counters while they run. Stop on the first unexpected outcome; preserve every result.
- Restart only the now-idle qualification daemon, verify script and original browser receipt bindings, then
  run four more distinct deadline observations. No job is resumed or replayed. This is **at most 14 fictional
  jobs**, normally about six minutes of runtime plus compilation and audit, with at most one job at a time.
- Require zero host PID-limit rejections, zero OOM events and no runtime panic for the accepted profile.
  Sampled peaks are observations, not proof of a universal maximum. Keep bounded private logs; use original
  daemon configuration for qualification. Any temporary diagnostic configuration must be restored and cannot
  supply a qualifying receipt for the original profile.
- Verify zero remaining owned containers/volumes, unchanged pins/configuration/browser receipt and external
  workload/network state. Export compact evidence and report all attempts. Preserve images and receipts;
  no distribution removal or unrelated storage cleanup is included.

Passing proves only these bounded local cases. Hostile-workload review, production activation, actual compute
costs, commercial terms and customer acceptance remain open. Both application execution switches stay disabled.
