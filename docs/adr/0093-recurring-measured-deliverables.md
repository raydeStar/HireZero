# 0093: Repeat useful work as a measured deliverable

Date: 2026-10-26

Status: Accepted; fictional verification is separate from customer usefulness and retention acceptance.

## Context

Recurring employee work previously always used the conversation skill and short-reply policy. A customer could
approve a useful first marketing plan, but scheduling similar work changed its instructions and output allowance.
The completed-result screen also offered no direct path into recurring work. This breaks the proposal's progression
from first useful outcome to dependable repeated work.

## Decision

- Add an optional skill to employee schedules. Omitted/chat retains the existing short-update behavior. Other
  choices must be role-declared measured skills. Coding execution, ordinary meeting turns and external actions
  cannot be scheduled through this model-work path. Roles continue to own their instructions and model purpose.
- Bind the selected skill's instructions, model route and measured policy into the finite schedule authority.
  Allocate the aggregate planning budget using that policy. Validate the role's input at save and execution;
  recheck authority before each dispatch and while work is running. Changed model/context/owner authority stops
  future runs. A finite owner approval covers the reviewed remaining requests; it does not grant other work.
- Deliverable occurrences create ordinary review-required work artifacts. Completing a run means the provider
  finished, not that the owner accepted its output. Artifact approval and task completion remain separate.
- Preserve explicitly selected approved result references, including referenced results from completed plan
  dependencies copied into a new schedule. Show their text during review and bind their revisions/digests.
  Limit references to twelve distinct records and half the request policy's byte allowance. Revocation or a
  changed revision stops future runs; cross-company references are rejected.
- Recurring requests exclude conversation history and implicit thread handoffs, including earlier recurring
  results. Relevant owner-approved documents and memory remain eligible under existing scoped retrieval.
  Coordinating roles retain their declared company overview. This is not approval to publish or use tools.
- Refuse retry/reassignment of all existing scheduled occurrences as independent work. Pausing, uncertain-usage
  holds, restart recovery and finite-run accounting continue through the original schedule. A new schedule needs
  its own review; it cannot replay a previous uncertain occurrence.
- Offer **Repeat this work** beside accepted measured results. Copy the request, employee, skill and selected
  references into a separate company-scoped browser draft. Preserve unrelated unfinished schedules. The owner
  reviews cadence, dates, finite count, current context and allowance before activation. Saving navigates to the
  exact saved schedule so reload does not recreate the copied draft.
- Keep nullable added fields out of old serialized inputs/context. Existing approvals, script schedules and
  source-refresh schedules retain their fingerprints; no database migration or runtime upgrade is required.

## Consequences

Recurring deliverables now preserve the capability that produced the original useful work, while short updates
remain available. The owner must still adapt a one-off request into repeatable instructions and review outputs.
No claim of actual customer retention, live model usefulness, production execution qualification or payment
acceptance follows from local fixtures.
