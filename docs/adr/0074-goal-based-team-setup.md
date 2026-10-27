# 0074 — Goal-based team setup and role-owned first assignments

Status: accepted. Date: 2026-10-25.

## Problem

The approved business brief always linked to Marketing. Developer and CEO hires returned to blank department
setup and lacked an immediate, tracked first assignment. The generic assignment view also described Marketing
outputs for every role. This did not implement the product proposal's needs-based team assembly.

## Decision

- Each web role module may contribute a recommendation and an approved-brief setup through the existing
  `business-setup` slot. Platform code knows no role IDs or role-specific goal vocabulary. Adding a role's
  contribution does not require editing another role.
- Marketing, Developer and CEO contribute small, deterministic goal-word matchers. A suggestion states the
  matched text and the kind of work the employee can help scope. These are explainable starting points, not a
  model judgment or a claim that the role will improve business outcomes. They require no model call.
- Only owner-confirmed or sourced goal fields from the current approved business profile are eligible.
  Hypotheses, unknown goal text, draft/revoked profiles and earlier business revisions do not supply a match.
  The profile must still supply the latest approved company knowledge. This same gate applies to setup presets.
- Display at most three matching roles. If none matches, Marketing offers a clearly labeled general starting
  option for the initial small-business audience. The owner can always browse all roles. No employee is hired,
  context approved, permission granted or job started by a recommendation.
- If a suggested role is already represented by non-retired employees, show those employees instead of a
  duplicate-hire shortcut. An unfinished hire opens onboarding; another employee opens their private chat.
  Explicit additional hiring remains available from the normal role catalog.
- Developer and CEO supply editable responsibilities and department context from the approved brief. Templates
  refer back to current company facts rather than duplicating claims into a second record. They mark missing
  system evidence unknown and grant no access through system descriptions.
- Developer adds `technical-starter-plan`; CEO adds `company-priorities`. Both opt into the existing measured
  deliverable policy. They produce internal advice with observations, hypotheses, missing evidence and concrete
  next work. CEO priorities work before any other employee exists and do not create delegation plans.
- The existing atomic `assignment.prepare` flow binds the exact company knowledge, creates a task and a reviewable
  packet, and sends nothing. Separate model authorization and artifact review still precede task completion.
  The assignment panel receives its description from the role rather than hard-coding marketing deliverables.

## Evidence and limits

Focused store tests cover the new starter skills, unchanged limits, no repository resource, no delegation authority
and refusal to complete unreviewed work. The browser journey covers role selection from approved goals, prefilling,
onboarding, two reviewed first results, existing-employee reuse, revocation and unknown goals. Existing Marketing
onboarding/first-assignment checks are retained. Real usefulness and recommendation relevance need customer
observations. Keyword matching is intentionally disclosed as a limited heuristic in the delivery record; this
change does not claim semantic planning, automatic hiring or complete implementation of all product phases.
