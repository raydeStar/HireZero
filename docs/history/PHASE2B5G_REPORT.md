# Phase 2B.5g report: Jev as an optional integration

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Jev is your external classifier service: cheap structured choices through an API key. In the legacy profile it
sorted every inbound mail into act, review or ignore, routed GitHub chat and triaged local requests, all
automatically. The company workspace has none of those pipelines, so this port gives Jev one owner-started use, and
takes its name out of Core.

## How it works

- **Status.** Jev is active when `HIREZERO_JEV_API_KEY` is set, as before. The Integrations card says whether it
  is. The key stays in the environment, not in a data-root file.
- **Sort recent messages with Jev** (the Developer's `jev-sort` action, choosing 1 to 72 hours and mail, Teams or
  both):
  - reads up to ten recent Microsoft 365 messages;
  - sends each as a short excerpt (sender, subject and the first 700 characters) to Jev for act, review or ignore,
    exactly the legacy intake's question;
  - counts answers under 70% confidence as review;
  - turns the result into evidence for your review.

  It runs only when you start it, refuses before reading anything if Jev isn't configured, and is capped at ten
  messages, because the excerpts leave this computer and Jev may bill per request.

## What changed

- **Integration:** `JevToolProvider.Company()` uses HTTP clients private to Jev, so the company profile still has no
  general client factory, and reports its status. The host registers it only when the key is set.
- **Developer:** the `jev-sort` action. The manifest already declared the Jev tools.
- **Out of Core:** the legacy intake's classifier tier no longer names Jev. Each classifier names itself, and Jev's
  name, "jev", is what the legacy pipeline records as decided-by, as before. The architecture test now also bans
  Jev names from Core.

## Tests

- `Microsoft365CompanyTests.JevSortsAtMostTenMessagesIntoReviewedEvidence`:
  - refused with nothing read and nothing sent while Jev isn't configured;
  - once configured, it reads ten messages, sends exactly ten excerpts and keeps the newest ten;
  - a 40%-confidence "act" is shown as review.
- `JevToolProviderTests`: the status reflects the key, including refusing a key with a line break.
- The legacy intake checks pass with the classifier naming itself.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 232 passed in two consecutive runs |
| CSharpier and web format checks; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** a real Jev request. It needs your key, and it sends message excerpts to Jev.

## Choices made (each reversible)

- **One consumer.** Jev's company use is the owner-started message sort that mirrors the legacy intake. GitHub chat
  routing and local request triage have no company equivalent yet, and stay legacy.
- **Ten messages at most** per sort, because of the egress and per-request billing.
- **The key stays in the environment variable.**

## Next

2B.5h, tailnet multi-user access, then 2B.5i, the daily brief.
