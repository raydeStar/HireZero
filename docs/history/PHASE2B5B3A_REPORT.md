# Phase 2B.5b3a report: execution limits you approve

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Before a live coding runner can run real fixes, the limits it runs under have to be yours to approve rather than
fixed in code. This phase adds that mechanism. Defaults are unchanged (the old pilot values), so behaviour is the
same until you choose new ones.

The live runner itself (2B.5b3b) waits for your decisions below.

## Decision

[ADR 0013](../adr/0013-execution-limits-approved-with-the-grant.md): execution limits are approved with the grant.

## What changed

- **Limits travel with the grant** (`CompanyExecutionLimits`: tokens, seconds and effort):
  - They come from `CompanyStore.ExecutionDefaults`, which is still 5,000 tokens, 20 seconds and low effort.
  - They are part of the proposal's digest, so you approve exact limits.
  - Anything above 1,000,000 tokens or 3,600 seconds, or any effort other than low, medium or high, is refused.
  - The panel shows them before you approve.
- **Approving sets this work's allowance.** Until now, each task's root budget was fixed at 5,000 tokens with no way
  to change it, so no execution could ever use more. Approving a grant now raises the task's allowance to cover its
  token limit. Company, department and employee budgets still cap the run; a run they can't cover is not sent.
- **The reservation matches the limit.** Dispatch reserves exactly the approved tokens, and settlement accepts usage
  up to that reservation instead of a fixed 5,000.
- **A run keeps its approved time.** Fixed: a run approved late in its 10-minute proposal window was cancelled when
  the proposal expired. A dispatched run now stays current for its approved seconds plus a minute from dispatch.
- **Developer bridge:** uses the grant's deadline, token limit and effort instead of the 20 seconds, 5,000 tokens
  and "low" written in code.
- **Rename:** `ICompanyCodingFactory.FixtureQualified` is now `Qualified`. A live runner will qualify the same way
  the fixture does.
- **Docs:** the dated phase reports moved into `docs/history/`, as AGENTS.md asks, with links updated (a separate
  commit).

## Tests

New in `PullRequestFixTests` (4; 11 in total):

- **Approval and reservation:** the grant carries the approved limits; a run settles within its 8,000-token
  reservation and releases it.
- **Budget:** a run whose limit exceeds the employee budget (30,000 over 25,000) is not sent; no run is started and
  no file is written.
- **Ceiling:** limits above it, or an unknown effort, are refused.
- **Run time:** a run dispatched at minute 9 of its proposal is still current at minute 20, settles 7,000 tokens
  within its reservation, and stops being current after its approved 15 minutes.

`execution-steps.test.mjs` also checks that the limits are shown.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 173 passed in two consecutive runs (169 before, plus 4 new) |
| CSharpier, web typecheck, format check and build | passed |
| Node tests | 12 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**One intermittent failure.** `employee-review-browser-smoke.mjs` failed twice: once in the full run, once in three
reruns right after it. It then passed 14 times in a row, and I couldn't capture the failing output. It covers
measured reviews, not the execution code changed here. I haven't shown whether it was intermittent before this
change.

## Decisions for you, before the live runner (2B.5b3b)

1. **Limits.**
   - Default token, time and effort limits for a fix run. My proposal: 200,000 tokens, 15 minutes and medium effort.
   - Whether to raise the server ceilings on the employee, department and company budgets (25,000, 50,000 and
     100,000 tokens). With the current ceilings, a 200,000-token run can never be sent.
2. **Read scope.** The sandbox can let the run read only the checkout, which may break `dotnet` or `npm` test runs,
   or also named toolchain folders, read-only.
3. **Network.** Off is proposed. A fix that needs a package restore then fails unless the checkout is already
   restored.
4. **Sign-in.** Coding uses the dedicated signed-in Codex home that measured replies use. While a run holds it,
   measured replies wait.
5. **Pin.** Commit a Codex runtime pin under `runtime/codex` (version and bundle digest), as OpenClaw has, and refuse
   any other binary. This would be ADR 0014.
6. **Soft stop.** Usage is only a soft stop: the runtime has no hard upstream cap. The runner cancels when reported
   usage reaches the limit, but a turn can overshoot.

## Next

2B.5b3b, once those are decided:

- a `CompanyCodexCodingFactory` qualified only for the pinned runtime;
- the MXC sandbox scoped to the checkout;
- the same preflight as measured replies (account, model catalog, sandbox profile, no ambient instructions);
- per-update usage tripwire;
- a `--company-coding` host flag that is off by default;
- FakeCodex modes for every refusal.

Live checks each need your approval: a sandbox probe without inference, one coding turn on a fictional repository,
then a real fix on a branch of yours.
