# Sandbox worker qualification

This is the bounded JavaScript worker for [ADR 0065](../../docs/adr/0065-bounded-sandbox-provider.md).
The host can register the adapter using an operator qualification receipt. New execution remains off by default.
Durable approval, allocated runtime limits, saved outputs and recovery are composed in the company workspace;
real containment still requires the explicit qualification below. Fictional browser checks do not qualify an engine.

The [existing base pin](pin.json) now has a Linux amd64 build inventory and an allowlisted context under
[ADR 0113](../../docs/adr/0113-script-image-inventory-and-local-qualification-plan.md). Its public registry
metadata declares Node 22.23.3 and 60,697,390 compressed layer bytes. The build verifies the actual version,
protected executable/worker paths and file hashes, writing `/opt/hirezero/image-inventory.json` with
`qualified: false`. That inventory is distinct from runtime qualification. The
[local standard/extended plan](../../docs/history/2026-10-26-script-qualification-plan.md) was subsequently
owner-approved. Its [execution evidence](../../docs/history/2026-10-26-local-script-qualification.md) records
a complete five-case pass and receipt checks across a dedicated-daemon restart, but also two earlier runtime
failures. The [subsequent diagnosis](../../docs/history/2026-10-26-script-runtime-task-exhaustion.md) identifies
host task exhaustion. Worker protocol 2 uses smaller Node pools but still failed its first runtime observation;
it has no qualification receipt. The [revised resource plan](../../docs/history/2026-10-26-script-task-budget-plan.md)
is now owner-approved. The adapter requires a 128-task host ceiling and separate soft/hard 32-task workload
limit under ADR 0120. The [first workload-limit case](../../docs/history/2026-10-26-script-task-budget-attempt.md)
failed and stopped qualification. No new receipt was issued; earlier failures remain recorded.

An operator provisions a Linux Docker engine with the `runsc` runtime using the
[gVisor guide](https://gvisor.dev/docs/user_guide/quick_start/docker/). Runtime/daemon installation and service
restarts are separate operator actions. The application performs none of them.

After explicit approval of the image build and runtime qualification, run from this repository on that host:

```sh
docker build --pull=false --network=none --platform=linux/amd64 -f runtime/sandbox/Dockerfile -t hirezero-sandbox:reviewed runtime/sandbox
export HIREZERO_SANDBOX_TEST_IMAGE="$(docker image inspect --format '{{.Id}}' hirezero-sandbox:reviewed)"
export HIREZERO_SANDBOX_TEST_DOCKER="$(command -v docker)"
export HIREZERO_SANDBOX_TEST_ENDPOINT="unix:///var/run/docker.sock"
export HIREZERO_SANDBOX_TEST_INSTALLATION="$(cat /srv/hirezero-private/sandbox-installation-id)"
export HIREZERO_SANDBOX_TEST_RECEIPT="/srv/hirezero-private/sandbox-qualification-new.json"
dotnet build tests/HireZero.Integrations.Tests --no-restore
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class SandboxRuntimeTests -noLogo
```

The build uses the exact base digest in the Dockerfile and may download it if absent. The adapter itself
accepts only the resulting immutable `sha256:` image ID and uses `--pull never`.

Qualification protocol v2 first checks the 32-task workload limit through bounded process/thread exhaustion,
unprivileged identity, zero effective capabilities and refusal to raise the hard limit or regain root.
The opt-in qualification also checks the real unprivileged script identity, protected PID 1 supervisor, read-only
input/image files, recovery of the bounded result log, a CPU-loop deadline, refusal of linked output and confirmed resource removal. A skipped test
does not qualify an engine. Generate the installation ID once as a GUID without hyphens, save it in private operator
storage, and retain it across restarts. The receipt output must be a new absolute path in a private directory owned
by the operator. Qualification writes it only after all checks and resource releases pass; failed checks still attempt
bounded cleanup. The receipt records the exact image, installation, endpoint, Docker CLI SHA-256 and selected
engine/runtime configuration, with a seven-day expiry. Keep these artifacts out of the public repository.

The fixture also creates a private `<receipt-path>.observations.jsonl` journal with case names and bounded
result metadata. It confirms cleanup before starting the next distinct case. Any failed case or unconfirmed
release stops this candidate immediately; no later case is started.
Any failed case prevents qualification; the observation journal is never a qualification receipt.
On the reviewed systemd/cgroup-v2 qualification host, set `HIREZERO_SANDBOX_TEST_CGROUP_ROOT` to its system slice
to record each container's sampled host task peak and PID/OOM counters. Missing samples, changed limits or
nonzero rejection/OOM counters fail the case before another job can start. Sampled peaks are observations,
not proof of a universal maximum. This monitor is test-only and changes no cgroup settings.

Configure the host with the matching `HIREZERO_SANDBOX_DOCKER`, `HIREZERO_SANDBOX_ENDPOINT`,
`HIREZERO_SANDBOX_IMAGE`, `HIREZERO_SANDBOX_INSTALLATION`, and `HIREZERO_SANDBOX_QUALIFICATION` settings.
Use `HIREZERO_SANDBOX_ENABLED=1` only after reviewing the qualification. Company script limits default to paused;
the owner must also enable them and approve each exact script packet. No setting builds/pulls images, installs a
runtime, or starts a Docker/WSL service. Startup checks configuration; a background probe and a fresh pre-dispatch
probe check readiness. A configured engine can receive bounded startup recovery commands for saved jobs even
when new execution is disabled.

Standard qualification permits scripts of at most 120 seconds. For the paid extended-script tier in
[ADR 0086](../../docs/adr/0086-paid-extended-scripts.md), obtain explicit approval for the longer qualification,
use a reviewed image containing the extended-tier worker, and set `HIREZERO_SANDBOX_TEST_EXTENDED=1` before the
qualification command above. This adds over four minutes: a 125-second successful script with receipt recovery,
a 121-second deadline test and confirmed removal of their resources. The receipt includes the exact checks
`extended-duration`, `extended-deadline`, and `extended-recovery-and-release` only after they all pass. An older
receipt without those checks remains standard-only. Never add the check names manually or treat fictional
tests as qualification. The tier permits at most 1,800 seconds; company paid entitlement may impose a lower
per-run ceiling. Existing memory, temporary-file and network limits remain unchanged.
The host refreshes runtime readiness independently of the job queue, waiting 30 seconds between probes, so a
long job does not delay qualification withdrawal checks until it finishes. Expiry is also checked directly
when reading runtime availability. These checks cancel authority; they never re-execute an interrupted script.

For a read-only readiness check, keep the same test binding and extended flag, set
`HIREZERO_SANDBOX_CHECK_RECEIPT` to the existing receipt, and run `SandboxQualificationBindingTests`. It uses
the production readiness adapter, verifies the expected standard/extended capability and confirms that
operator-disabled execution remains unavailable. It does not create a job, change a setting or renew evidence.

Set `HIREZERO_SANDBOX_ENABLED=0` and restart to retain recovery while disabling new scripts. Expired qualification
also blocks new execution while retaining recovery on the originally bound engine. Preserve the original receipt,
image and installation settings until all outstanding jobs settle. Replacing the engine must not turn an empty
resource list into evidence of cleanup on the old engine. Existing jobs keep their original provider identity.
Changing configuration never migrates those jobs or replays their scripts; restore the original binding to recover.

Requalify after engine, kernel, runtime configuration, image or CLI changes and before expiry. Renewals may replace
the configured receipt atomically only for the same engine/image/installation binding. Configuration fingerprints
are drift checks, not proof of an unchanged runtime binary or a signed security attestation: the operator must
control and pin the daemon/runtime binaries and their configuration. This focused suite does not establish every
containment property or qualify hostile workloads by itself. Production security review remains required.

Routine offline checks, which do not start Docker or execute submitted scripts:

```sh
node tests/sandbox-worker.test.mjs
node --test tests/sandbox-image.test.mjs
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class SandboxTests -noLogo
```

The worker receives one JSON packet through stdin. Submitted code runs as UID/GID 65534; the fixed supervisor
owns read-only inputs and emits the result envelope. Requested outputs must be ordinary UTF-8 files with flat
names and a combined size of at most 64 KiB. Missing or invalid selected output makes the job fail.

The caller must persist outputs before invoking cleanup. Execution retains a bounded private engine log so
recovery can retrieve a completed result without replay. Failed or uncertain cleanup retains an explicit resource
reference. Unknown creation/execution is never replayed; reconcile only the recorded resource and ownership.
Durable storage/recovery is a required caller contract and is not implemented by this adapter alone.
