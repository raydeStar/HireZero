# Phase 2B.5b3b report: a live coding runner on a pinned Codex

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Company execution can now run a real Codex coding turn instead of only the fixture. It is built with your choices
from 2B.5b3a and is off unless you start the host with `-Coding`. Everything is verified offline with FakeCodex.
Nothing live has run: no sandbox probe, no model turn, no push.

## Decision

[ADR 0014](../adr/0014-pinned-codex-runtime.md): live company coding runs on a pinned Codex runtime.

## Your choices, as built

| Choice | Built as |
| --- | --- |
| 200,000 tokens, 15 minutes, medium effort | `CompanyExecutionLimits.Default`, proposed with every grant; you still approve each grant's exact limits |
| Raise the budget ceilings to fit | Company 1,000,000, department 500,000, employee 250,000 tokens (ceilings and defaults); CEO plan budgets unchanged |
| Checkout plus named toolchain folders, read-only | The checkout and the run's scratch are writable; folders you pass with `-CodingRead` are read-only and must be specific folders outside your profile root, the sign-in home and the scratch |
| Network off | `network={enabled=false}` in the profile; the run request and the thread must both confirm it |
| Dedicated sign-in home | The same `-CodexHome` as measured replies; runs and replies share one gate |
| Commit a Codex pin | `runtime/codex/pin.json`: `codex-cli 0.163.0-alpha.10.2`, executable SHA-256 `F6DBF48C…0312` |
| Usage as a soft stop | Usage is recorded on every update, and the run stops when it reaches the limit; a turn can overshoot |

**About the pin.** The binary from the earlier qualification (0.162.0-alpha.17.2) is no longer installed: the
desktop app has updated itself since. I pinned the newest bundled `codex.exe`, from desktop app 26.1013.4894.0
(`app/resources/codex.exe`). The earlier 26.1013.3656.0 build reports the same version but has a different hash.
The desktop app will keep updating; each update will be refused until the pin is changed.

## What changed

- **`CodexAppServer`.** The connection's sandboxed app-server launcher, extracted so measured replies and coding
  share it. It takes a parameterized permissions profile and an exact permissions check. Measured replies behave as
  before; their tests pass unchanged.
- **`CompanyCodexCodingRunner`.** A preflight runs before any turn; any doubt refuses with "not sent". Then the turn
  runs with:
  - only messages, reasoning, commands and file changes allowed;
  - the usage tripwire;
  - a stop on approval requests, exhausted usage, a changed account mode or a changed credit balance;
  - the turn interrupted and the process tree stopped on any failure;
  - the scratch folder removed afterwards.
- **`CompanyCodexCodingFactory`.** Qualified only for the pinned binary with its reviewed identity, a sign-in home
  and safe read-only folders. It refuses a checkout that overlaps them.
- **Host and launcher.** `--company-coding` (`-Coding -CodingRead <folders>`) is valid only with `--company-codex`.
  The host refuses to start if live coding isn't qualified, and shows `liveCoding` in its capabilities.
- **Defaults and budgets** as in the table above.

To try it, once you approve the live steps below:

```powershell
tools/Start-CompanyLocal.ps1 -DataRoot <new folder> -Codex -CodexExecutable <pinned codex.exe> -CodexIdentity <identity> -CodexHome <dedicated home> -Coding -CodingRead 'C:\Program Files\dotnet','C:\Program Files\Git'
```

## Tests

- `LiveCodingTests` (13):
  - **A run:** writes only its checkout under a profile with exactly checkout write, scratch write and toolchain
    read. It reports usage, the model and the result at the approved effort, and removes its scratch folder.
  - **Preflight refusals send no turn:** an API-key sign-in, exhausted usage, a missing model, a broader effective
    profile, or ambient instructions.
  - **A changed grant or request** is refused before a turn: the grant no longer current, network, a different
    effort or model, another folder, or an environment.
  - **Usage tripwire:** a run whose usage passes the limit is stopped and interrupted.
  - **Run failures:** an out-of-scope tool item, an approval request or a credit change fails the run.
  - **Qualification:** only the pinned binary with its identity qualifies, and read-only folders that are a drive
    root, your profile's parent, a folder holding the sign-in home, or missing are refused.
  - **The host** refuses `--company-coding` without `--company-codex`.
- `PullRequestFixTests` (1 more): the live runner drives the whole reviewed path, with default limits, the diff
  review, the artifact and a publication proposal.
- **Mutation checks.** Disabling the tripwire, or the ambient-instructions check, fails the matching tests.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 187 passed in two consecutive runs (173 before, plus 14 new) |
| CSharpier, web typecheck, format check and build; Node tests | passed; 12 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

## Needs your approval: live qualification, in order

Each step uses your Codex sign-in, and steps 2 and 3 use included usage. AGENTS.md keeps them out of routine
checks, so each needs your explicit go-ahead.

1. **Sandbox probe, no model.** `command/exec` under `hirezero_execution` on the pinned 0.163 binary. A write inside
   the checkout must succeed; a write beside it and a network call must fail; reads must match the named folders.
2. **One coding turn** on a fictional temporary repository, with low limits. It confirms the thread's reported
   sandbox shape for a write profile, the item types 0.163 actually emits, usage reporting, termination and the
   diff review. Anything unexpected is refused rather than accepted, so this is where a protocol difference would
   show.
3. **A real `pr-fix`** on a branch of yours. Its push is a separate approval in the app.

## Next

After qualification, the fix loop: re-gathering evidence after a push, and another fix when checks fail. Then 2B.5c
(wiki drafts, request preparation, Trusted Postgres).
