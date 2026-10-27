# 0087: Recurring reviewed scripts

Date: 2026-10-26

Status: Accepted; fictional lifecycle and desktop/mobile journeys verified. Production runtime and recurring-report usefulness remain unverified.

## Context

Finite schedules could produce private model replies or check cited public sources. They could not execute an
approved calculation or transformation and return a report. A recurring report should use deterministic compute
without an unnecessary model call, while preserving exact approval, source isolation and non-replaying recovery.

## Decision

- Add a `script-task` schedule type. The owner chooses an employee's saved standalone script review, reviews
  its immutable code, selected document families, output names, runtime tier, resource limits and finite dates,
  then explicitly authorizes one execution per remaining occurrence. Existing one-to-31 occurrence limits,
  daily/weekly clocks, missed-slot policy and active-schedule limits apply.
- A schedule has its own approval. The original proposal supplies immutable code and input selections;
  executing or discarding that one-off proposal does not cancel a separately reviewed schedule. The owner
  pauses or ends the schedule itself. Task-result and delegated-plan inputs are excluded from this recurring
  type because they need their own dependency review. Saving or reviewing a schedule performs no execution.
- Selected document families are fixed by the original selected document IDs. Each occurrence follows their
  explicit replacement links and requires exactly one currently approved revision within the employee's
  audience. A draft is never read. A revoked original with no approved replacement, multiple approved branches
  or an inaccessible replacement stops the schedule. Each job saves the exact resulting document IDs, versions,
  digests and text; changes during execution revoke that packet through the existing source-authority checks.
- Pin the employee/context fingerprint, runtime identity and, for extended work, exact paid-plan entitlement.
  New context, runtime or plan authority needs a fresh schedule review. Current company compute limits and
  subscription allowances are checked for every occurrence. The entire per-run duration is reserved and
  settled using the existing shared monthly pool. There is no automatic purchase or monetary cost estimate.
- Claim each due slot durably, then create its task and one approved sandbox packet in one transaction. The
  existing isolated worker claims that dispatch once. This task cannot enter the model queue, be retried as
  model work or acquire an ad hoc browser/script grant. It inherits no live model or external-action authority.
- Keep an occurrence running until its resource hold is settled. A cleanup or unknown-execution hold prevents
  another occurrence of that schedule, while other due schedules remain eligible. Confirmed failures block
  future occurrences until explicit review. Unknown requests are never replayed; startup leaves saved sandbox
  work to the original runtime's recovery path. A claimed occurrence without a dispatch fails without retry.
- Pausing or ending a schedule revokes active task authority and withholds late output; usage and cleanup still
  settle. Resuming permits only future remaining slots and is refused while previous resources remain held.
  Ending future occurrences does not erase previously valid reports. Existing source and task revocation
  continue to apply to saved results.
- A successfully released script leaves its task awaiting review. The owner selects saved output, approves the
  resulting artifact and explicitly completes the task. Schedule execution completion and owner acceptance
  remain distinct. Run history links directly to saved work; recurring results use existing inbox notices.
- Optional null fields preserve old schedule, authority, run and sandbox JSON. Authority comparisons use
  canonical digests for the new structured template. No schema migration, runtime pin update, live provider
  call or always-on hosting claim is introduced.

The UI shows script scope, current approved replacement semantics, per-run and remaining total allocations,
separate recurring consent and saved run links. Script work exposes output review and recovery rather than
unrelated model-request or retry controls. Broader scheduled browser/developer workflows, production isolation,
actual compute costs and observed customer value remain separate work.

Verification is recorded in [the recurring-script receipt](../history/2026-10-26-recurring-reviewed-scripts.md).
