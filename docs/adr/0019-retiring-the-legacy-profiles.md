# 0019: Retiring the legacy profiles

Status: Accepted (Phase 2B.5j). Amends [0009](0009-legacy-features-are-optional-integrations.md).

## Context

ADR 0009 kept every legacy feature, each ported as an optional integration before the legacy profiles could go. By
2B.5j the company workspace covered most of them. The rest were either ported in 2B.5j or judged by the owner not
worth porting. The developer, recovery and held profiles kept a second runtime alive: `IRole`, `roles.json`, the
model gateway, the usage meter, the legacy Core stores and endpoints, the OpenClaw gateway and `web/src/shell`.

## Decision

- **One profile.** The company workspace is the only startup profile. `HIREZERO_EXECUTION_HOLD=1` now refuses
  startup instead of serving a read-only held profile.
- **Ported in 2B.5j:**
  - the budget panel;
  - GitHub sign-in (the GitHub CLI's own browser flow);
  - the review queue (the `pr-queue` action);
  - owner tasks (priority and snooze on Today items);
  - the work log (the day's events on Today);
  - Trusted PostgreSQL setup (the `postgres-setup` publishing action).

  Request preparation was already the `request-prep` skill.
- **Dropped by the owner's decision:**
  - live session hooks (session listing and the daily brief cover them on demand);
  - shift proposals and the backlog;
  - rule-based mail intake and polling (owner-started Microsoft 365 reads replaced them).
- **Deferred:**
  - long-term memory and memory suggestions (they need a decision on where company memory lives);
  - tailnet multi-user access, sharing and people (Phase 3, with real users and roles);
  - Trusted PostgreSQL inside live coding runs (per-repository database profiles, on the live-coding track).
- **Removed:**
  - the legacy profiles and their launchers;
  - `IRole`, `roles.json` and `LegacyRole`;
  - `IModelGateway`, `UsageMeter` and `BudgetLedger`;
  - the legacy Core stores and endpoints;
  - the OpenClaw gateway and its runtime pin;
  - the session-hook script;
  - the legacy Developer services and panels;
  - `web/src/shell`;
  - their tests.

  The agent-session vocabulary moved out of Core into the AgentApps integration.

## Consequences

One runtime remains, so Phase 3 changes (tenancy, roles, audit, Postgres) touch one composition root. An
architecture test keeps company code free of the retired pieces. The removed code stays in history for the deferred
features to draw on.
