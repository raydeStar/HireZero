# Local browser qualification — 2026-10-26

**Result: the pinned candidate passed the real local runsc browser qualification with fictional pages.**
The final receipt also passed read-only readiness checks before and after restarting its dedicated daemon.
This qualifies the recorded local binding for the bounded suite; production deployment and real-site usefulness
remain unverified.

The owner explicitly approved the separate `HireZero-Qualification` WSL setup and fictional browser
qualification. Application execution remains disabled; this authorization does not cover live websites,
live models, script qualification or production activation.

## Host preparation

All 16 [pinned downloads](../../runtime/qualification-host/pin.json) passed exact size and hash checks before
import/installation. The new Ubuntu 24.04.5 amd64 distribution uses systemd, with Windows-drive automount and
Windows process interop disabled. The first configuration check caught an unapplied file; direct installation,
read-back and a restart of only this new distribution resolved it before any packages were installed.
All 23 expected base package versions matched the manifest. Automatic APT timers were disabled only here.

The exact 13-package installation simulation passed, then the installation and version read-back passed.
Selected packages are held. gVisor reports release-20261019.0; the SDK reports 10.0.403. Docker 29.9.2 has a
distinct engine ID, registered runsc, cgroup v2/systemd and the required memory/swap/PID/CPU limit capabilities.
Its initial containers, images and volumes were empty. These structural checks do not qualify execution.

Under [ADR 0109](../adr/0109-qualification-engine-without-bridge-networking.md), the new daemon was configured
before its first start with no bridge, firewall management, forwarding or masquerading. Only the `host` and
`none` built-in network records exist. Interface identities and forwarding values matched before/after.
Read-only IPv4/IPv6 firewall snapshots from the existing WSL environment matched exactly. The original Docker
Desktop engine ID was unchanged. No existing workload was stopped or reconfigured.

## Source and build

The original 543-file archive passed extraction/hash verification. Its first Linux build found an omitted
embedded Core resource, `config/model-routes.json`. Review of project declarations also identified
`runtime/codex/pin.json`. An explicit two-file supplement was copied and verified against the checkout,
bringing the source inventory to 545 files. The original archive and failed-build log are preserved.
The runbook export allowlist now includes both files. No application source was changed for this correction.

Locked NuGet restore passed. Five explicit source supplements preserve each addition/replacement and its
hash without rewriting the original archive. All 549 resulting source files matched both the checkout and the
Linux copy before the final documentation update. The final Linux test build passed with zero warnings/errors.
Private logs, engine/image inventories and installation identity remain outside source control.

## Bugs found and repaired

1. Docker reports added capabilities using `CAP_` names. Both adapters now accept the equivalent spelling
   while still refusing missing, additional, duplicate or malformed grants. The first attempt never started
   a browser because its envelope check rejected the equivalent names.
2. Docker's local log driver rejects its default compression with `max-file=1`. Both adapters now explicitly
   request and inspect `compress=false`, preserving their existing bounded single-file logs. The second
   attempt's inspected resource remained unstarted, with the daemon's exact configuration error retained.
3. The third attempt started Chromium but stalled during initialization before any page request. A bounded
   probe measured 507.6 MiB of the 512 MiB allowance and about one CPU in use. Suppressing the unused startup
   window and component-extension background pages resolved startup without increasing limits or weakening
   isolation ([ADR 0110](../adr/0110-browser-startup-within-existing-memory-budget.md)). Diagnostic discovery
   filters were not adopted. The successful startup probe exited before its five-second sample, so it did not
   establish a new memory peak.
4. After the fourth attempt passed, daemon restart reordered the same 41 runsc feature capabilities and
   invalidated the raw-text fingerprint. [ADR 0111](../adr/0111-stable-runtime-capability-fingerprint.md) sorts
   that list before hashing while retaining every entry and all other configuration. Changed grants, duplicate
   entries, runtime paths, argument order and security-feature values still invalidate the binding.

Each failed attempt reached a known terminal state and confirmed cleanup before a fresh, explicitly diagnosed
qualification attempt. No uncertain external operation was replayed. The fourth pass remains historical evidence;
the fifth pass supersedes it with the corrected fingerprint.

## Final evidence

| Check | Result |
| --- | --- |
| Linux build | Passed; zero warnings/errors |
| Focused adapter and fingerprint facts | 37 passed; zero failures/skips |
| Node runner/image contract | Passed |
| Fourth real browser qualification | Passed in 21.634 seconds; preceding fingerprint scheme |
| Fifth real browser qualification | Passed in 23.255 seconds; final fingerprint scheme |
| Saved receipt/readiness check | Passed before and after dedicated daemon restart; one fact each |
| Disabled execution check | Unavailable with `operator-disabled` before and after restart |
| Final owned containers / volumes | 0 / 0 |
| Host free disk | About 319.6 GiB at final preservation check; above the 10 GiB floor |

The qualified local image is
`sha256:58e364bd730aef720579e47fd7ace793c8f8dd93631e096cbcb5ea9bbd161b23`, rebuilt from the unchanged
digest-pinned Playwright base. Its actual inventory reports Node 24.22.1 and Chromium 156.0.8078.4, revision 1248;
the worker hashes were verified. The inventory's `qualified: false` remains intentional: qualification comes
from the separate receipt, not the build metadata.

The final receipt was issued at **2026-10-26 23:18:46 UTC**, expires **2026-11-02 23:18:46 UTC**, and is bound
to the private installation, image, CLI digest, engine/kernel/architecture and inspected runsc configuration.
It records dynamic itinerary execution, scoped broker refusal, bounded text/screenshot capture, cancellation,
deadline enforcement, saved-output recovery and independent resource release. The fixture serves only fictional
HTTP responses through the real broker and isolated Chromium. No live website or model call was made.

The execution envelope still uses 512 MiB, one CPU, 128 processes, network `none`, a read-only root, bounded
temporary filesystems, `no-new-privileges`, the original capabilities and Chromium's sandbox. No GPU, host
networking, extra privileges, personal profile or credentials were supplied.

Compact private evidence is retained under the ignored `test-results/runtime-setup/execution-final/`, including
`browser-qualification-5.json`, its test log, both `binding-*-restart.log` files, `final-verification-5.json`
and `preservation-final.json`. Raw engine identities and host paths must not enter the public repository.

## Remaining boundary

The dedicated distribution, pinned toolchain and candidate images are retained for review. Disposable test
resources were removed. Existing Docker Desktop identity/version, WSL interface identities, forwarding and
IPv4/IPv6 firewall snapshots matched the baseline. No existing workload was stopped or reconfigured.

No application host was started or application execution enabled. The readiness fixture's enabled branch only
inspects metadata; its network factory throws if invoked. This result does not qualify scripts, extended
scripts, a different machine or a production deployment. Real-site usefulness, hosted acceptance, customer
validation and the other product-phase requirements remain separate. Requalify before expiry or after a
material binding change; do not copy this receipt to another installation.
