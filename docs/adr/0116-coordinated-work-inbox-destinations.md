# 0116: Surface coordinated work decisions in the durable inbox

Date: 2026-10-26

Status: Accepted; extends ADR 0059. Verification is recorded separately.

## Context

The inbox projected task and artifact events but omitted plan proposals/completion and meeting outcomes. Meeting
turn artifacts appeared as ordinary results even though the meeting collects them into a bounded discussion.
Blocked meeting tasks were excluded from alerts. An owner could therefore receive noisy turn-level notices while
missing the decision or uncertainty that actually required attention.

## Decision

- Project plan proposals into attention notices and parent completion into completed notices, linking the exact
  plan. Child artifacts still need their own review and keep their existing task notices.
- Include a bounded, cleaned plan objective or meeting title in authenticated alert details so simultaneous
  decisions are distinguishable. Email continues to omit business content and use the generic inbox link.
- Emit an event when the final successful meeting turn reaches the bounded discussion limit. Project that state,
  proposed minutes awaiting approval and accepted minutes into notices for the exact meeting. Each notice is
  saved in the same transaction as its source event; existing operation/result idempotency prevents duplicate replay.
- Omit ordinary meeting-turn artifact/success alerts. Keep failed, blocked, withheld and uncertain turn alerts,
  linking the exact task so its outcome and usage can be reconciled. A parent alert never retries the task.
- Add explicit plan and meeting destinations to the inbox. Opening one refreshes the current company snapshot,
  saves its ID in the URL, scrolls to the matching record and moves keyboard focus to it. Refresh and browser Back
  preserve navigation. Unknown destinations show an error rather than opening unrelated business sources.
- A missing plan or meeting ID shows an unavailable message within the current company. A URL grants no access;
  the normal company snapshot and membership checks remain authoritative.
- Apply the same decision boundary to Today and the cockpit: collected discussion artifacts do not request
  individual review, a completed discussion surfaces its need for minutes, and plan/meeting items carry their
  exact destination ID. Successful meeting turns remain in the meeting record rather than filling the cockpit's
  latest-results list. Existing priority/snooze keys remain unchanged.
- Keep per-member read marks, generic email content, category preferences, expiry, consent and no-replay delivery
  unchanged. No old events are backfilled or emailed by this change. Viewing an alert does not approve a plan or
  accept minutes; accepted minutes still create no follow-up task or external authority.

## Limits

These notices prove durable state transitions, not the usefulness or correctness of a model's output. Fictional
fixtures establish local behavior; real SMTP delivery, hosted identity-provider acceptance and customer use of
coordinated work remain separate checks.
