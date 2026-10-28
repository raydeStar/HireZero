# ADR 0155: Keep the reviewed Codex runtime outside desktop update directories

Date: 2026-10-28

Status: Accepted for isolated text preflight; real model delivery and coding qualification remain separate

## Context

The desktop updater removed the package directory referenced by both local launchers. The application remained reachable, but native work could not start. A configured path into an externally managed package is not durable runtime storage.

## Decision

- Pin the official `codex-cli 0.165.0-alpha.1.2` executable bundled with desktop version `26.1023.6123.0`. Record its exact SHA-256 in `runtime/codex/pin.json`; no dependency versions or lockfiles change.
- Install a verified copy in an application-owned, versioned runtime directory. Refuse conflicting contents and reparse points. Never replace its bytes in place or silently choose the latest desktop package.
- Update the explicitly configured executable path and bundle identity only after local backup and idle checks. Retain existing account homes, model choices and application records.
- Keep existing approval bindings exact. A runtime change does not transfer an old approval or replay an uncertain request. Recurring work needs review against the new binding before execution.
- Coding, browser, script and automatic model execution remain disabled. This text-runtime recovery does not qualify those execution types or change sandbox access.

## Evidence and limits

The official package binary and a standalone copy both passed read-only native inspection with the existing dedicated sign-in: Luna is present, included usage is permitted, and an ephemeral thread has the exact permission profile, no ambient instruction sources, read-only access, no network, and no provider fallback. Neither inspection sent `turn/start` or performed inference. The copy passed without neighboring package binaries.

The CLI-generated schema with `--experimental` retains the adapter's turn-start, usage and completion fields, matching the client's `experimentalApi` capability. The default public schema omits some fields this client uses. These schemas are version-specific, as documented in [Codex App Server](https://learn.chatgpt.com/docs/app-server#message-schema). Schema agreement and preflight are not proof of successful inference, actual usage accounting or live coding isolation.

Keep a compact local receipt for copied hashes, native inspection, backup and installation. Private account and pilot details stay outside source control.
