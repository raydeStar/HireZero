# Jev decision transport and company credentials

Status: implementation and focused fictional checks complete; not exposed for application execution.

[ADR 0149](../adr/0149-company-bound-jev-decision-transport.md) adds the typed OpenRouter Decisions transport
without changing Luna/Codex OAuth routing, the legacy Jev tool or the active Marketing schedule.

Twelve focused integration facts and all seven company integration-isolation facts pass with zero skips.
The integration checks cover immutable request bytes, exact company/digest matching, versioned model selection,
explicit credential readers, revocation, concurrent dispatch, lost replies, cancellation before/after dispatch,
HTTP refusals and unknown outcomes, malformed answers/accounting, duplicate JSON fields, bounded UTF-8 input
and bounded chunked response reading. Valid reported cost remains available even if token counts are invalid.
Confidence is retained separately from class probabilities; neither grants authority to act.

The company isolation checks exercise the actual protected secret store and host factory. Two fictional
companies use distinct keys; a missing key, copied encrypted envelope and revocation cannot send. A request
prepared for one company cannot dispatch through the other. No ordinary Jev tool is registered by the factory.

All requests use in-process fictional HTTP handlers. No live model, OpenRouter/Jev call, credential change,
runtime qualification job, application installation or scheduled run was performed for this change. No new
dependency or runtime pin is required. This is connector groundwork, not live provider or product acceptance.

Remaining: durable reviewed decision admission, monthly accounting and reconciliation, user-facing review,
fictional end-to-end coverage of those flows, then separately authorized real provider usefulness/accounting.
