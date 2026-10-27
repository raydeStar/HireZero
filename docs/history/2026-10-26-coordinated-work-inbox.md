# Coordinated work in the inbox — 2026-10-26

The product audit found a gap between coordinated execution and owner attention. Plan proposals/completion and
meeting outcomes did not create inbox notices. Individual meeting-turn artifacts created review alerts, while
blocked meeting tasks were excluded. The cockpit also requested review of collected discussion turns rather than
directing the owner to minutes.

[ADR 0116](../adr/0116-coordinated-work-inbox-destinations.md) adds durable parent plan and meeting decision alerts.
The final successful meeting turn now emits the bounded-discussion outcome event. Ordinary turn success is quiet;
failed, blocked, withheld or uncertain work retains an exact task destination. Alert details include a bounded,
cleaned objective or meeting title. Email remains generic and subject to existing consent and no-replay rules.

Inbox links open the exact current-company plan or meeting, focus its highlighted card and retain its ID in the
URL. Reload and Back preserve navigation; unavailable IDs and unsupported destinations have explicit guidance.
Today/cockpit also carry exact parent IDs and surface the need to propose minutes. Successful turns remain in the
meeting record instead of occupying the latest-results list. Meeting states use decision-oriented wording.
Viewing alerts grants no authority, and accepting minutes still creates no follow-up work.

## Verification

- Test-host build passes with zero warnings/errors. Web typecheck and production build pass with the existing
  bundle-size advisory.
- Seventeen focused facts pass across orchestration notices, notification delivery and Today. Added cases cover
  proposal/completion destinations, distinct readable details, durability, per-member read marks, cross-company
  refusal, quiet discussion turns, exactly one discussion outcome alert, separately proposed/accepted minutes,
  operation replay without duplicate notices, no follow-up dispatch and uncertain-turn recovery without replay.
- The extended fictional inbox browser journey passes through the real host, commands, database and UI. It retains
  existing email-consent, receipt-loss, read persistence, uncertain-delivery and disabled-email checks, then covers
  exact plan/meeting navigation, keyboard focus, reload/Back, unapproved plan viewing, proposed minutes, separate
  acceptance, no new tasks, no per-turn cockpit review requests and unavailable plan guidance.
- Desktop (1440 pixels) and mobile (390 pixels) captures are inspected. The selected record is visible and
  highlighted, approval controls remain reachable, and no horizontal overflow is observed. No uncaught UI
  exceptions are reported. Fake delivery remains at the expected three envelopes after email is disabled.

Private compact proof is under ignored `test-results/`: `orchestration-inbox-build.log`,
`orchestration-inbox-web-build.log`, `orchestration-inbox-tests-final.log`,
`orchestration-inbox-browser-final.log`, `inbox-browser.json`, and desktop/mobile images. All work and identities
are fictional. No live model, email, provider account or deployment was used. Temporary fixture data and browser
profiles are removed after test processes exit.

The prepared source supplement for a separately approved script qualification now has 28 entries, producing
560 source files if applied. The existing 549-file Linux snapshot was checked for unexpected drift. This refresh
updates prepared hashes only; there was no Linux transfer, runtime build/run or application activation. Production
delivery, hosted identity acceptance and real customer usefulness remain open in the delivery ledger.
