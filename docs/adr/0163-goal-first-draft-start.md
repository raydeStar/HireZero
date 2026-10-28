# ADR 0163: Start with a goal and a useful draft

Status: accepted

## Context

The owner clarified that immediate usefulness is the priority: a user should state a few goals and receive useful
work without navigating setup screens, choosing an organization structure or answering avoidable questions.
The previous Home checklist exposed business confirmation, hiring, department onboarding, assignment preparation
and result review as separate chores.

## Decision

Put goal entry on Home. Existing companies use their approved brief. A new company supplies one sentence about
its business and a goal; its name is optional. A role-owned recommendation selects a specialist and a concrete
first deliverable. Show the suggested deliverable and allow inspection of defaults and model settings.

One owner-only, transactional `goal.prepare` command confirms only the supplied owner facts, leaves other profile
fields Unknown, creates the suggested specialist and department when necessary, and prepares one measured
internal assignment. It reuses an existing active specialist and binds the exact current approved brief. It never
approves imported or generated research, replaces an existing brief, or dispatches a model. A new goal for an
existing employee receives its own conversation. The request requires a usable first draft and explicitly labeled
assumptions rather than blocking on noncritical questions. Role-specific templates remain in their role modules.

Prepared internal jobs load their exact review automatically, with full context expandable. Existing run consent,
account/allowance disclosure, packet digests and dispatch checks still apply. After authorization, the exact result
opens in Work. Home puts the latest copy of each recent result above setup and team status.

Browser request receipts survive reload. Uncertain writes are reconciled rather than resubmitted. A confirmed
save whose workspace refresh fails retains its task identifier and offers a read-only reload. Existing unfinished
briefs and hires keep explicit resume paths; public website onboarding remains available as an alternative.

## Limits

This is a first-draft shortcut for the existing Marketing, Developer and CEO starter deliverables. It is not
arbitrary autonomous execution or an automatic publishing flow. Model/account connection remains a prerequisite.
Fictional E2E proves the shorter path and recovery; a real first-draft usefulness assessment remains necessary.
