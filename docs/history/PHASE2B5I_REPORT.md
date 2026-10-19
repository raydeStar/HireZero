# Phase 2B.5i report: the daily brief

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

In the legacy profile, the daily brief was a model-free digest of one day:

- what needed you, contributed by each role;
- each coding-agent session's activity;
- inbox counts and agent activity;
- optional narratives written by the same app that ran each session;
- once a day, a memory note of yesterday.

The company workspace now has the parts that fit its model, split along the three layers.

## How it works

- **Today** (Core, on the company overview). It lists what waits on you right now, from the company's own records:
  - knowledge drafts, proposed plans and meetings awaiting your decision;
  - results and gathered evidence awaiting review;
  - handoffs waiting for approval, proposed runs, and publications proposed, approved or unknown;
  - work whose outcome is unknown.

  It also counts the day's company events on this computer's clock. It uses no model and no tools, and is the same
  for every role.
- **Read the day's agent sessions** (the Developer's `session-brief` action, for today, yesterday or a day in the
  last 30). Through the AgentApps integration it reports, for each session active that day:
  - turns, changed file names, commands and failures;
  - tests, and whether the session ended on a question.

  It reads no transcript and no message text, and lists folders by name, never full path. It is available only for
  apps listed in `agent-apps.json`, and the result is evidence for your review.
- **Daily brief** (the measured `day-brief` skill). It writes the brief from that approved evidence: what needs you,
  what got done, and what is stuck. It uses only the evidence's counts and names, through the company's own model
  route. This replaces the legacy same-provider narratives.

## What changed

- **Core:** `CompanyStore.Today` and `GET /api/company/today`.
- **Tools and integration:** the `agentapps.sessions.activity` tool contract, and the AgentApps provider serving it
  from the existing Codex and Claude activity readers.
- **Developer:** the `session-brief` action and the `day-brief` skill.
- **Web:** the Today panel on the company overview.

## Not ported

- **Inbox counts.** The company workspace keeps no intake store; mail is read on request through the Microsoft 365
  actions instead.
- **The memory note of yesterday.** It needs a decision on where company memory lives.
- **The legacy brief itself.** `Brief.cs` and `/api/brief` remain until 2B.5j retires the legacy profile.

## Checks

- 248 .NET tests pass. New:
  - Today lists waiting items and counts only that day;
  - day parsing, and a day of sessions as bounded evidence;
  - the provider's activity is counts and names only, never an excerpt or a full path.
- The web format check and type check pass.
