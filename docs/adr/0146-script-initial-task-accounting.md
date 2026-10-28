# ADR 0146: Account for the script worker's initial task

Status: implemented and locally qualified; application execution disabled. Supersedes ADR 0120's internal
`nproc=32:32` setting. See [execution evidence](../history/2026-10-27-script-task-accounting-correction.md).

## Context

The pinned runtime's task accounting charges the first root-spawned process to root even after it becomes the
unprivileged workload user. Later threads and children use that user's counter. The approved diagnostic observed
four workload threads plus 29 child processes under `nproc=32:32`: 33 actual tasks. See the
[source review and diagnostic](../history/2026-10-27-script-task-accounting-review.md).

## Decision

Tighten the exact soft/hard internal limit to `nproc=31:31`. Keep the approved **32 actual workload tasks** and
128 host tasks. The unchanged pinned supervisor creates only one child that changes identity; that child has no
capabilities and cannot regain root or raise its hard limit. This correction is specific to the pinned worker
and runtime, not a general claim about every UID transition. No image, runtime pin, CPU, memory, network, file,
output or deadline allowance changes.

Require the same observed total of 32 tasks, process creation refusal and thread creation refusal in runtime
qualification. Also verify the visible 31/31 limits and refusal to raise the hard limit to 32. Retain bounded
process/thread inventories on success and failure. Do not accept 33 as a passing result.

Advance the adapter identity to `docker-runsc-v5` and qualification protocol to `sandbox-qualification-v3`.
Older receipts cannot authorize the revised profile. Container inspection refuses either bound set to 32.
Settle outstanding resources with their original adapter/binding before upgrading; this identity change cannot
migrate or replay existing jobs. The qualification engine is verified empty before transfer. New application
execution remains disabled during qualification.

## Verification and authorization

Offline tests cover exact profile inspection and rejection of old receipts. The owner's subsequent authorization
allows at most ten bounded runtime jobs including the completed accounting diagnostic. The candidate uses one
remaining job first, with the existing image and a 15-second deadline. This lowers the internal setting to enforce
the approved actual ceiling; it does not increase the resource allowance. Stop and inspect any failure. Further
qualification must use a concrete bounded plan within the remaining allowance, with no uncertain replay,
downloads, image replacement, application activation or unrelated workload changes.

The corrected diagnostic and all six standard/extended qualification cases passed. Script and browser receipt
bindings also passed before and after the previously approved idle-daemon restart. Eight of ten authorized jobs
are consumed, counting the earlier failed diagnostic. This is bounded local qualification, not completion of
the earlier eight-extra-deadline reliability panel or production/hostile-workload acceptance.
