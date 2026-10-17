# 0009: Every legacy feature is kept, as an optional integration

Status: Accepted (Phase 2B)

## Context

The legacy Developer profile carries features the company workspace lacks: Microsoft 365 intake, coding-agent
session steering, Azure DevOps, Jev triage, tailnet multi-user access and the daily brief. Retiring the profile
(roadmap Phase 2B) needs a decision on each. The owner runs HireZero in more than one environment (a work setup and
a very different home setup), so a feature that is essential in one place may be unavailable or unwanted in another.

## Decision

All of them move into the company workspace. None is assumed:

- Each is an integration that registers its tools only when its configuration is present (account, endpoint,
  credential or binary), and is otherwise absent rather than failing.
- Configuration lives in the data root, never in source, so one build serves every environment.
- The owner can see which integrations are active and why the others are not.

## Consequences

- No legacy feature is deleted when the profile retires; each needs a port with tests before the profile goes.
- Defaults are conservative: a fresh data root has no integrations active beyond local, read-only ones.
