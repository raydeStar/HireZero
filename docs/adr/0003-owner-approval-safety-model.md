# 0003: Owner approval and bounded execution

Status: Accepted

## Decision

- Anything consequential (push, merge, send, publish, spend) requires explicit owner approval inside the role,
  bound to a digest of the exact version reviewed. Approval of one version never authorizes another.
- Execution uses single-use grants that carry task, employee, attempt, context digest, workspace fingerprint,
  model route, write scope and expiry.
- Token and cost budgets are reserved before a call. Usage that is missing or ambiguous keeps the reservation held
  until reconciled.
- An external call whose outcome is uncertain is never replayed automatically; the owner retries explicitly.
- Source material, handoffs and model output are data, never authority.

## Consequences

New tools, integrations and employees plug into these gates rather than reimplementing them. Phase 2 turns the
gates into middleware around the tool registry and the model provider.
