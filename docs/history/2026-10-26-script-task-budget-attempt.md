# Revised script task-budget attempt — 2026-10-26

The owner approved [the revised profile and qualification plan](2026-10-26-script-task-budget-plan.md).
The adapter now requires a 128-task host ceiling and exact `nproc=32:32`, `nofile=128:128`, `core=0:0` settings.
Its identity advances to `docker-runsc-v4`; qualification protocol v2 requires workload task-exhaustion proof.
Old receipts and receipts without that proof cannot qualify the new profile. The worker image and runtime pins
were unchanged, and application execution remains disabled.

## Offline verification

The locked builds passed on Windows and the dedicated Linux qualification copy. The 28 focused adapter,
qualification and runtime-profile facts passed on both. Added refusal coverage checks altered host limits and
missing, duplicate, extra, malformed or changed process/file/core limits before script start, with owned cleanup.
A test-only cgroup observer withholds acceptance after PID-limit rejections, OOM events or missing/invalid samples.
Qualification now stops after the first failed case and confirmed cleanup.

## First real case: failed, stopped

Only **one of the maximum 14 approved jobs ran**: the 15-second-bounded fictional workload task-limit case.
It returned failed with child exit code 1 after approximately 1.145 seconds. No qualification receipt was issued.
The supervisor reported a missing requested output and replaced the child's stderr with that generic message.
The failing fixture assertion therefore cannot be identified from the retained result; it is not a qualified pass.

The resource monitor recorded 38 samples, a sampled peak of 90 host tasks, zero host PID-limit rejections and
zero OOM events, with the expected host limit throughout. These observations support that this attempt avoided
the earlier 32-host-task exhaustion; they do not establish the workload limit, successful containment or reliability.

The case confirmed resource release. The final audit found zero containers and volumes in the qualification
daemon, unchanged daemon configuration/image/browser receipt, and 569 verified Linux source files. Existing
Docker Desktop container/volume inventory and the other WSL networking state were unchanged. No daemon restart,
download, later test case, application activation or deployment occurred.

Compact private receipts are in ignored `test-results/script-task-budget-qualification/evidence/`, with source
supplement 9 identifying the tested source. Later fixture diagnostics are separate, unexecuted changes.

## Prepared diagnostic, not yet executed

The fixture now prepares a bounded failure output containing its stage and assertion message before rethrowing.
All existing assertions, cleanup and resource limits remain. An explicit diagnostic-only test option returns after
the single workload case and cannot write a qualification receipt. A separate owner decision is required before
another job because the approved plan required stopping this candidate on a failed assertion.

Subsequent outcome: the owner approved the separate single diagnostic. Its
[recorded result](2026-10-26-script-task-limit-diagnostic-plan.md#execution-result) identifies a 33-versus-32
workload-count assertion, confirms cleanup and retains the unqualified status. No later qualification cases ran.
