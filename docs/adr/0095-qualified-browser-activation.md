# 0095: Register browser execution only against a current qualification

Date: 2026-10-26

Status: Accepted; offline and fictional owner-journey verification. Real image/runtime qualification and activation remain pending.

## Context

[ADR 0079](0079-browser-queue-and-owner-review-journey.md) composed the browser queue and owner review with a
fictional executor. The production host still lacked an explicit registration path. Script qualification is not
evidence that a browser image can complete its own itinerary, preserve captures, stop on cancellation and release
resources. Runtime readiness also needs monitoring while a job is executing.

## Decision

- Add a separate `browser-qualification-v1` receipt. Bind it to an immutable local image ID, durable installation
  ID, explicit local endpoint, CLI digest and inspected Linux/runsc engine profile. Require the exact browser
  check set and a validity period no longer than thirty days. Keep operator details in private storage.
- Add an explicitly opted-in qualification suite that uses a real contained browser with fictional HTTP pages
  through the scoped host broker. Check dynamic steps, off-origin refusal, bounded output, cancellation,
  deadlines, saved-output recovery and confirmed resource release. Write a new seven-day receipt only after all
  checks, cleanup and final profile/digest comparison pass. Routine tests do not execute this suite.
- Register the browser only when all five binding settings are supplied and the receipt matches. Default new
  execution to disabled. A script receipt, partial settings, remote endpoint or mutable image tag cannot enable
  browser work. Company compute and the exact itinerary still require their existing owner approval.
- Probe readiness independently of the serial job worker, with thirty seconds between probes, and recheck before
  each dispatch. Check expiry on availability reads. The in-flight authority watcher cancels work when runtime
  availability is withdrawn; the browser adapter also checks availability before releasing captures.
- Preserve the original runtime binding for recovery and cleanup even when new work is disabled or the receipt
  expires. Recovery never starts a new visit. An engine replacement cannot confirm release on the original engine.
  Availability is not added to historical-result authority, so disabling new execution alone does not erase saved
  evidence or prevent reading the original resource's receipt.
- Explain paused execution, expired verification, an unreachable engine and an unavailable image separately in
  the owner UI. Saved results and recovery remain accessible. No private operator paths or engine identifiers are
  included in the public status.

## Consequences

The production composition path exists but remains off until the operator qualifies and explicitly enables an
actual reviewed image. No production image or Chromium pin is selected by this change. A receipt is bounded
operator evidence, not a signed attestation or proof of every containment property. Real-site usefulness and
customer acceptance remain separate requirements.

See [operator instructions](../../runtime/browser/README.md) and the
[verification receipt](../history/2026-10-26-qualified-browser-activation.md).
