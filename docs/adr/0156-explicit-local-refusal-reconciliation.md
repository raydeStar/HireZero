# ADR 0156: Reconcile evidenced local refusals separately from provider receipts

Status: accepted

## Context

A historical business-brief request can remain uncertain even when retained local logs establish that runtime validation refused it before process creation. The existing reconciliation path requires a provider receipt. Inventing such a receipt would misrepresent what happened; blindly rerunning an uncertain request could duplicate spending.

## Decision

The owner can explicitly reconcile an uncertain brief request using either a provider receipt or local evidence of refusal before dispatch. Local evidence has its own diagnostic receipt field. It requires the exact request version and digest, a diagnostic identifier, an explanation and explicit confirmation that no request was sent. It settles zero usage only and cannot replace any provider receipt or positive observed usage. Provider reconciliation retains its existing usage and termination requirements.

Neither path imports output or reruns the request. Existing audit events remain, with the resolution appended. A timeout, cancellation or currently missing executable alone does not establish non-dispatch. The system does not infer historical resolution from current runtime health.

## Consequences

The owner can recover from a proven local refusal without falsifying provider accounting. This remains an evidence-based owner attestation, not automatic verification of the explanation. A new model request still needs its own prepared packet and applicable spending authority. Routine tests use fictional evidence and providers.
