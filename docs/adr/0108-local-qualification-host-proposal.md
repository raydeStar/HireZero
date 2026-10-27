# 0108 — Proposed dedicated local qualification host

Subsequent status: the owner explicitly approved setup and fictional qualification on 2026-10-26.
[ADRs 0109](0109-qualification-engine-without-bridge-networking.md), [0110](0110-browser-startup-within-existing-memory-budget.md)
and [0111](0111-stable-runtime-capability-fingerprint.md) record execution decisions and repairs;
the [execution receipt](../history/2026-10-26-local-browser-qualification.md) records the local pass.
The proposal below preserves its original approval boundary.

Status: proposed; owner requested a reviewable setup plan, not installation. Runtime execution remains disabled.

The existing approved Docker Desktop inspection found no configured runsc runtime. The owner subsequently
requested a local setup plan. Provisioning a runtime into Desktop's internal distribution or modifying the
existing Ubuntu environment would mix this experiment with unrelated workloads.

Prepare a new `HireZero-Qualification` WSL 2 distribution with its own Linux amd64 engine. The proposed
[manifest and runbook](../../runtime/qualification-host/README.md) pin the OS image, Docker/containerd/Buildx,
gVisor bundle with sidecars, .NET SDK and missing Ubuntu packages. Existing repository runtime and dependency
locks remain unchanged. Only this dedicated engine would use the proposed systrap/systemd runtime registration.

This is a qualification target for the existing fictional browser suite, not a per-customer VM design or a
production deployment. Require a distinct engine ID and no Desktop integration, preserve the ordinary default
runtime, and keep application execution off. No existing distribution, engine, workload, global WSL setting
or Windows service is changed. Production readiness, script qualification and operator activation are separate.

Download metadata signatures/checksums and declared byte totals can be verified before installation. Actual
archive contents, Linux dependencies, engine capability and Chromium/runsc compatibility still require the
approved local experiment. A mismatch stops the attempt; it never changes a pin or weakens containment.

Installation approval, if given, must name this proposed environment, its downloads and service effects, the
browser build and fictional qualification, and its private output location. Destructive cleanup of the new
distribution is a later explicit decision. This proposal itself authorizes no installation or execution.
