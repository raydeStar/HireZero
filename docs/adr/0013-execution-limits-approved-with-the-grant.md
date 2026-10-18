# 0013: Execution limits are approved with the grant

Status: Accepted (Phase 2B.5b3a)

## Context

Reviewed execution ran under limits fixed in code: 20 seconds, 5,000 tokens and low effort. That is enough for the
synthetic runner, but not for a real fix. Three other limits also blocked longer runs:

- each task's root budget was fixed at 5,000 tokens;
- the reservation was always 5,000 tokens;
- a run stayed current only until its proposal expired, 10 minutes after it was proposed, so a run approved late
  was cancelled early.

## Decision

- **Limits travel with the grant.** A grant carries `CompanyExecutionLimits`: tokens, seconds and effort. The host
  sets the defaults (`CompanyStore.ExecutionDefaults`, the old pilot values unless configured). The limits are part
  of the proposal's digest, so you approve exact limits.
- **One sanity ceiling.** At most 1,000,000 tokens and 3,600 seconds, with effort low, medium or high. Proposals
  outside it are refused.
- **Your approval sets the work's allowance.** Approving a grant raises the task's root allowance to cover its token
  limit. Company, department and employee budgets still cap the run, and a run they cannot cover is not sent.
- **The reservation matches the limit.** Dispatch reserves exactly the approved tokens, and settlement accepts usage
  up to that reservation. Missing or excess usage still keeps the reservation and needs reconciliation.
- **A run's own time.** A dispatched run stays current for its approved seconds plus one minute from dispatch,
  whatever its proposal's expiry. A grant must still be started before its proposal expires.
- **Grants recorded earlier** run under the pilot limits.

## Consequences

- A live coding runner can run real fixes without code changes to the limits. Choosing its defaults, and whether
  to raise the employee, department and company budget ceilings (25,000, 50,000 and 100,000 tokens), are your
  decisions.
- A revoked or unused grant leaves the work's raised allowance in place. It cannot be spent without another approved
  grant, and the other budgets still apply.
