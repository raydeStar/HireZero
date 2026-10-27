# Local standard and extended script qualification — 2026-10-26

**The final five-case run passed, but intermittent runtime exits remain a reliability blocker.**
The [subsequent diagnosis](2026-10-26-script-runtime-task-exhaustion.md) captured host task exhaustion;
the smaller-pool candidate also failed. No reliable fix has qualified.
The generated receipt and existing browser receipt passed read-only checks before and after restarting the
idle dedicated daemon. Application execution remains disabled. This is evidence for the bounded local suite,
not production acceptance or a demonstrated fix for the earlier failures.

The owner explicitly approved the [standard and extended plan](2026-10-26-script-qualification-plan.md),
including the pinned image build, fictional jobs and dedicated-daemon restart. No customer data, live model,
provider operation, payment, deployment or application activation was performed.

## Inputs and build

The separate qualification host retained its existing Docker 29.9.2, runsc release-20261019.0 and .NET 10.0.403
pins. Host disk space was approximately 319 GiB before execution and 317.5 GiB afterward, above the 10 GiB floor.
The original 549 source hashes passed verification. Supplement six transferred 48 selected files, including
the unchanged script worker absent from the earlier browser-only snapshot. An interrupted copy was inspected
and its one completed replacement and remaining staged hashes verified before completion. Supplement seven
records the operator-test reporting change. All 566 resulting source hashes passed the final Linux audit.
These hashes describe the tested snapshot before the final documentation update.

The locked test-project builds passed with zero warnings/errors. The exact Node base index, amd64 manifest,
config and four compressed layer blobs matched their recorded hashes; compressed layers totaled 60,697,390 bytes.
This engine reports the OCI index as the base image ID, so the config was independently checked in the local
content store rather than equated with that ID. The recipe's build steps had networking disabled. Its existing
constant-platform Dockerfile advisory was retained; no runtime or dependency pin changed.

The resulting script image is
`sha256:4c1aab4086e10ab8928fa34d4c9e17c26753ed70a86ef83cf61c4fa0cfed1924`.
Its inventory verified Node 22.23.3, protected paths and exact worker/pin hashes. The inventory still says
`qualified: false`; it cannot authorize execution. The unstarted inventory container was removed.

## Failed attempts and diagnostic limits

Two original qualification attempts passed the protected-output case and then returned `unknown` for the
short deadline case. Neither issued a qualification receipt, and both confirmed resource cleanup. Retained
daemon events showed exit code 2. A separate bounded deadline diagnostic emitted a `timed-out` envelope but
still exited with code 2; another minimal supervisor probe also exited with code 2. Refusing those outcomes
was correct: an output envelope alone does not establish a clean container exit.

Temporary [gVisor panic logging](https://gvisor.dev/docs/user_guide/debugging/) was used only on the idle dedicated
daemon. A diagnostic probe and standard suite passed with that logging configuration and produced no panic
explanation. Their results do not qualify the original profile. The exact original configuration was restored
after each diagnostic, and its hash and existing browser binding were rechecked. No limits, capabilities,
network restrictions, mounts, image pin or runtime pin were relaxed.

The operator fixture now writes a private, bounded observation journal beside the requested receipt. Each
case must confirm cleanup before another distinct case starts; an unconfirmed release stops the suite.
Assertion failures are retained so later approved cases can be measured after cleanup. Any failed case still
prevents receipt issuance. The journal contains case names and bounded result metadata, not script/input text.
This improves coverage and diagnosis; changed timing and cleanup do **not** prove the earlier runtime problem fixed.

## Final run and receipt checks

The complete run used the original runtime configuration and unchanged image, with one CPU, 256 MiB memory,
32-host-task ceiling, network `none`, read-only root/image and the existing bounded temporary/output storage.

| Case | Observed result | Adapter wall time |
| --- | --- | --- |
| Unprivileged script, protected supervisor/inputs and output recovery | Completed; UID 65534 and recovered output verified | 0.761 s |
| Two-second CPU deadline | Timed out | 2.551 s |
| Linked output | Refused; no output returned | 0.659 s |
| 125-second extended work, 140-second ceiling | Completed; exact saved output recovered | 125.533 s |
| 121-second extended CPU deadline | Timed out | 121.480 s |

The single opt-in fact passed in 252.395 seconds with all five cases and cleanup. Its receipt contains all seven
standard checks and all three extended checks, issued at `2026-10-27T01:11:15.9332378Z` and expiring at
`2026-11-03T01:11:15.9332378Z`. This is one complete passing run after the two earlier aborted attempts,
not a claim that every attempt passed or that repeat reliability is established.

Four read-only facts passed: script and browser receipt checks before and after restarting only the idle
qualification daemon. They exercised enabled/disabled availability in test objects without activating an
application or dispatching jobs. The final audit found zero containers and zero volumes in that engine.
Its original identity, runtime configuration, executable hashes, browser receipt, network interfaces and
forwarding settings matched. Docker Desktop's identity, container/volume inventory and the existing WSL
network/firewall snapshots also matched their initial state.

Compact private evidence is retained under ignored `test-results/script-qualification-setup/execution/`, including
failed attempts, diagnostics, source manifests, inventory, per-case observations, receipt checks and final audit.
Installation identities, daemon details and raw receipts are not copied into public documentation.

## Remaining work

P3.2/P3.6 now have a complete local standard/extended pass and receipt continuity evidence. **Runtime reliability
is not accepted despite the subsequent task-exhaustion diagnosis.** The
[revised profile plan](2026-10-26-script-task-budget-plan.md) awaits approval; establish repeatable behavior
before activation. A diagnostic pass with a changed profile cannot silently replace the
reviewed configuration. Production qualification, actual compute-cost accounting, commercial terms and customer
usefulness/payment validation remain open. Both application execution switches remain disabled.
