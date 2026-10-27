# Local browser qualification preflight — 2026-10-26

The owner explicitly approved starting Docker Desktop, inspecting the local Linux engine, and downloading,
building and running fictional browser qualification only if `runsc` was already installed and available.
Application activation remained excluded.

Verified the installed Desktop executable's Authenticode signature as valid with publisher Docker Inc, then
requested hidden startup. The approved local Linux-engine endpoint responded successfully to `docker info`:

- Engine version: 29.7.2; operating system: Docker Desktop; architecture: x86_64; engine type: Linux.
- Default runtime: `runc`.
- Registered runtimes: `io.containerd.runc.v2`, `nvidia`, `runc`.
- No registered `runsc` runtime, so the existing qualification prerequisite is not met.
- Approximately 349 GiB remained free before the inspection, above the required 10 GiB floor.

No image was downloaded or built, no container qualification ran and no runtime/daemon configuration was
changed. The absent registered runtime does not establish whether an unregistered binary exists elsewhere;
it establishes that this engine is not presently a configured qualification target. No attempt was made to
install or register a runtime or weaken containment. Docker Desktop was left running; unrelated workloads
were not stopped. Application execution remains disabled.

The raw local inspection is retained only in ignored `test-results/browser-runtime-engine.json` because engine
metadata can identify the installation. A configured approved Linux/runsc host, or separately reviewed setup,
is still required before the pinned candidate can be built and qualified. The authorization was acted on;
there is no longer an unanswered startup-permission question.

A subsequent read-only WSL inspection and [setup proposal](2026-10-26-local-runtime-setup-plan.md) explain the
separate Desktop/Ubuntu engines and the dedicated Linux qualification target. No installation has been performed
or approved by the earlier conditional authorization.
