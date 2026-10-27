# Dedicated local browser qualification host

**Owner-approved setup and fictional browser qualification passed on 2026-10-26.** The
[execution receipt](../../docs/history/2026-10-26-local-browser-qualification.md) records the exact local binding,
expiry, repairs and restart checks. This directory describes a dedicated WSL 2
distribution named `HireZero-Qualification`. It is not an application feature, production topology, or approval
to run these commands on other hosts. Approval covers this local experiment only; application execution stays disabled.

The existing Docker Desktop engine has no configured runsc runtime. Its current containers, volumes, settings
and existing Ubuntu distribution are outside this plan. The earlier conditional build permission does not
authorize a new runtime installation. The subsequent explicit setup approval is recorded under
[ADR 0109](../../docs/adr/0109-qualification-engine-without-bridge-networking.md).

## Reviewed inputs

[pin.json](pin.json) contains exact HTTPS URLs, versions, byte counts and hashes for 16 files:

| Component                        | Proposed version   |
| -------------------------------- | ------------------ |
| Ubuntu WSL amd64                 | 24.04.5            |
| Docker Engine and CLI            | 29.9.2             |
| containerd.io                    | 2.4.2              |
| Docker Buildx                    | 0.38.1             |
| gVisor bundle including sidecars | release-20261019.0 |
| .NET SDK                         | 10.0.403           |

Nine small Ubuntu packages complete the missing dependencies without a general upgrade. Exact package
versions and expected existing libraries are in the manifest. The browser recipe and immutable base remain
the separate [browser pin](../browser/pin.json).

The 16 files total **787,040,809 bytes**. Including the browser base gives **1,770,927,646 bytes** (about 1.77 GB,
1.65 GiB), before NuGet's repository-locked dependencies and small metadata requests. Reserve approximately
20 GiB for extracted files, builds and the new distribution disk. Require at least 30 GiB free before starting
and keep at least 10 GiB free after every large operation. The current proposal does not allocate that space.

Ubuntu image checksums and Ubuntu/Docker package indexes passed local GPG signature verification; package
index hashes match their signed release metadata. gVisor's GitHub asset digest agrees with its release checksum
file. The SDK SHA-512 comes from Microsoft's release metadata. The initial proposal checked metadata only;
actual artifact size/hash verification is mandatory before import, extraction or installation. See the
[execution receipt](../../docs/history/2026-10-26-local-browser-qualification.md) for the approved attempt's results.

Sources: [Ubuntu images](https://releases.ubuntu.com/24.04.5/),
[Docker packages](https://download.docker.com/linux/ubuntu/dists/noble/stable/binary-amd64/Packages),
[gVisor release](https://github.com/google/gvisor/releases/tag/release-20261019.0),
[Microsoft SDK metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).

## 1. Windows staging, after approval

Use PowerShell 7. The proposed private root is `$env:LOCALAPPDATA\HireZero\Qualification\20261026`.
It was absent during inspection. Recheck both that path and the distribution name immediately before execution;
do not reuse either if it already exists. Keep the current working directory at the repository root.

```powershell
$ErrorActionPreference = 'Stop'
$qualificationRoot = Join-Path $env:LOCALAPPDATA 'HireZero\Qualification\20261026'
if (Test-Path -LiteralPath $qualificationRoot) { throw 'Qualification directory already exists.' }
if ((Get-PSDrive -Name C).Free -lt 30GB) { throw 'Keep room for the laboratory and its exit door: at least 30 GiB.' }
wsl.exe --list --verbose
# The operator verifies that HireZero-Qualification is absent before the following writes.
$stage = Join-Path $qualificationRoot 'stage'
$downloads = Join-Path $stage 'downloads'
New-Item -ItemType Directory -Path $downloads -ErrorAction Stop | Out-Null
Copy-Item -LiteralPath runtime/qualification-host/pin.json -Destination (Join-Path $stage 'pin.json')
Copy-Item -LiteralPath runtime/qualification-host/wsl.conf -Destination (Join-Path $stage 'wsl.conf')
Copy-Item -LiteralPath runtime/qualification-host/daemon.json -Destination (Join-Path $stage 'daemon.json')
$pins = Get-Content (Join-Path $stage 'pin.json') -Raw | ConvertFrom-Json
foreach ($artifact in $pins.artifacts) {
    $file = Join-Path $downloads $artifact.name
    if (Test-Path -LiteralPath $file) { throw 'Refuse to overwrite a staged download.' }
    Invoke-WebRequest -Uri $artifact.url -OutFile $file -ErrorAction Stop
    if ((Get-Item -LiteralPath $file).Length -ne $artifact.bytes) { throw 'Download size differs from the pin.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm $artifact.algorithm).Hash -ine $artifact.hash) {
        throw 'Download checksum differs from the pin.'
    }
    if ((Get-PSDrive -Name C).Free -lt 10GB) { throw 'Free-space floor reached; preserve the staged evidence.' }
}
```

Export the **current** required `src/`, `tests/`, `runtime/browser/`, `runtime/codex/pin.json`,
`config/model-routes.json`, `Directory.Build.props`, `global.json` and `NuGet.Config` sources, including relevant
uncommitted changes, into `stage/source.tar`. The two explicit JSON resources are embedded by project files.
Use a reviewed
relative-file allowlist from `git ls-files --cached --others --exclude-standard`, restricted to those paths.
Reject links, paths outside the checkout and `bin/`, `obj/`, `wwwroot/`, `.git`, credentials, application data
or ignored outputs. Preserve a relative filename/SHA-256 manifest alongside the archive and compare it after
extraction. A HEAD-only archive would omit the implementation being qualified. Do not publish this staging root.

Import the verified OS file; this is the first new distribution, not a replacement for Ubuntu or Desktop:

```powershell
wsl.exe --import HireZero-Qualification (Join-Path $qualificationRoot 'distro') (Join-Path $downloads 'ubuntu-24.04.5-wsl-amd64.wsl') --version 2
if ($LASTEXITCODE -ne 0) { throw 'Import did not complete; inspect the existing state before another attempt.' }
wsl.exe --distribution HireZero-Qualification --user root --exec install -d -m 0700 /opt/hirezero-qualification
if ($LASTEXITCODE -ne 0) { throw 'Staging directory was not confirmed; inspect before continuing.' }
```

Copy the staged files via the exact new distribution's Windows share
`\\wsl.localhost\HireZero-Qualification\opt\hirezero-qualification`; this avoids treating binary archives as
PowerShell text pipelines. Verify the copied hashes. Save its original `/etc/wsl.conf`, then install this
directory's proposed `wsl.conf`. Stop only the new distribution with
`wsl.exe --terminate HireZero-Qualification` and start that same distribution again. **Do not use
`wsl --shutdown`, restart Desktop, change the default distribution or edit global `.wslconfig`.**

Before continuing, confirm systemd is PID 1, Windows drives are not mounted, and the new distribution has no
Desktop Docker integration, Docker socket, dockerd installation or Docker data from elsewhere. If it does,
stop and review the unexpected state; do not uninstall, disconnect or delete another engine. Disable automatic
APT update timers inside this new disposable distribution only, so the selected runtime profile cannot drift
silently while qualification is being prepared. Do not interrupt an active package transaction.

## 2. Install only the pinned stack in the new distribution

Every command in this section runs as root **inside `HireZero-Qualification`**, from
`/opt/hirezero-qualification`. Verify `WSL_DISTRO_NAME`, Ubuntu 24.04, amd64, the copied file hashes and every
`expectedBasePackages` version with `dpkg-query` first. Refuse mismatches rather than installing extra packages.

Run `dpkg --simulate --install` with exactly the 13 `kind: deb` files in the manifest. Review the result:
only those packages may be added; no removals, downgrades, replacement engine or unspecified dependency fetches.
After extracting the verified runtime and installing the daemon configuration below, use `dpkg --install`
with that same explicit array. Dependencies are all local files; do not run `apt -f`,
an unpinned install, or an upgrade as a fallback. Package scripts start the new distribution's Docker/containerd
services; that service effect is part of the installation being proposed. Hold the 13 selected package versions.

Inspect the gVisor and SDK tar inventories before extraction: refuse absolute/traversing paths or links escaping
their dedicated destinations. gVisor must provide `runsc` and its accompanying `gvisor-bin/`; preserve that layout.

```sh
set -eu
install -d -m 0755 gvisor dotnet
install -d -m 0700 source receipts
chmod 0700 downloads
python3 -m tarfile -e downloads/gvisor-x86_64.tar.bz2 gvisor
python3 -m tarfile -e downloads/dotnet-sdk-10.0.403-linux-x64.tar.gz dotnet
python3 -m tarfile -e source.tar source
chmod 0755 /opt/hirezero-qualification
test -x /opt/hirezero-qualification/gvisor/runsc
test -d /opt/hirezero-qualification/gvisor/gvisor-bin
/opt/hirezero-qualification/gvisor/runsc --version
/opt/hirezero-qualification/dotnet/dotnet --version
```

Keep downloads, receipts and build sources private even though the runtime parent and gVisor bundle must be
traversable/readable to the unprivileged runtime process. Confirm root ownership and no unprivileged writes to
the gVisor bundle. Compare extracted sources to the staged source manifest before building.

Refuse an existing `/etc/docker/daemon.json` or engine data. Install the reviewed [daemon.json](daemon.json)
**before installing Docker packages**, so their first service start cannot create a bridge or alter firewall
or forwarding settings in WSL's shared network namespace. Record interfaces, forwarding and available firewall
rules before and after startup. See [ADR 0109](../../docs/adr/0109-qualification-engine-without-bridge-networking.md).

```sh
set -eu
test ! -e /etc/docker/daemon.json
install -d -m 0755 /etc/docker
install -m 0644 daemon.json /etc/docker/daemon.json
```

Now install the previously simulated exact package array, hold those package versions, and inspect the new
engine using `/usr/bin/docker --host unix:///var/run/docker.sock info --format '{{json .}}'`. If a restart is
needed, restart only this distribution's Docker service. No pre-existing workloads are restart targets.

Compare the engine ID with the recorded Desktop ID and require that they differ. Require registered `runsc`,
Linux amd64, cgroup v2/systemd, and true `MemoryLimit`, `SwapLimit`, `PidsLimit` and `CpuCfsQuota` support.
The configuration keeps `runc` as the ordinary default, explicitly selects systrap/systemd for runsc and does
not expose a TCP Docker API. Bridge creation, iptables/ip6tables management, forwarding and masquerading are
disabled for this networkless qualification engine. [gVisor documents this cgroup configuration](https://gvisor.dev/docs/user_guide/systemd/).
If these checks fail, preserve diagnostics and stop; do not weaken the adapter's checks.

## 3. Build and qualify the existing browser candidate

Run the [browser qualification procedure](../browser/README.md) from the extracted `source/`, using only this
Linux engine. Restore the test project's existing NuGet lockfiles and build the selected test executable.
No application host is started and no application execution setting is enabled.

```sh
set -eu
export DOTNET_ROOT=/opt/hirezero-qualification/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
dotnet restore tests/HireZero.Integrations.Tests --locked-mode
dotnet build tests/HireZero.Integrations.Tests --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
/usr/bin/docker --host unix:///var/run/docker.sock build --pull=false --network=none --platform=linux/amd64 -f runtime/browser/Dockerfile -t hirezero-browser:candidate runtime/browser
HIREZERO_BROWSER_TEST_IMAGE="$(/usr/bin/docker --host unix:///var/run/docker.sock image inspect --format '{{.Id}}' hirezero-browser:candidate)"
export HIREZERO_BROWSER_TEST_IMAGE
export HIREZERO_BROWSER_TEST_DOCKER=/usr/bin/docker
export HIREZERO_BROWSER_TEST_ENDPOINT=unix:///var/run/docker.sock
HIREZERO_BROWSER_TEST_INSTALLATION="$(cat /opt/hirezero-qualification/receipts/installation-id)"
export HIREZERO_BROWSER_TEST_INSTALLATION
export HIREZERO_BROWSER_TEST_RECEIPT=/opt/hirezero-qualification/receipts/browser-qualification-new.json
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class BrowserRuntimeTests -noLogo
```

Generate `receipts/installation-id` once as a hyphen-free UUID, using exclusive file creation, before this block.
The qualification output must not already exist. Keep that identity and receipt
private. Retain compact build/test logs, the image inventory and exact engine/runtime/CLI versions. This suite
uses fictional pages through the actual isolated browser; it does not contact customer sites or call models.

A passing test must include independent resource-release confirmation. A skip, partial result, lost cleanup
acknowledgment or sandbox incompatibility is not qualification. Do not add `--no-sandbox`, host networking,
GPU access, privileges or more permissive mounts to force a pass. No automatic replay after uncertainty.
Script and extended-script qualification and application activation are outside this approved browser run.

## Preparation evidence

The [preparation receipt](../../docs/history/2026-10-26-local-runtime-pins.md) records metadata verification
and a private 543-file source snapshot. That archive was extracted and compared against every recorded file
hash, then its temporary verification copy was removed. The first Linux build found two omitted embedded
resources; an explicit two-file supplement preserves their hashes separately without rewriting the original
archive. Recheck the source manifest and any supplements against the checkout before
using it; later source changes need a newly reviewed snapshot. The later execution used five supplements and
verified all 549 files before the final documentation update. Successful builds and qualification are recorded
separately in the execution receipt; the preparation manifest alone never establishes qualification.

## Failure and cleanup

Preserve the exact partial state and compact evidence if any step fails. Inspect that state before resuming;
do not repeat an import or uncertain execution. Only the new distribution's recorded resources can be stopped
or removed. Keep receipts before any later explicit decision to unregister `HireZero-Qualification`; unregister
is destructive and is not an automatic failure handler. Existing Docker/WSL workloads and the source checkout
are never cleanup targets. The initial host setup does not qualify this environment for production.
