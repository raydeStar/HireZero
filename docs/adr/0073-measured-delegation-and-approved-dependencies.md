# 0073 — Measured delegation and approved dependency results

Status: accepted. Date: 2026-10-25.

## Problem

The platform could approve a delegation plan, but its tasks were excluded from the measured queue used by native
account execution. The manual plan form supplied only the old 5,000-token strict-dispatch allowance per task.
Dependent tasks waited for completion without receiving their predecessors' approved results. A 30-minute deadline
also covered the entire plan, including human review. The plan page exposed neither task progress nor task links.

## Decision

- CEO opts its existing planning skill into the bounded deliverable profile. Its company overview adds each
  colleague's eligible internal skill IDs and private thread ID. It still contains no other employee's private
  conversation, notes or department knowledge. The role proposes structured task assignments and acceptance criteria.
- Ordinary `chat` and role-declared measured skills in an approved plan may use the measured queue. Their fixed
  deliverable policy permits 24 KiB of application context, 1,800 output tokens, 22,000 planning tokens, 90 seconds
  and 9,000 visible characters, with 500-word guidance. Execution skills, meetings and repository grants remain
  separate. Strict qualified-provider execution retains its existing admission path.
- Every measured child requires its own exact context/usage review and explicit model authorization. Preparing
  a packet does not spend, reserve tokens or authorize execution. The parent must be executing, unexpired and
  based on a still-approved exact source proposal, when present.
- New plans allow 1–4 tasks and a ceiling of 5,000–100,000 tokens, retaining the existing minimum 5,000 per task for
  strict work. The manual form proposes 22,000 per task for measured work and shows that ceiling before approval.
  Model proposals must budget for their selected measured tasks. Underfunded historical or new plans are refused
  by measured preparation/admission; they are never silently enlarged.
- A plan's shared root is never raised by measured admission. Consumption by another child may change its budget
  revision, so that revision alone does not invalidate a separately prepared child. Dispatch still checks the
  original plan ceiling and atomically reserves the full job allowance across root, employee, department, company
  and monthly limits. Insufficient remaining capacity sends nothing.
- New plan digests include a seven-day execution window and optional exact source artifact ID/version/digest.
  Proposal approval still expires after 30 minutes. Exact approval starts the seven-day execution deadline.
  Existing plans without this metadata keep their prior deadline and ceiling. This does not lengthen an individual
  model job, create an unlimited schedule or authorize a new retry.
- Completing a predecessor requires owner-approved work as before. Dependent tasks receive its currently approved
  artifact bodies/digests as explicit context evidence, bounded by the existing 12-item/context limits. Owner plan
  review states this sharing. Private employee context is not copied. Missing or revoked dependency results refuse
  preparation, invalidate pending authorization and withhold late output while reported usage is settled.
- A plan created from a coordinator artifact keeps that exact artifact version as authority. Revocation or a later
  version fences approval, dispatch and late completion. Only an artifact from the planning skill may seed a plan;
  an arbitrary coordinator chat response cannot become delegation authority.
- An expired measured packet proven never sent may be freshly prepared within its still-valid parent plan.
  Earlier receipts remain intact. Dispatched, failed-with-uncertain-usage and otherwise attempted work cannot use
  this exception; it is not an automatic replay path.

## User experience

The Plans objective form can ask the CEO to draft tasks; the request opens its measured work review. Generated
proposals show named assignees, tasks, dependencies and shared allowance, with exact JSON available separately.
Owner artifact approval is followed by plan preparation and exact plan approval, then individual measured jobs.
Plan cards show task state, dependencies, deadlines and links to the work views. The parent completes only after
every child completes after owner review.
Prepared jobs are labeled as waiting for authorization instead of running. A refused command with a conflict
response refreshes the workspace, preserves its draft and explains the next step; it does not replay the command.
The owner must review the new state and explicitly submit again.

## Verification and limits

Focused tests exercise shared-budget consumption, unchanged ceilings, dependency evidence, tenant/private-context
isolation, source and dependency revocation, late usage settlement, first-plan completion and proven-unsent expiry.
The browser fixture follows a CEO proposal through Marketing and Developer work, separate packet review, a lost
plan-creation response, saved task reviews and parent completion. Fixtures do not prove actual model planning quality,
customer usefulness or real repository execution. Plans and meetings do not gain browser, sandbox, publishing or
spending permissions from this change. No schema migration or runtime upgrade is required.
