# 0052: Company drafting before hiring

Status: Accepted. Extends 0051 and the explicit measured-provider policy in 0036.

## Decision

Business profile extraction is a role-neutral company operation, not a hidden employee. Its immutable request
contains the confirmed business identity, selected source snapshots and existing owner corrections. No private
employee chat, department knowledge or tool authority is included.

Preparing a draft does not dispatch a model request. The owner reviews the exact request and positive allowance,
then authorizes one pinned measured-provider call. Company-wide token reservations, provider holds, bounded local
execution, usage receipts and restart fencing apply. Unknown outcomes are never replayed automatically. An owner
may reconcile an unknown operation only with an explicit receipt, usage and confirmation that execution stopped.
Automatic provider qualification stays unchanged: both real providers deliberately decline automatic work. This
operation uses their existing measured interface with the owner's exact decision, packet digest and allowance.

Only a valid structured response whose citations match the captured sources may create a profile draft. Model
output cannot assert owner provenance, erase captured owner corrections or approve knowledge. Stale, late, invalid
and uncertain responses remain withheld; confirmed usage is still accounted for. The owner reviews the draft through
the existing exact profile approval boundary. Routine verification uses fake providers exclusively.
