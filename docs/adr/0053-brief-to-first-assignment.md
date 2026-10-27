# 0053: Approved brief to first assignment

Status: Accepted. Extends 0051, 0052 and the role UI contract in 0006.

The conversational first-plan behavior below is superseded by [ADR 0071](0071-tracked-first-deliverable.md):
new first plans are tracked assignments whose artifacts require review before completion. Existing conversational
history and receipts remain intact.

## Decision

A role may declare a `business-setup` UI contribution. The shared hiring UI supplies only an approved business
profile backed by approved company knowledge. Marketing contributes a suggested responsibility, editable
department defaults and a small first-plan request. Other roles retain their existing onboarding.

Department defaults refer to the approved company brief instead of copying its factual claims into another
knowledge record. The owner still reviews department context. Existing drafts are preserved; suggestions do not
silently replace an in-progress hire. The Marketing goal and request remain editable.

The first plan is one private conversational reply, using the existing measured context, allowance and explicit
authorization flow. It requests grounded observations, hypotheses, small experiments, sample copy and unknowns.
Preparation neither invokes a model nor creates an autonomous task, tool grant, campaign publication or send.
The result remains durable conversation history and does not automatically become approved company knowledge.

Context-led conversation preparation may supply `Id`, `ExpectedVersion` and `Digest` as an optimistic precondition
on the latest approved company knowledge. The server refuses a changed, revoked or cross-company reference before
saving a message. The existing dispatch boundary also refuses a prepared packet after its knowledge is revoked.
General conversation preparation retains its existing optional-company-context behavior.

The browser keeps the first request and its uncertain operation ID per company and employee. A lost response is
resolved by reading its receipt, never automatically repeating the mutation. Synthetic tests verify mechanics;
customer usefulness and actual model quality require separate field evidence.
