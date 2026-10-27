# Browser resource adapter — 2026-10-25

Scope: continue the three-phase proposal with isolated browser-resource execution/recovery contracts and a
bounded adapter. Decision: [ADR 0077](../adr/0077-browser-resource-execution-and-recovery.md).

## Implemented

- Role-neutral browser execution, usage, creation and release receipts.
- A separate Docker/runsc browser profile with exact image/engine/installation/company/request binding,
  inspected isolation/resource settings, one dispatch, bounded duplex execution, non-replaying log recovery
  and explicit removal after output persistence. Script execution keeps its existing profile and protocol.
- Resource names include the full runtime identity. Cleanup requires the original recorded reference, so a
  replacement runtime or wrong company cannot claim an old resource was removed from its own empty resource
  list. Altered request arrays cannot expand a running packet.
- A contained PID 1 entrypoint launches a fresh unprivileged browser with private pipes, fixed executable paths,
  no host credentials and an independent deadline covering output/cleanup stalls. It refuses ordinary host use.
- Recovered host usage/wall time remain unknown; worker counters are not billing evidence. Revocation after
  worker completion still withholds captures.

## Verification

- Integration project build: zero warnings/errors, 7.26 seconds on final source.
- Final focused run: **36 passed, zero failed/skipped**, 7.160 seconds: 11 new adapter facts, 8 protocol facts,
  15 existing script sandbox facts and 2 opt-in real worker/Chrome journeys using fictional HTTP.
- Adapter cases cover exact isolation, no URLs/content in engine arguments, explicit output retention, duplicate
  dispatch, unavailable/ordinary engines, revocation before/after work, changed request arrays, lost creation/start
  acknowledgement, original-log recovery, wrong company/request/runtime, OOM/corrupt/missing logs, failed removal
  and cleanup when execution support disappears. Engine responses are fictional.
- `node tests/browser-runner.test.mjs`: host execution refused before launching Chrome or writing a result.
- `node tests/browser-protocol.test.mjs`: adverse framing and worker-channel checks passed.
- Compact evidence: `test-results/browser-adapter-build.log`, `browser-adapter-tests.log`, and the retained
  fictional process-boundary receipt `browser-worker.json` / `browser-worker-page.jpg`.

## Boundaries and next work

No Docker resource was created, no image was built/downloaded and no runtime service was changed. Production
image/pins and actual Linux/Chromium containment remain unqualified. No live page, model, account or deployment
was involved. The application does not register the new executor.

Next: qualification-based activation and durable company browser jobs with exact approval, compute allowance and
overhead, recovery, saved outputs/review, lineage, retention and notifications. Production security/compatibility
checks remain an operator qualification step. The full product goal remains active; all three phases are in progress.
