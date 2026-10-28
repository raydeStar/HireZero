# Script task accounting correction

Status: correction and standard/extended qualification passed; installed locally, application execution disabled.

[ADR 0146](../adr/0146-script-initial-task-accounting.md) lowers the exact internal counter from 32 to 31 to
preserve the approved ceiling of 32 actual workload tasks. The prior diagnostic consumed one of ten authorized
jobs. The next job tests this correction with the existing immutable worker image in the separate qualification
distribution. It cannot issue a qualification receipt or activate an application.

Transfer only the five changed adapter/qualification/test source files as supplement 12 after verifying the
existing 569-file source and supplements through 11. Build without restore using the installed locked stack.
Retain at least 10 GiB free and allow at most 1 GiB incremental output. Check exact engine/configuration/image,
idle resources and external workload state. Preserve the fresh boot's disabled network forwarding.

Run one fictional task-limit case, 15-second deadline, same CPU/memory/network/host limits, internal 31/31.
Require 32 actual workload tasks, process EAGAIN, refused new worker thread, refused hard-limit increase and
confirmed cleanup. Stop on either outcome, preserve compact evidence and verify original external state. No
automatic repeat, model request, download, image build or service restart is part of this first candidate job.

## Correction result and next bounded qualification

The Windows and Linux builds passed without restore or warnings. All 26 focused adapter/qualification facts
passed. The diagnostic then passed under the unchanged 15-second deadline: 32 actual workload tasks, process
and thread refusal, and confirmed removal of all resources. Its 24 host observations peaked at 88 tasks with
zero host PID rejections, zero OOM events and no changed limit. The source/image/configuration/external-state
audit passed. It issued no qualification receipt. Two of ten authorized jobs are consumed.

The next prepared run uses the existing six-case qualification unchanged: workload-task limit, protected
output/recovery, standard CPU deadline, linked-output refusal, extended 125-second success/recovery and extended
121-second CPU deadline. Each distinct case must pass and release its resources before the next starts. This
consumes at most six additional jobs; an unchanged uncertain outcome is never replayed. Existing limits, image,
disabled application execution and no-download/no-build policy remain. A new v3 receipt can be written only
after all six cases and cleanup pass. Verify both script and existing browser receipt bindings without creating
another job, and retain compact observations plus the final source/configuration/external-state audit.

All six cases subsequently passed, with six resource observations and a sampled peak of 88 host tasks. A v3
standard/extended receipt was issued, and both the script and existing browser read-only binding checks passed.
Eight of the ten authorized jobs are consumed. The earlier approved task-budget plan also authorized one
restart of the idle qualification daemon: perform that restart only after these successful checks and verified
empty resources, then require unchanged receipt hashes/configuration and both bindings again. This creates no
additional job and changes no application setting. The older eight-extra-deadline reliability panel is not
included in this ten-job allowance and is not claimed complete.

## Final evidence and delivery

| Case | Observed outcome | Wall time |
| --- | --- | --- |
| Workload process/thread limit | Completed; 32 actual tasks and both creation paths refused | 780 ms |
| Protected files, supervisor and result recovery | Completed; exact result recovered | 487 ms |
| Standard CPU deadline | Timed out as required | 2,389 ms |
| Linked output | Failed as required; no output admitted | 451 ms |
| Extended duration and recovery | Completed; exact result recovered | 125,453 ms |
| Extended CPU deadline | Timed out as required | 121,487 ms |

The six cases produced 12,000 host samples: zero PID-limit rejections, zero OOM events, no changed limits and
no missing-observation failure. Exhaustion peaked at 88 host tasks; every other case peaked at 32. These are
sampled observations, not a universal overhead bound. All containers and volumes were removed. The new receipt
uses protocol v3 and expires seven days after qualification; no application received it as an enabled binding.

After the dedicated idle-daemon restart, both script and original browser binding checks passed again. The
receipt hash, original engine/configuration/image/browser receipt and all 569 source hashes remained unchanged.
The final external-state comparison passed; other distributions stayed stopped. The temporary keepalive helper
was removed by its verified process identity. No distribution shutdown or unrelated service change was made.

The release is installed in both existing local workspaces with separate data/binary backups. All 180 release
files match each installation. Both services are healthy, audits intact, and saved work, measured model records
and spending records unchanged. The accepted weekly plan and exact two-occurrence schedule remain unchanged,
active with zero runs consumed. Browser/script execution remain disabled. No live model request, purchase,
publishing or outreach occurred. Eight of the owner's ten bounded runtime jobs are consumed; two remain unused.

This resolves the concrete 33-versus-32 accounting defect and earns bounded local standard/extended
qualification. It does not establish universal reliability, hostile-workload security, production activation,
actual compute costs or customer/commercial acceptance. Private logs, hashes, receipts and approval accounting
remain outside source control.
