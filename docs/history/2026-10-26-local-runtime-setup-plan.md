# Local runtime setup proposal — 2026-10-26

Status at preparation: inspection and proposal only. No runtime, distribution or VM had been installed or configured.
The subsequent owner-approved setup and local fictional qualification are recorded in the
[execution receipt](2026-10-26-local-browser-qualification.md); application execution remains disabled.
The subsequent [pinned runbook](../../runtime/qualification-host/README.md) and
[manifest](../../runtime/qualification-host/pin.json) now resolve the main download versions, hashes,
sizes and required Ubuntu packages. See [ADR 0108](../adr/0108-local-qualification-host-proposal.md).

The approved Docker Desktop Linux engine has no registered `runsc` runtime; see the
[inspection receipt](2026-10-26-local-browser-runtime-inspection.md). Read-only WSL inspection also found a
running Ubuntu 22.04 distribution on WSL 2, with an x86_64 Linux 6.18 kernel and a Docker client/socket.
`runsc`, `dockerd` and `dotnet` were not on that distribution's PATH. This is not an exhaustive binary inventory.
Windows Hyper-V provisioning commands were not available in the current PowerShell session.

Docker Desktop runs its engine in its own WSL distribution. Installing a binary in the existing Ubuntu
distribution would not register it with that engine. Docker also warns about conflicting direct Engine/CLI
installations alongside Desktop. See [Docker's WSL documentation](https://docs.docker.com/desktop/features/wsl/).

The owner selected preparation of a local setup plan. The proposed target is a new WSL 2 distribution named
`HireZero-Qualification`, using Ubuntu 24.04 amd64 and its own Docker Engine. This reuses the installed WSL
facility without installing a Windows hypervisor or modifying the existing Ubuntu distribution. This is a
qualification experiment, not a production deployment or a claim of proven WSL/gVisor/Chromium compatibility.
The new distribution must have Docker Desktop integration disabled and a distinct engine ID; if those cannot
be established without affecting running workloads, stop and revise the plan to a dedicated Linux VM.

## Proposed local footprint

- A new private directory outside the public checkout, containing the distribution disk, verified downloads,
  a disposable copy of the required source files and qualification receipts. Choose its exact absolute path
  in the installation manifest. Do not copy application data, credentials, browser profiles or `.git`.
- Plan for roughly 20 GiB of additional disk allocation as an estimate, then verify package/image totals and
  preserve the repository's 10 GiB free-space floor. The browser base alone is about 984 MB compressed; the OS,
  Docker, SDK and extracted images are additional. The subsequent pinned manifest totals about 1.77 GB of
  downloads including the browser base, plus the additional repository-locked NuGet restore.
- Inside this distribution only: enable systemd, disable automatic Windows-drive mounting and Windows process
  interop, install the required toolchain, and register `runsc`. Do not change global `.wslconfig`, the default
  distribution, existing Desktop settings, existing Ubuntu packages, Windows services or firewall rules.
- No host ports, GPU access, personal credentials or production data are needed for the fictional browser suite.
  Per-job resource limits remain those already enforced by the adapter. WSL still shares the host WSL kernel;
  these settings are not a separate hardware isolation boundary.

Microsoft documents [custom distribution import](https://learn.microsoft.com/en-us/windows/wsl/use-custom-distro)
and [per-distribution configuration](https://learn.microsoft.com/en-us/windows/wsl/wsl-config). These support the
proposed layout, not a claim that Docker endorses two simultaneously integrated engines.

## Installation sequence to prepare for approval

1. Pin the OS image, Docker Engine, gVisor release bundle and .NET SDK, recording official origins and verified
   checksums/package signatures. Keep at least 10 GiB free after the estimated allocation. Include the gVisor
   sidecar binaries; a lone `runsc` binary is insufficient for current releases.
2. Import the verified root filesystem under the new name and private path, using `wsl.exe --import
   HireZero-Qualification <private-distribution-directory> <verified-rootfs.tar> --version 2`. Refuse an existing
   name/directory. Apply only that distribution's `/etc/wsl.conf`; if a restart is required, terminate only
   `HireZero-Qualification`, never use `wsl --shutdown`. No such command has been run.
3. Install/register the pinned runtime only in that dedicated engine, retaining its ordinary default runtime
   and using the explicit `runsc` selection for qualification. Use the default systrap platform initially.
   Record the exact daemon changes and any restart before execution; never restart the existing Desktop engine.
4. Copy the current required sources, including relevant uncommitted changes, using an explicit file allowlist
   into the new Linux filesystem. Use a .NET 10 SDK selected against the repository's `global.json` (10.0.100,
   latest-feature roll-forward), record the resolved SDK version and restore locked project dependencies.
   Run the repository qualification there against its explicit local Unix socket. Current adapters
   accept local Unix/Desktop named-pipe endpoints, not arbitrary TCP/SSH endpoints. A Windows-generated receipt
   cannot qualify a different Linux engine or CLI.
5. Build the existing immutable browser candidate and run `BrowserRuntimeTests` with a new private receipt path.
   Preserve a failure as failure, including Chromium sandbox incompatibility. Do not broaden runtime permissions
   or add `--no-sandbox` to obtain a pass. Review exact resource cleanup after every attempt.
6. Keep application execution disabled. Script and extended-script qualification are separate opt-ins; the
   owner's earlier conditional browser-build permission did not authorize their installation or execution.

Official references: [gVisor installation](https://gvisor.dev/docs/user_guide/install/),
[Docker registration](https://gvisor.dev/docs/user_guide/quick_start/docker/) and
[platform selection](https://gvisor.dev/docs/user_guide/platforms/). The repository's
[browser procedure](../../runtime/browser/README.md) remains the qualification contract.

## Verification and rollback

Before a build, verify that the new engine ID differs from Desktop, `runsc` is registered, the architecture is
amd64, and resources can be bounded. The qualification must save real results, recover without replay, refuse
off-origin requests and confirm removal of every owned resource. Failure leaves execution disabled and retains
compact diagnostics; it never justifies weaker containment.

Rollback is confined to the newly created distribution and its explicitly recorded files. First preserve the
receipt/logs and verify owned jobs have stopped. Stop only its engine/distribution. Removing its registration
with `wsl --unregister HireZero-Qualification` destroys that distribution and therefore requires an explicit
cleanup decision after verifying its identity and path; it is not an automatic failure handler. Existing WSL
distributions, Docker containers/volumes and source work remain outside this cleanup scope.

The proposed manifest is now prepared: 16 files totaling 787,040,809 bytes, or 1,770,927,646 bytes including the
existing browser base, plus repository-locked NuGet dependencies. Metadata signatures/checksums were verified;
the runtime payloads have not been downloaded. The runbook proposes a new private LocalAppData directory,
records the exact runtime registration and service effects, and retains separate installation approval.
The earlier approval was conditional on an already configured runtime; the later answer authorized this plan
only. Neither authorizes execution of the setup.
