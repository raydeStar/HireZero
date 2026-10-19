# 0034: Support, the third employee

Status: Accepted (Phase 4).

## Context

Phase 4 asks for a third employee, Support or Finance/Ops, built only from a new module folder. This proves the
platform scales by hiring rather than by editing. Support was chosen first:
- its work (customer replies, triage, help articles) is drafting for the owner's review, which the platform already
  governs;
- it needs no external system to be useful;
- Finance/Ops would first need accounting integrations.

Customer messages carry personal data, and customers ask for things the business may not grant.

## Decision

- **Only its own folders.** Support is `src/HireZero.Roles.Support` (the module) and `web/src/roles/support` (its
  reply-brief editor).
  - The host references role projects by wildcard and discovers modules at startup
    ([ADR 0006](0006-compiled-in-module-discovery.md)). The web picks up `web/src/roles/*/company.tsx` by glob.
  - No file in Core, the host, the web shell or another role changed. The only other changes are:
    - the solution entry;
    - the regenerated lockfiles that record the new project;
    - its tests and documentation.
- **What it does:**
  - `support-reply`, a measured skill with a structured brief (customer message, what the owner knows, the
    outcome wanted). It drafts one reply that promises only the stated outcome, and lists the facts relied on,
    the facts missing and anything needing a person.
  - `support-triage`, which orders approved prepared messages by urgency, with a suggested owner action for each.
  - `help-article`, a draft help-centre article from approved evidence.
  - Chat and meeting turns, like every employee.
- **Deterministic intake, before any model.** The `support-intake` action splits pasted messages and redacts emails,
  phone numbers and card numbers. A card number is a 13 to 19 digit Luhn-valid number. A bare run of digits stays,
  because it is usually an order number. The action flags:
  - urgent, billing, legal, security and privacy topics;
  - legal, security and privacy as needing a person, whatever a draft says.

  The owner approves the prepared batch before triage uses it, so models only ever see redacted text. A reply
  brief's message and context are redacted the same way when saved.
- **No sending, no tools.** Support declares no tools. It cannot send, refund or change accounts. Connecting a
  helpdesk later would follow the ads pattern: neutral Read contracts first, and replies sent only as owner-approved
  publications.

## Consequences

Adding an employee is now demonstrated as two new folders and their tests. Redaction is pattern-based, so names,
addresses and unusual formats are not removed. The owner still reviews every prepared batch and every draft, and the
reply skill is told to quote no contact details. A Finance/Ops employee can follow the same way.
