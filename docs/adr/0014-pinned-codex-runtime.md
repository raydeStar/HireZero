# 0014: Live company coding runs on a pinned Codex runtime

Status: Accepted (Phase 2B.5b3b). Extends [0004](0004-pinned-agent-runtimes.md) to Codex.

## Context

Reviewed company execution could only run the synthetic fixture runner. The legacy Developer's live coding runner
inherits the caller's environment and a persistent sign-in home with the `unelevated` Windows sandbox, and checks no
binary identity, so it is not safe to reuse. Measured text replies already run a strict, identity-checked Codex
connection; coding needs the same discipline plus write access to one checkout.

## Decision

- **Pin.** `runtime/codex/pin.json` records the exact `codex-cli` version and the SHA-256 of its executable. It is
  embedded in `HireZero.Integrations.Codex`, and any other binary is refused before a process starts. Upgrading is a
  reviewed change to the pin. The first pin is `codex-cli 0.163.0-alpha.10.2`, from the Codex desktop app
  26.1013.4894.0. The 0.162 build that earlier qualification covered is no longer installed.
- **Opt-in.** The host flag `--company-coding` (launcher `-Coding`) is off by default and valid only with
  `--company-codex`. The host refuses to start if the factory isn't qualified, which needs:
  - the pinned binary;
  - its reviewed bundle identity;
  - the dedicated sign-in home;
  - safe read-only folders.
- **Sandbox** (`hirezero_execution` profile, MXC on Windows, strict process-local configuration):
  - the run may write the approved checkout and its own scratch folder;
  - it may read the folders you name (`-CodingRead`), each a specific existing folder outside your profile root,
    the sign-in home and the runtime scratch, with no links, and not overlapping the checkout;
  - no network;
  - no MCP, plugins, web search, memories or project docs;
  - the shell tool is the only feature enabled beyond text replies;
  - the environment is cleared, and temp folders point into the scratch folder.
- **Preflight before any turn.** Any failure means "not sent", with nothing replayed. The preflight checks:
  - a ChatGPT sign-in with included usage available;
  - the coding model in the catalog;
  - the effective configuration matches the profile exactly;
  - the profile is allowed;
  - the thread is ephemeral, with zero ambient instruction sources, approval policy `never` and no network;
  - the request matches its grant;
  - the grant and checkout are still current.
- **During the run.** Only messages, reasoning, commands and file changes are accepted. Any other item, any approval
  or permission request, exhausted usage or a changed account mode stops the run.
  - Usage is recorded on every update, and the run stops when it reaches the grant's token limit. This is a soft
    stop: usage is reported after the fact, so a turn can overshoot.
  - A changed credit balance fails the run for reconciliation.
  - The bridge's deadline and outcome rules (ADR 0013) apply unchanged.
- **Defaults** (your choice, 2026-10-17): 200,000 tokens, 15 minutes and medium effort per run. The company,
  department and employee budget ceilings, which are also their defaults, are now 1,000,000, 500,000 and 250,000
  tokens.
- **Sign-in.** Coding uses the same dedicated sign-in home as measured replies. Runs are serialized on the provider
  gate, so measured replies wait while a run holds it.

## Consequences

- Offline tests play every path with FakeCodex. Live qualification still needs your approval for each step:
  1. a sandbox probe without inference (write inside the checkout succeeds, outside fails, network is denied);
  2. one coding turn on a fictional repository;
  3. a real fix on a branch of yours.
- The protocol shapes come from the 0.162 qualification. Version 0.163 may differ, for example in the sandbox shape
  a write profile reports, or in item types. Anything unexpected refuses rather than proceeds; that is what the
  live checks are for.
