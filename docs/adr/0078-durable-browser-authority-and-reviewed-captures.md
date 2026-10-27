# 0078 — Durable browser authority, shared compute and reviewed captures

Status: accepted; host queue, browser controls and production qualification pending. Date: 2026-10-26.

## Problem

The resource adapter in [ADR 0077](0077-browser-resource-execution-and-recovery.md) cannot decide which company
may run an itinerary or whether recovered output still has authority. Its captures need a durable task and owner
review path. Giving browsers a separate compute allowance would allow customers to exceed the intended shared
limit by switching execution types.

## Decision

- Append company migration 23 for company-scoped browser jobs. Existing migration checksums stay unchanged.
  Jobs bind the employee/task generations, runtime identity, exact origins, ordered steps, viewport, screenshot
  permission, request/byte/time ceilings, proposal deadline and reserved compute seconds. Only an active owner
  can prepare, approve once, revoke or select output. Preparation and admission require explicit runtime readiness.
- Start with unfinished standalone work tasks. Current approved context, source provenance, evidence and handoffs
  remain required. Plan, meeting and scheduled composition are separate work. No credentials, private documents
  or ambient tools enter a public browser packet.
- Script and browser jobs share the existing monthly compute policy. A browser reserves its approved duration
  plus twelve seconds for bounded startup/output/cleanup. This exact allocation is saved with the proposal,
  rather than being recalculated from future runtime defaults. Unresolved jobs hold their allocation across UTC
  month boundaries. Only a confirmed never-created job settles to zero seconds; any released browser resource
  consumes the conservative reservation. These allocations are not measured CPU time or monetary charges.
- A transactional dispatch claim yields the execution packet once. Revocation accepts an older saved version
  of this immutable job so worker progress cannot defeat a cancellation click. Employee/context changes,
  approving-owner removal and paused compute also stop authority. The integration runner watches authority
  during execution; returned captures pass a final durable authority check before they are saved.
- Recovery first fences the job against execution. An unclaimed job becomes confirmed unsent without invoking
  an executor. A claimed job can only read its original receipt and release its resource. A replacement runtime
  cannot dispatch or recover it. Lost acknowledgements never cause another visit. Recovery remains possible
  when new execution is unavailable.
- Validate scoped typed output, bounded host usage, release status and the original resource reference before
  saving. Retain unknown measurements as unknown; worker diagnostic counters never become host accounting.
  Confirmed creation is monotonic across recovery. Save output before calling release with the exact request
  and recorded resource ID. Failed cleanup keeps the hold. Suspected pre-existing resources are not removed
  through the ordinary execution path. Malformed receipts cannot trigger removal of recoverable engine logs.
- Browser/context exit and resource removal are distinct observations. A completed driver receipt without
  confirmed browser/context exit is marked `cleanup-incomplete` and cannot become an approved task result.
  Revoked late output is withheld. Outcome and cleanup-attention events enter the existing owner inbox.
- An owner can select up to 8,000 exact characters from a completed capture for ordinary task review. Store
  the job/result digests, reading step, page URL, capture date, partial-evidence flag and excerpt offsets/hash.
  This creates an unapproved artifact, not approved company knowledge. Task completion still needs artifact
  approval, and sharing still needs its own decision. Revoking the source job or its context fences downstream
  completion, handoff and retrieval through the existing evidence checks. Reopening a saved excerpt does not
  rerun the browser. Up to eight excerpts per job keep this composition bounded.
- The compute API and usage screen show a combined history with script/browser labels and explain the shared
  limits. Browser job details are available in the company task detail contract. Historical records and their
  operation receipts remain retained; full browser-data deletion is not implemented by this decision.

## Verification and remaining composition

Fictional executor tests exercise approval, restart, tenant isolation, cancellation, owner revocation, shared
quotas, retained cleanup holds, malformed output, review/completion, source revocation and migration 23. Existing
script lifecycle, adapter and protocol checks remain regression coverage. The underlying real-Chrome process
fixture is documented in ADR 0076; these new lifecycle tests do not themselves run Chrome or prove containment.

This change does not register an executable browser provider or background browser queue in the host. Browser
itinerary/capture controls, all-company queue startup/recovery, a pinned production image, qualification-based
activation, retention controls and composed browser E2E remain required. The three-phase product goal remains
open; real usefulness, provider authorization, pricing and customer validation are separate requirements.
