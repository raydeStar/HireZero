# 0065: Bounded sandbox provider

Status: Accepted. Provider implementation in progress; not enabled in the host.

## Decision

The first on-demand compute provider uses an explicitly configured local Linux Docker engine with the `runsc`
runtime. Ordinary Docker and host process execution are not fallback providers. The operator supplies an
immutable local image ID built from `runtime/sandbox/Dockerfile` and a durable installation identifier. The
Dockerfile reuses the repository's existing pinned Node base; this change upgrades no runtime or package.
No application request starts Docker, installs a runtime or pulls an image.

The provider accepts bounded JavaScript, named UTF-8 inputs and selected output names, with no host paths,
credentials, tool registry, network permission or model identity. The contract is role-neutral. Each job gets
a fresh named container with installation/company/job ownership labels. Before sending inputs, the provider
inspects its actual runtime, image, mounts, namespaces, capabilities, resource limits and entrypoint. A drifted
envelope refuses execution. An existing resource is reconciled separately and is never restarted as new work.

Limits include one CPU, 32 processes, 128–512 MiB memory with no extra swap, 8–64 MiB working temporary storage
plus 8 MiB `/tmp`, and 1–120 seconds of script execution. Root filesystems are read-only; host mounts, host
devices, exposed ports and automatic restart are absent. Inputs travel only through stdin. A bounded local
engine log retains the trusted result envelope until the platform saves the result and explicitly releases
the container. The log driver uses one file with a 1 MiB rotation bound; operator storage must protect these
private outputs just as it protects company data. No unbounded/default container logging is used.
The selected output is at most 64 KiB of UTF-8 files, plus at most 64 KiB combined stdout/stderr.

The pinned Node supervisor runs as PID 1. It has only SETUID/SETGID capabilities, drops supplementary groups,
then starts submitted code as UID/GID 65534 with a fixed minimal environment. Input/script files belong to the
supervisor and remain read-only in a sticky working directory. A separate supervisor deadline exits the PID
namespace even if the submitted script loops or retains descendants. This isolation behavior requires the
explicit real-runtime qualification below; passing adapter fixtures is not proof of OS containment.

File output uses non-following, nonblocking descriptors, verifies ordinary files and enforces byte bounds.
Missing, linked, non-text or oversized requested files fail output collection. Child stdout cannot forge the
trusted result envelope. Result text remains untrusted tenant data.

Cleanup inspects ownership before removing the exact container ID, then verifies absence. Failure to remove
does not discard an already captured result or claim release. Interrupted create/start operations are not
replayed. An empty listing alone cannot settle an interrupted create: a delayed create could still arrive.
Recovery must carry whether creation was confirmed; observing and deleting the owned resource can settle
release, but ambiguous absence remains held. Execution never removes the container itself. The caller must
persist the result first and then invoke release. Recovery reads only the owned, exited container's bounded
log, checks the same envelope and output scope, and never starts or replays a job. Recovered results do not
invent a wall-clock duration that the adapter did not observe.

## Required composition work

The adapter is deliberately not registered in the product until these shared-platform pieces exist:

1. Durable company-scoped proposals binding script, approved inputs, outputs, image identity and limits.
2. Exact owner review and dispatch recorded before provisioning, with per-company compute admission.
3. Cancellation/revocation monitoring, crash recovery and immutable results/release receipts.
4. Durable artifact, task and Inbox linkage; unknown work remains held and is never replayed automatically.
5. A usable review/result interface and full browser journey with a fake provider.
6. Explicit qualification on a provisioned Linux runsc engine before claiming real sandbox execution.

Browser workflows, additional language/toolchain images, premium environments, storage/compute pricing and
commercial entitlements remain separate requirements of the full three-phase goal.

## Sources and limits

Runtime selection follows [gVisor's Docker guide](https://gvisor.dev/docs/user_guide/quick_start/docker/).
Container controls follow the [Docker run reference](https://docs.docker.com/reference/cli/docker/container/run/).
Result retention uses Docker's [bounded local logging driver](https://docs.docker.com/engine/logging/drivers/local/).
These controls are not a blanket security certification. The engine and pinned image are trusted operator
infrastructure; production isolation, kernel/runtime updates and operational monitoring still need qualification.

No daemon installation, service restart, image pull/build or real container run was performed in the initial
adapter implementation. The local Docker CLI was present but its engine endpoint was stopped.
