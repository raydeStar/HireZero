# Phase 2B.5d1 report: Microsoft 365 intake as an optional integration

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Microsoft 365 intake now works in the company workspace, as ADR 0009 describes: optional, configured in the data
root, and visible to you as active or not, with the reason. It is a faithful read-only port. The legacy integration
only ever read mail and Teams, and so does this. Sending replies would be new; it is split out as 2B.5d2 and waits
for your decision.

## How it works

1. **Setup** (company overview, **Integrations**):
   - Register `http://127.0.0.1:5065/oauth/microsoft/callback` as a desktop redirect URI on your own Entra app
     registration.
   - Paste its client ID, choose Mail and/or Teams, and sign in. The company workspace needs its own sign-in; legacy
     tokens are not copied.
   - The tools register as soon as a client ID is saved; no restart is needed.
2. **Read recent messages** (the Developer's `m365-read` action): choose 1 to 72 hours and mail, Teams or both. It
   reads at most 25 messages per source as untrusted text, and the result becomes an artifact for your review.
   - The content guard labels instruction-like text.
   - The read never moves the legacy sync cursor.
   - Nothing is polled in the background.
3. **Inbox triage** (measured): over the approved read, it gives each message a category, priority and next step,
   flags phishing or instruction attempts, and lists what to handle first. It cannot reply, send, mark or move
   anything.
4. **Integrations status:** the overview lists Microsoft 365, the synthetic Wiki, Git and live coding, each with
   whether it is active and why not.

## What changed

- **Core:** a role-neutral `IntegrationStatus` record and an `IIntegrationStatusSource` interface. No Microsoft
  names: the two comments that named Outlook and Teams are now neutral.
- **`HireZero.Tools.Microsoft365`** (contracts only): `m365.status`, `m365.mail.read` and `m365.teams.read`, all
  Read.
- **Integration:**
  - A tool provider whose reads are refused, with the reason, until the integration is configured and signed in.
  - Bounds of 1 to 72 hours and at most 50 messages.
  - Owner-started reads that use plain queries instead of the delta cursor.
  - The sign-in redirect and return address are per workspace; the legacy defaults are unchanged.
  - The company services have no poller and use HTTP clients private to the connector, so the company profile still
    offers no general HTTP client factory. An existing check enforces that.
- **Host:** maps the setup endpoints, registers the tools once configured, and reports `integrations` in the
  workspace's capabilities.
- **Developer:** the `m365-read` action, the `inbox-triage` skill and the three Read tools in the manifest.
- **Web:** the Integrations card, with status and Microsoft 365 setup.
- **Architecture test:** Core may not name Microsoft 365 products. Later phases add Azure DevOps and tailnet names
  as those move.

## Still legacy until 2B.5j

The legacy intake pipeline (`Intake.cs`, rules, the `intake_signals` table and `/api/intake*`), its poller and the
legacy Inbox UI keep serving the Developer profile until it retires. The pipeline's notification sender list still
includes Microsoft domain patterns; it goes with the pipeline.

## Tests

- `Microsoft365ToolProviderTests` (3):
  - the status explains each missing step, and reads are refused before any Graph call;
  - an owner-started read uses the plain query with the right window and limit, and leaves the sync cursor and last
    sync untouched;
  - bounds and the Mail switch are enforced.
- `Microsoft365CompanyTests` (6):
  - a read becomes reviewed evidence, with the injection label and Read-only audit, and triage needs it approved;
  - read input is bounded (4 cases);
  - the host reports Microsoft 365 as not active with its reason, offers the company redirect, and registers no
    tools until configured.
- `ArchitectureTests.CoreNamesNoIntegrationProduct`: a planted "Outlook" in Core fails it.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 209 passed in two consecutive runs |
| CSharpier, web typecheck, format check and build; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** a real Microsoft sign-in or Graph read. It needs your Entra app with the new redirect URI, and
your account.

## Found along the way

- **The Wiki integration breaks the same rule.** With `HIREZERO_SYNTHETIC_WIKI=1`, it registers a general HTTP
  client factory in the company profile, which the profile otherwise forbids. No test covers that combination. A
  follow-up should give the Wiki private clients, as Microsoft 365 now has.
- **A lock can hide a test failure.** When a check fails before a company host starts, the host's data-root lock is
  never released, and the test reports a file-lock error instead of the failed check.

## Decisions for you

1. **2B.5d2, replying to a message.** It needs the `Mail.Send` permission, which the legacy integration never
   requested, and a small Core change: a publication with no local call, whose one send you approve in advance.
2. **Role.** Intake sits on the Developer for now, as in the legacy profile. It could move to a future assistant
   employee.
3. **Redirect URI.** Add the 127.0.0.1:5065 redirect URI to your Entra app before signing in.
