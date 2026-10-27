# Sandbox provider foundation — 2026-10-25

Scope: the on-demand execution provider in the full three-phase product goal. See
[ADR 0065](../adr/0065-bounded-sandbox-provider.md). This is not yet a customer-enabled sandbox feature.

## Implemented

- Role-neutral bounded request/result/release contracts in Core, with flat input/output names and byte limits.
- A separate Docker integration using an explicit local engine endpoint and immutable image ID. It requires
  Linux/runsc and reported memory, swap, CPU and process-limit support. Ordinary Docker is not a fallback.
- Provision, inspect, execute once and ownership-checked release. Actual container configuration is verified
  before sending input. Host mounts/devices/network access, image pulling and restarts are absent. Inputs are
  sent over stdin, never as process arguments. The result envelope is retained in a bounded local engine log.
- A pinned-image Node supervisor with an unprivileged child, script deadline, bounded standard streams and
  selected UTF-8 output collection using non-following/nonblocking file descriptors. Missing/invalid outputs
  fail collection. Source syntax and pure file-handling behavior were checked locally.
- Explicit unknown create/start/removal states. Existing resources are not restarted. Interrupted creation
  cannot be cleared from an empty listing alone. Confirmed outputs remain available when removal fails.
- Separate execution and release calls: the caller can save the result before deleting resources. An exited,
  owned container's bounded log can recover the result after a host interruption without starting another job.
  The adapter does not invent an elapsed duration during recovery.

## Verification

The integration test project built with zero warnings/errors. All 13 adapter cases passed against a fake
engine in 0.146 seconds, covering ownership, envelope drift, unavailable/ordinary runtime refusal, revocation,
lost responses, no replay, output scope, explicit recovery and retained results after failed cleanup.
Two Node tests passed for packet validation and real local bounded file collection. Node syntax validation,
scoped Csharpier formatting and `git diff --check` passed.

The explicit real-runtime qualification test was discovered and skipped because no authorized image/engine
was configured. Read-only inspection found the Docker CLI installed but its Linux engine endpoint stopped.
No daemon was started or changed; no image was pulled/built, no submitted script ran, and no container was
created. Linux symlink protection, supervisor containment and actual resource enforcement remain unverified.
The qualification test and operator instructions are prepared under `runtime/sandbox/`.

The new project uses existing pinned dependencies; the test lockfile adds only its project reference. No
package/runtime version was upgraded. Compact logs remain ignored under `test-results/sandbox-*`.

## Next required work

Implement the durable company proposal/approval/dispatch ledger, monthly compute reservation and settlement,
approved-input revocation fences, cancellation and restart recovery, stored artifact/Inbox linkage, host
composition and the review/result browser journey. Then qualify the real runtime explicitly. Browser
automation and premium environments remain in the unchanged full acceptance ledger.
