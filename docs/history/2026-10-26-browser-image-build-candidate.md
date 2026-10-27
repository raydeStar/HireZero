# Browser image build candidate — 2026-10-26

This turn found and filled a delivery gap in the advanced browser path: the worker and qualification suite
existed, but no reproducible image recipe or browser distribution pin was supplied. The repository now contains
a Linux amd64 build candidate under [ADR 0100](../adr/0100-browser-image-build-candidate.md).

## Prepared artifacts

- `runtime/browser/pin.json` records an immutable platform-specific Microsoft Playwright image digest, its
  upstream manifest/configuration digests, the selected Chromium revision/version/path and Node major. The image
  digest pins all base contents; actual executable versions and hashes are recorded during a future build.
- `Dockerfile` supplies the adapter's fixed executable paths, root-owned protected worker/browser files, no
  image-declared volume and network-disabled build steps without floating package installation.
- `.dockerignore` limits context to the exact runtime files. Application data, credentials and the rest of the
  checkout cannot enter this build through an unrestricted copy.
- `verify-image.mjs` checks platform, versions, executable aliases, protected paths and file hashes. Its generated
  inventory explicitly has `qualified: false`. It cannot execute from an ordinary host checkout or grant runtime
  qualification. The procedure documents the existing real qualification suite and separate activation gate.

## Verified evidence

- Public registry index, selected Linux amd64 manifest and config were fetched. The selected manifest and config
  bytes matched their SHA-256 descriptors. The config declares no volumes; compressed base layers total
  983,886,837 bytes. Upstream release source identifies Chromium 156.0.8078.4 at revision 1248 and Node major 24.
  These sources are linked in ADR 0100 and the pin.
- **Two offline Node checks passed**: the extended browser-runner/image contract check and the existing protocol
  check. They cover version/platform/path/hash mismatch, protected-file refusal, pin/recipe consistency, context
  allowlisting, host-entrypoint refusal and rejection of inventories claiming qualification. No browser image
  was executed by these checks.
- JavaScript syntax, changed-file formatting and whitespace checks passed. Disk space exceeded 10 GiB.
- Read-only local preflight found Docker CLI/Desktop installed with a valid Docker Inc launcher signature, but
  the local Linux-engine pipe absent. Docker Desktop, WSL and containers were not started. No layer was pulled.

## Open qualification

The image has **not been built**, its executable inventory has **not been generated**, and the real runtime has
**not been qualified**. Source/metadata verification cannot prove Dockerfile execution, Chromium sandbox
compatibility, runsc containment or real-site usefulness. The upstream base is a testing/development image with
extra browsers and dependencies; production/distribution review remains necessary.

The next step requires an approved Linux/runsc host and explicit image-build/runtime qualification approval.
The current runtime envelope must remain intact if qualification fails. Application activation, production
deployment, billing and customer validation remain separate. No SDK, native Codex or script-sandbox pin was
changed, and no live model, payment or external publication occurred. The three-phase goal remains active.

Offline proof: `test-results/browser-image-contract.log` and `browser-image-offline.log`.
