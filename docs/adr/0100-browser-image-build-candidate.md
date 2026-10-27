# 0100 — Pinned browser image build candidate

Status: candidate prepared, subsequently built and qualified on the owner-approved local runsc host.
See the [execution receipt](../history/2026-10-26-local-browser-qualification.md). Production review and
activation remain separate. Date: 2026-10-26.

## Problem

The browser adapter, contained worker, durable approval and opt-in qualification suite existed, but the repository
did not provide a browser image recipe or a pinned browser distribution. An operator could not reproduce the
expected image from this checkout. Fictional local Chrome tests did not fill that gap.

## Decision

Add a Linux amd64 candidate under `runtime/browser/`, with an explicit Dockerfile and `pin.json`. The base is the
platform-specific immutable Microsoft Playwright `v1.64.1-noble` image, including its Node 24 runtime, browser
binaries and system dependencies. Its digest pins the actual Node patch and all transitive image contents; the
build inventory records the executable version and hash. This adds a separate browser image candidate and does
not change the native Codex or script-sandbox runtime pins.

The public registry index, selected manifest and configuration were fetched; each fetched object was SHA-256
checked against its parent descriptor. The selected configuration is Linux amd64 and declares no volumes. The
base layers total 983,886,837 compressed bytes. The recorded source bundle declares Chromium revision 1248,
Chrome for Testing 156.0.8078.4, with its Linux executable under `chrome-linux64/chrome`.

Sources: [Microsoft registry manifest](https://mcr.microsoft.com/v2/playwright/manifests/v1.64.1-noble),
[upstream browser pin](https://raw.githubusercontent.com/microsoft/playwright/v1.64.1/packages/playwright-core/browsers.json),
[upstream Dockerfile](https://raw.githubusercontent.com/microsoft/playwright/v1.64.1/utils/docker/Dockerfile.noble).

The candidate supplies the adapter's fixed Node and Chromium paths. It copies only the worker, verifier and pin
files; `.dockerignore` excludes everything else. It performs no apt, npm or browser downloads in build steps.
Those steps have networking disabled. The immutable base can still be downloaded when absent, after explicit
build approval. Inherited browser directories are changed to root ownership without group/other write access.
The runtime adapter retains its read-only root, unprivileged Chromium child, no network, fixed runsc profile,
quotas and separate owner itinerary approval.

The build verifier checks the expected platform, executable aliases, versions, ownership and non-owner write
permissions. It records SHA-256 digests for Node, Chromium and the copied worker/pin files in
`/opt/hirezero/browser/image-inventory.json`. It executes version switches only, never a browser itinerary.
The inventory explicitly records `qualified: false`; it cannot replace a runtime qualification receipt.
Unexpected image contents fail the build instead of selecting another browser or installing missing packages.

## Qualification boundary

This is a build candidate, not a production-ready browser recommendation. Upstream describes its image as
intended for testing/development and warns about untrusted browsing; it also documents separate-user and
sandbox requirements for crawling. The wrapper, immutable digest and file inventory do not prove containment.
See [upstream guidance](https://playwright.dev/docs/docker).

The base retains its other browser binaries and dependencies. This first candidate prioritizes a reproducible
qualification target; it is not a minimal image or a completed distribution-license/security audit. A future
optimized or updated image is a new reviewed pin and must be qualified again. No image is built or pulled by
routine checks, and no engine, WSL service or runtime is installed or started automatically.

On an owner-approved Linux runsc host, build the candidate, inspect its inventory and run `BrowserRuntimeTests`
against the resulting immutable image ID. The existing suite must prove the real broker/worker/Chromium path,
deadline, cancellation, log-only recovery and cleanup. If Chromium's own sandbox or the fixed runsc envelope
is incompatible, qualification must fail; do not remove the sandbox or weaken the envelope to manufacture a pass.
Operator review and explicit activation remain separate steps. No completed image build or runtime qualification
is claimed by this decision. See the [preparation record](../history/2026-10-26-browser-image-build-candidate.md)
for the exact evidence and remaining checks.
