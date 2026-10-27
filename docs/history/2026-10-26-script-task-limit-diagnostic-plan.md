# One workload-limit diagnostic — 2026-10-26

Status: owner approved; the single diagnostic ran and stopped with a failed assertion. Cleanup was confirmed.

The [first revised-profile attempt](2026-10-26-script-task-budget-attempt.md) failed its workload-limit fixture.
The original approved plan says: “Stop this candidate on any failed assertion, unknown outcome, OOM, runtime
panic or unconfirmed release.” It stopped after one job, with cleanup confirmed and no qualification receipt.
The recorded result does not reveal which assertion failed because a missing output hid the child's stderr.

## Exact proposed diagnostic

- Reuse the same dedicated qualification distribution, installed stack and immutable worker-protocol-2 image.
  No download, image build, daemon restart or application activation is proposed.
- Keep the approved 128-task host ceiling, 32-task workload limit, one CPU, 256 MiB memory/no additional swap,
  network denial, read-only image, capabilities and all temporary-file/output limits.
- Transfer only the two reviewed fixture files after verifying source supplement 9. Build with existing locked
  dependencies and no restore. Keep incremental test storage within the earlier 1 GiB allowance and 10 GiB free.
- Run **one** fictional workload-limit diagnostic with a 15-second supervisor deadline. It keeps the same identity,
  capability, hard-limit, bounded process/thread-exhaustion and cleanup assertions. The only diagnostic change is
  writing the failing stage and a bounded assertion message to its expected output before rethrowing.
- Keep cgroup PID/OOM observations. Stop after this one case whether it passes or fails; do not run the other
  qualification cases or repeat it. Diagnostic-only mode cannot issue a qualification receipt.
- Confirm original resource release and unchanged external workloads/network state; export compact evidence.
  Investigate the concrete result before proposing another qualification run. Never loosen an assertion or
  resource limit to turn an unexplained failure into a pass.

This requests one additional observed job, not permission to retry until green. Application execution stays off.

## Execution result

The approved diagnostic transferred exactly the two fixture files as source supplement 10 and built with
existing dependencies: zero warnings/errors, no restore or download. One fictional job ran with the unchanged
image and limits. It failed after 945 ms at `count-workload-tasks`: the UID-based `/proc` count was **33**, while
the assertion required **32**. The prior identity, capability, hard-limit and process-exhaustion assertions had
completed; the subsequent worker-thread refusal assertion was not reached. This does not establish containment.

The observer recorded 29 samples, a peak of 90 host tasks, zero host PID-limit rejections and zero OOM events.
There was no qualification receipt, image build, daemon restart, application activation or additional job.
The final audit verified 569 source files, unchanged pinned image/configuration/browser receipt, zero qualification
containers/volumes, and unchanged external Docker inventory and other WSL network state. Compact private evidence
is retained in `test-results/script-task-limit-diagnostic/evidence/`.

The exact assertion is now known; the reason for the count discrepancy remains unproven. Keep the candidate
unqualified. Any proposed fix must preserve the intended ceiling and receive fresh executable verification;
changing the expected count to 33 would not demonstrate the approved 32-task contract.
