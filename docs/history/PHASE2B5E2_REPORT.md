# Phase 2B.5e2 report: read-only session steering

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

You can now steer a coding-agent session from the company workspace, as you decided:

- **Read-only:** no edits, no network.
- **Your installed app,** at an explicit command path.
- **No message text** is read from sessions.
- **No new sessions** are started.

Unlike the legacy one-click send, every message is drafted, reviewed and approved, then sent once.

The legacy steering code has also moved out of Core.

## How it works

1. **Enable steering** in `agent-apps.json` in the company data root, naming each app's full command path:

   ```json
   { "apps": ["claude"], "steer": { "claude": "<full path to claude.exe>" } }
   ```

   An enabled app with no command path is listed but cannot be steered. The Integrations card says which apps can
   be steered.
2. **List agent sessions**, and approve the listing.
3. **Session steer** (measured): drafts `{sessionId, message}` for a session in that approved listing. You can also
   write the draft yourself.
4. **Approve the draft, then Propose publication.** The Developer refuses a session that isn't in the approved
   listing on that task, or a message with a credential-like value. The publication has no local calls and carries
   the exact send, so approving the publication approves that one message to that one session ([ADR 0016](../adr/0016-publishing-one-approved-call.md)).
5. **Publish once:**
   1. A read-only check runs first: the session is still listed, its folder exists, it is idle and quiet for a
      minute, and no HireZero turn is running. If not, nothing starts and the publication stays approved, so you can
      try again later.
   2. The message is sent once through `agentapps.session.send`, which repeats every check.
   3. The turn runs read-only: Claude in its default permission mode, where anything needing approval is refused,
      or a read-only Codex turn with no network.
   4. Follow the session in its own app. A send that isn't confirmed is unknown and is never retried.

## What changed

- **Contracts:** `agentapps.session.check` (Read) and `agentapps.session.send` (External). The send's input is
  only a session id and a message, so nothing in it can ask for edits or network.
- **Integration:**
  - The provider gains read-only steering for apps with a named command path.
  - Every check runs again inside the send, and only one HireZero turn runs per session.
  - It reuses the legacy Claude and Codex drivers, always with edits and network off, and never starts sessions.
- **Developer:**
  - the `session-steer` measured skill, which publishes;
  - `CompanySteerPublication` (propose, then a readiness check, then claim, bind, send once and finish);
  - the two tools in the manifest.
- **Out of Core:**
  - `SteerRequest`, `ISessionDriver`, `SessionSteering`, the legacy steering endpoints and their request types now
    live in `HireZero.Integrations.AgentApps` (`LegacySteering.cs`). The legacy Developer profile maps them with
    `MapLegacySessionSteering()`.
  - The session watcher settles turns through a neutral `ISessionReconciler`.
  - The architecture test now also bans those type names from Core.
  - The steering checks moved to the integration tests.

## Tests

- `AgentAppsSteeringTests` (6):
  - steering tools exist only when you name the command;
  - a ready session gets exactly one message, sent read-only, and a second is refused while that turn runs;
  - working, waiting-for-approval and recently active sessions are left alone (3 cases);
  - unlisted sessions, other apps, missing folders and oversized messages are refused.
  - **Mutation check:** forcing edits on fails the read-only test.
- `SessionSteerTests` (4):
  - an approved message is sent once, audited under the publication, and a second publish is refused;
  - unlisted sessions, messages with credentials and the listing itself can't be proposed;
  - a busy session leaves the publication approved, and it publishes once the session is ready;
  - an unconfirmed send is unknown, survives a restart, and is never retried.
- `SteeringChecks` (moved): legacy steering behaves as before.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 224 passed in two consecutive runs |
| CSharpier, web typecheck and format check; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** a real steer. It needs `agent-apps.json`, your installed app and an idle session, and it makes
that app run a turn on your account. The Codex driver is the legacy one: it resumes the thread through HireZero's
own Codex home, which may not see threads that exist only in the Codex desktop app's home. Steering Claude Code is
the more direct path.

## Still legacy until 2B.5j

Session listing state for the legacy profile stays: the session store, hooks and watcher, and the legacy Sessions
panel.

## Next

2B.5f: Azure DevOps as an optional integration. Also still open: live coding step 2 and 2B.5d2 (mail replies, which
can now reuse the one-approved-call publication).
