# Phase 2B.5j report: one runtime

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).
Decision: [ADR 0019](../adr/0019-retiring-the-legacy-profiles.md).

HireZero ran two runtimes side by side: the company workspace, and the legacy developer, recovery and held profiles
with their own role catalog, model gateway, usage meter, stores, endpoints and web shell. 2B.5j leaves one.

## Steps

1. **The company path stands apart.**
   - The coding usage event name, the hold switch and the web API client moved to neutral homes.
   - An architecture test keeps company code from naming any retired piece.
2. **Modules version their own storage** ([ADR 0018](../adr/0018-module-storage-migrations.md)).
   - `ModuleStorage.Migrate` applies an owner's checksummed steps on the company schema's rules.
   - Coding runs moved onto it first.
3. **Ported to the company workspace:**
   - the budget panel;
   - GitHub sign-in, through the GitHub CLI's browser flow;
   - the review queue (the `pr-queue` action);
   - owner tasks (priority and snooze on Today);
   - the work log (the day's events on Today);
   - Trusted PostgreSQL setup (the `postgres-setup` publishing action: write the runner, install the exact pin).
4. **Retired:**
   - the legacy profiles and launchers;
   - `IRole`, `roles.json`, `IModelGateway`, `UsageMeter` and `BudgetLedger`;
   - the legacy Core stores and endpoints, and the legacy Developer services and panels;
   - the OpenClaw gateway and its pin, the Tailscale integration and the session-hook script;
   - `web/src/shell`, and their tests.

   The agent-session vocabulary moved from Core into the AgentApps integration.

## Owner decisions

- **Dropped:** session hooks, shift proposals, the backlog and rule-based mail intake.
- **Deferred:**
  - long-term memory;
  - Trusted PostgreSQL inside live coding runs;
  - tailnet multi-user access (Phase 3).

## Checks

- 150 .NET tests pass. The removed legacy tests went with their code.
- 11 frontend tests pass.
- The company launcher test passes.
- The company headless browser smoke passes.
- The web build, its type check and both format checks are clean.
- The company schema's first step is unchanged, so existing company databases open as before.
