# 0071 — A tracked first deliverable

Status: accepted. Date: 2026-10-25. Updates the first-assignment behavior in ADR 0053.

## Problem

The approved-brief onboarding flow requested a four-part one-week marketing plan through a conversational reply.
The platform instructed that reply to fit within 80 words and marked the conversation task complete when text arrived.
The result did not enter the artifact review/completion workflow. That mismatched the proposed first useful
deliverable and made a generated reply indistinguishable from completed, owner-reviewed work.

## Decision

`assignment.prepare` atomically saves an owner's request, creates one ordinary work task and prepares its exact
measured context for review. It requires the current approved company knowledge ID, version and digest, an active
employee, that employee's private thread, an eligible measured skill and an explicit allowance. Preparation does
not spend, dispatch, approve a result or grant tools. Failed preparation rolls back the message, task, context and
attempt together. Existing operation receipts handle a lost response without creating another task.

The role module owns the requested work. Marketing adds a `marketing-starter-plan` skill for a small team's first
week: approved observations and citations, hypotheses with actions/days/success measures, draft sample copy,
unknowns and a few owner questions. The web contribution names that skill. Core does not depend on Marketing.

Measured skills may opt into the named `deliverable` profile in their manifest. The platform fixes its bounds:
24 KiB of application context, 1,800 observed output tokens, 22,000 planning/total tokens, 90 seconds and 9,000 visible
characters, with an instruction to return at most 500 words. The word count is guidance; the existing token/character
checks enforce result acceptance. Token/time/credit limits remain explicit soft stops for native-account execution;
the API adapter retains its pre-send price/allowance checks and output-token request limit. Modules cannot supply
arbitrary limits. Invalid profiles or use on an unmeasured skill fail manifest validation.

The existing ordinary-work and conversation policies remain unchanged. Prepared policy, instruction and context
digests still bind authorization and completion. No change silently expands an existing job's allowance. Changing
the approved company facts or role policy invalidates a stale proposal; revoked facts withhold late text while
reported usage is settled.

The first plan now produces a `needs_review` artifact and leaves its task `awaiting_review`. The owner approves
the exact result and separately marks the task complete. The existing handoff and organizational-memory flows can
then reuse the approved artifact. Existing historical conversations and their receipts stay intact.

Saved work cards lead with their result. The original request is expandable; a completed measured run's receipt
is shown separately, below the result. A fully reviewed/completed task says so instead of continuing to say
that its draft awaits review. An owner-authored or script-generated result does not display empty model-run controls
after review/completion, and proposed/running work retains its visible authorization controls.
An explicit Read result action opens the existing work view. On narrow screens that view replaces the chat and
composer so the result and its review controls have usable reading space; desktop retains the adjoining chat.

## Consequences and verification

The first deliverable has a durable work item, a review decision and a completion state. The owner still controls
execution and external actions. The result can be longer than a chat answer without changing every role's budget.
No schema migration, integration/runtime upgrade or live model call is required for this change.

Focused checks cover atomic/idempotent preparation, roles/companies, exact company knowledge, larger bounded output,
review-before-completion, late revocation, manifest validation and the fake API adapter's output limit. The browser
journey begins with business discovery and approval, hires Marketing, loses a first-assignment response, reviews
the exact packet, authorizes one fixture result, approves it, completes the task and reloads the saved result.
Actual model usefulness and a real-user onboarding trial remain separate acceptance requirements.
