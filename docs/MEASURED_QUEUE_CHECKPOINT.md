# Measured employee queue checkpoint

Local implementation on 2026-10-15, based on `bfba023`. No new native inference, login, spending, push, account/security change, automatic dispatch or real Developer repository execution occurred. The existing read-only preview and its expiry were not modified.

The default company profile remains unavailable for model execution. Selecting the pinned native connection exposes preparation and explicit owner authorization for one eligible internal task. The native adapter still reports `Qualified=false`; its strict counter/completion methods remain unavailable. Marketing alone opts its draft skill into measured admission. Core remains role-neutral and integrations contain no role behavior.

## Delivered contract

`measured.prepare` atomically claims a fresh eligible task and captures its exact context without starting a native process. Its proposal binds task ID/version, employee authority, context/instruction digests, runtime identity, recorded route, explicit existing-credit fallback allowance, ten-minute expiry and `measured-oauth` policy. The full application instructions and serialized context must fit 4 KiB UTF-8. Plans, meetings, repository tasks and tasks with previous attempts are excluded.

The owner reads the sealed packet and explicitly accepts the soft limits. `measured.run` then consumes a single durable authorization and reserves 8,000 estimated tokens against task, employee, department and company ledgers. The unused task root increases only as part of that exact approval. The 8,000 reserve comprises planning allowances of 4,500 baseline input, 2,500 packet/headroom and 1,000 output; none is a provider-enforced bound or exact preflight count. Strict 4,000/1,000/5,000 admission remains unchanged.

One owned native process/thread and at most one application `turn/start` are permitted per attempt. Account serialization remains held during completion and cancellation drain. There is no application retry, fallback or automatic resumption. A durable consumed receipt is available while the HTTP response is outstanding; repeated operations return that receipt without another native request. Restart leaves consumed attempts uncertain/held. An unexpired unsent proposal still requires a fresh explicit owner run command after restart.

Pinned route/identity checks, ephemeral state, zero ambient instruction sources, verified process-local MXC scratch-only reads, no tools/retrieval/network and no broader backend fallback remain in place. One native turn can contain internal native retries; application request count does not prove one billable HTTP attempt or a maximum credit charge.

The local 60-second deadline, 2,000 visible-character stop, 1,000 observed-output target and 8,000 observed-total tripwire withhold/stop locally after detection and can be exceeded upstream. A clean complete result persists full provider input/output/reasoning usage and one internal artifact requiring exact owner content review. Reasoning is included in output; an absent sub-breakdown stays unknown. Runtime input and cached input are not subtracted from usage.

Cancellation and anomalies never publish content. Complete consistent usage settles once even when content is rejected. Known overage remains explicit with the reservation held; inconsistent components/totals remain visible without settlement. Interrupted terminals have no verified final-usage marker, so their usage remains partial and reserved, including updates queued before interrupt acknowledgement. A confirmed ordinary completion received late can settle without publishing content or clearing a reconciliation hold. Missing usage, quota/credit changes, tool requests, invalid receipts, tripwires and unconfirmed termination block future dispatch across restart. Process cleanup failure preserves known usage while withholding the result.

The old 150-credit number is only the historical qualification planning buffer. It is neither a minimum Marketing job cost nor a proven maximum charge. Measured jobs require an explicitly chosen fallback allowance; this interface cannot enforce a per-job credit ceiling. No purchase, recharge, API-key fallback or account-spending configuration is added.

## Verification

Independent code review identified deadline/grace publication, cleanup receipt loss and missing task-version binding. Those issues were fixed and the final review reported no remaining blocking findings. It also prompted conservative interrupted-usage handling and its queued-partial regression.

| Gate | Result |
| --- | --- |
| Full solution and fake-native build | PASS, exit 0 |
| Frontend TypeScript/Vite production build | PASS; existing large-bundle warning |
| Company checks, including real HTTP measured queue | PASS 283, exit 0 and fixture cleanup complete |
| Core checks | PASS 420 |
| Developer checks with fictional local fixtures | PASS 642 |
| Host checks | PASS 87 (44 held + 22 discovery + 21 recovery) |
| Integration checks | PASS 54 |
| Policy checks | PASS 44 |
| Node/UI checks | PASS five test files, including measured packet review/acknowledgement |
| Launcher checks | PASS 14 with fake HTTP and no route/process changes |
| Staged whitespace and focused private-string diff scan | PASS |

An initial Company run passed every assertion but failed fixture cleanup because the new task-version test used SQLite's default connection pooling. Setting `Pooling=False` for that fixture fixed the handle retention; the rerun exited successfully and cleaned its root. The confirmed leftover from the failed run was removed after its test process exited.

Reproduce the build without invoking the mismatched global SDK selection: set `MSBuildSDKsPath` to the installed SDK's `Sdks` directory, run `dotnet exec <installed-sdk>/MSBuild.dll HireZero.slnx -nologo -verbosity:minimal -p:UseSharedCompilation=false`, and build `tests/HireZero.FakeCodex/HireZero.FakeCodex.csproj` the same way. Execute each of the six resulting test DLLs under `tests/HireZero.{Company,Core,Developer,Host,Integrations,Policy}.Tests/bin/Debug/net10.0/`. Run `npm run build --prefix web`, `node --test tests/approval-ui.test.mjs tests/company-ui.test.mjs tests/measured-ui.test.mjs tests/execution-feedback.test.mjs tests/session-hook.test.mjs`, and `./tests/launcher.test.ps1`.

Commands use the installed .NET SDK 10.0.203 through direct MSBuild with `UseSharedCompilation=false`, existing restored packages, and no restore/network/model calls. The frontend is built before the solution to avoid asset-scanning races. More than 100 GiB free was checked before builds; disposable fixture roots are cleaned by the test harness after owned processes exit. Compact ignored logs remain in `test-results/measured-*.log`.

## Exact proposed fictional brief and remaining approval

The proposed brief is [ONE_JOB_MARKETING_BRIEF.json](ONE_JOB_MARKETING_BRIEF.json), also used by the real-HTTP fake-native queue checks:

```json
{
  "objective": "Introduce a fictional paper crane kit for internal review",
  "audience": "Adult paper craft hobbyists",
  "channel": "Unpublished landing page draft",
  "deliverable": "One headline and two short sentences, followed by claim sources, missing facts and one owner review question",
  "constraints": "Fictional facts only: the kit contains paper and a printed folding guide. No price, testimonial, performance claim, publishing or outreach. Keep the complete response under 80 words."
}
```

Use one fresh company root and one fictional Marketing employee. Approved facts contain only paper and a printed folding guide, with no imported history, personal notes, handoff or repository access. The exact generated context, task version, runtime identity and digest must be prepared and reviewed before any future authorization.

Remaining approval is for **one native Luna queue request for that exact packet**, with the 8,000-token estimate, explicit acceptance that token/output/deadline/credit controls are soft, included usage first, and a newly chosen existing-credit fallback allowance. If a provider-enforced per-job credit maximum is required, this native interface cannot satisfy that requirement and must remain stopped. The prior four-call approval is exhausted. End at one review-required artifact and stop; no automatic continuation, publication, purchase, recharge, API fallback, push or Developer execution is included. No new approval is requested or consumed in this code-only checkpoint.
