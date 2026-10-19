# 0004: Agent runtimes are pinned and verified

Status: Accepted

## Context

HireZero drives external agent runtimes (OpenClaw for text, Codex for coding). The source previously assumed a
runtime the owner installed by hand under `<data root>/Runtime` and never recorded which version was expected,
so a qualified behaviour could silently change after an upgrade.

## Decision

- `runtime/openclaw/package.json` pins the exact OpenClaw version and its supported Node range; the committed
  `package-lock.json` pins the whole dependency tree (resolved only from the public npm registry).
- `tools/Install-OpenClawRuntime.ps1` installs that locked tree into `<data root>/Runtime/openclaw` with dependency
  lifecycle scripts disabled, runs only OpenClaw's own bundled-plugin setup under a Node that satisfies the range,
  verifies the installed version and writes a receipt (`hirezero-runtime.json`).
- The OpenClaw integration embeds the same `package.json`. Before starting any OpenClaw process the host compares
  the installed package version with the pin and refuses a mismatch with a message naming both versions.
  `tools/OpenClawSignIn.ps1` performs the same check.
- Upgrading is a reviewed change: edit the pin, regenerate the lockfile
  (`npm install --package-lock-only --ignore-scripts` in `runtime/openclaw`), run the offline checks and, where a
  live pilot is required, an owner-approved qualification.

The first pin is OpenClaw 2026.10.4 (published 2026-10-15), which requires Node 24.16+ (or 26.1+).

## Consequences

Runtime upgrades become explicit and reviewable. Phase 2 moves runtimes behind the single model provider so a
pinned runtime is one provider among several.
