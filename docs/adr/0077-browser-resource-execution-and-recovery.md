# 0077 — Browser resource execution and non-replaying recovery

Status: accepted; production image/qualification and durable company composition pending. Date: 2026-10-25.

## Problem

[ADR 0076](0076-browser-worker-protocol-and-output-fence.md) proves the browser process boundary but does not
provision or release an isolated resource. A host restart must recover output without repeating page interactions,
and a replacement engine must not claim an original resource was removed. Personal browsers and network access
inside the script sandbox do not satisfy the proposed isolation contract.

## Decision

- Core defines `IBrowserExecutor` and `BrowserExecution`, independent of any employee role. Execution, recovery
  and release are separate operations. Captures, host network measurements, elapsed time, browser exit, resource
  creation and resource removal remain separate observations. Missing measurements remain unknown.
- `DockerBrowser` is a distinct adapter within the sandbox integration. It requires an immutable local image ID,
  installation identifier, original Linux engine ID and scoped network-session factory. It uses the configured
  local Docker endpoint/CLI boundary. It never builds/pulls an image, installs a runtime, changes a daemon or
  falls back to ordinary container execution.
- Resource names bind the complete runtime identity (image, engine and installation), company and job. Labels
  also bind the exact request digest. Requests are copied before asynchronous execution, recovery or release.
  Cleanup requires the original recorded resource reference; wrong-company and replacement-runtime references
  are refused. Changed requests cannot take over a present resource.
- Inspect the original engine/image, require `runsc`, check for an existing resource, create once and inspect its
  configuration before starting. The profile has no network, a read-only image, no host mounts/devices/ports,
  private namespaces, one CPU, 512 MiB memory with no additional swap, 128 processes, 128 MiB `/work` and
  32 MiB `/tmp` tmpfs mounts, bounded file descriptors and no core dumps. Health checks, automatic removal and
  restarts are disabled. Engine-added access or relaxed inspected limits prevent start.
- The trusted supervisor is PID 1/root with only `SETUID`, `SETGID` and `KILL` capabilities. It clears supplementary
  groups and launches Chromium as UID/GID 65534 in a fresh process group with an explicit minimal environment,
  ephemeral profile and private CDP pipes. `KILL` lets it stop the owned unprivileged process group; the child
  receives no inherited ambient capability. No browser sandbox-disabling flag is supplied. Actual Chromium
  compatibility and containment must be demonstrated on the selected image/engine before activation.
- `runtime/browser/runner.mjs` refuses ordinary host execution. Its fixed binary paths are `/usr/local/bin/node`
  and `/usr/bin/chromium`; its root-owned, read-only bundle belongs at `/opt/hirezero/browser/`. An independent
  PID 1 watchdog bounds startup and stops the resource after the approved browser duration plus twelve seconds,
  including blocked output or cleanup. Host admission must account for this overhead. PID 1 and primary browser
  exit are not substitutes for the host's later confirmed resource-removal receipt.
- The CLI gains bounded duplex operation, preserving its sanitized environment and argument-list construction.
  Tenant input travels through stdin; URLs and page data never become engine arguments. The attachment has a
  deadline and bounded diagnostics. Stopping a client CLI does not assert that its container stopped.
- Authority is checked before provisioning/start, during resource mediation and after worker exit. Late
  cancellation/revocation withholds captures even if a terminal receipt arrived earlier. The network session is
  disposed after the attempt. The caller must record dispatch durably before entering execution.
- A repeated attempt in the same adapter is refused. After restart, an existing named resource is never started
  again. Lost create/start acknowledgements, malformed output, OOM, unconfirmed exit and engine changes remain
  uncertain. The adapter does not remove resources automatically or infer success from absent logs.
- Bounded local engine logs retain resource messages and the terminal receipt until the caller saves the result.
  Recovery checks original ownership/configuration, normal stopped state and complete scoped logs. Its parser
  has no broker and cannot make requests. Recovered worker counters remain diagnostics: host network usage and
  host wall time are unknown unless the caller separately persisted them.
- Save validated output before release. Removal targets the inspected container ID, then confirms absence on the
  same engine. Absence without confirmed creation remains uncertain. Cleanup and log recovery still work when
  the original engine loses new-execution support; a replacement engine cannot stand in.

## Qualification and composition still required

`AvailableAsync` reports structural readiness, not production qualification. The host does not register this
adapter. No production browser image or dependency pin is selected by this change. A worker image must contain
the fixed bundle/binaries above, no declared volumes, and label `io.hirezero.browser.worker=1`; that label alone
grants no trust. Its immutable digest, engine/runtime/CLI configuration and actual containment, supervisor,
deadline, network, recovery and cleanup checks require review and evidence before activation.

Next implement the image/qualification binding and durable company lifecycle: exact owner approval, shared
compute reservations including overhead, revocation/restart recovery, saved artifacts, retention, notifications
and task review. Offline adapter tests and host refusal do not prove Linux containment. The real-Chrome fixture
uses fictional pages and runs no container. P3.1 remains incomplete.

Runtime background: [gVisor architecture](https://gvisor.dev/docs/architecture_guide/intro/),
[Docker runtime setup](https://gvisor.dev/docs/user_guide/quick_start/docker/) and
[production considerations](https://gvisor.dev/docs/user_guide/production/). Routine checks execute no installation.
