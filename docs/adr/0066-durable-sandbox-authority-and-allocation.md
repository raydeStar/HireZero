# 0066: Durable sandbox authority and runtime allocation

Status: Accepted. Local lifecycle composition; host/UI exposure and real runtime qualification pending.

Follow-up: [0067](0067-script-review-ui-and-recoverable-queue.md) adds host/UI fixture composition and refines
queue deadlines and stale-progress revocation. Real runtime registration and qualification remain pending.

## Decision

Extend [0065](0065-bounded-sandbox-provider.md) with company-owned script proposals and a durable dispatch
gate. Migration 21 adds sandbox jobs and a separate compute policy; the earlier schema checksums remain frozen.

A proposal belongs to one existing standalone employee task. It binds the task and employee generations,
name, pinned runtime identity, exact script, approved document IDs/versions/digests/text, input filenames,
requested output names and resource limits. Only selected documents enter the container. Company-context
conversations cannot select an employee's private documents; other companies' records remain inaccessible.
Plans, meetings and schedules require further explicit orchestration support and are not admitted here.
Task evidence and reviewed handoffs retain their existing revocation checks.

An active owner reviews the exact proposal digest and authorizes one dispatch. The review expires after an
hour. Admission, approval provenance and the monthly reservation commit together before infrastructure is
called. A second transaction claims the dispatch exactly once, including across separate store instances or
host restart. A replayed HTTP operation returns its stored receipt; it cannot claim another execution.
Changing the script or limits requires a new proposal and review.

Compute admission is independent of AI token admission. Its default policy is paused, with safety ceilings
of 20 jobs and 1,800 allocated script seconds per UTC month when explicitly enabled. Owner configuration can
set 0–1,000 jobs and 0–120,000 seconds. Each admitted job consumes a job slot and holds its approved maximum
script seconds. Once resource release is confirmed, a sent or uncertain execution consumes that conservative
allocation; a confirmed unsent script consumes zero seconds. Unresolved holds carry into later months.
These are **allocated script seconds**, not measured CPU seconds, elapsed provisioning time, a subscription
entitlement or a monetary charge. Observed adapter wall time is recorded separately when available. Physical
compute, memory, storage and infrastructure overhead pricing remain unfinished product requirements.

The integration runner monitors document/task/member authority, company pause, explicit revocation and its
bounded execution deadline. Losing the authority store cancels the invocation. Successful output is saved
transactionally before resource release can remove the engine log. It is untrusted review material, never
automatically approved knowledge or a published artifact. Task detail exposes saved jobs, and outcome/release
attention events link the personal Inbox to the task. Audit entries retain proposal/result digests and release
receipts without copying the private script into the audit chain.

Recovery first fences further dispatch. An approval that was never claimed can settle as unsent without
touching infrastructure. A claimed job may only recover a result or release its recorded resource; it cannot
create or start work again. Completed output remains recoverable after the original dispatch deadline, but
revoked context, removed owners and changed employee authority withhold late content. Previously saved output
survives cleanup failure and restart. Repeated identical uncertain receipts do not repeatedly notify the owner.
Confirmed settlement is immutable. Runtime identity changes cannot authorize a new provider to operate on an
older provider's job. An operator must retain the appropriate provider for its outstanding recovery work.

## Verification boundary

Offline tests cover exact approval, concurrent admission and durable claims, document/tenant isolation,
revocation, owner removal, changed runtimes, response loss, cleanup failure, cross-month holds, recovery before
and after dispatch, result-before-release ordering, notification links and schema upgrade compatibility.
No test executes submitted code on the host, starts Docker or claims real OS containment.

The adapter and lifecycle runner remain unregistered in the product host until review/result UI and fake
provider browser acceptance are implemented. Promotion to usable sandbox execution additionally requires
explicit qualification of the pinned image on a Linux `runsc` engine. Artifact promotion, plan/schedule
composition, commercial entitlements and premium runtime classes are separate remaining work.
