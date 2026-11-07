# Editable personality · November 6, 2026

The owner asked to edit each employee's personality in the app. Decision record:
[0184](../adr/0184-editable-personality.md).

## What changed

- Marketing's soul.md is now the role's default personality, offered through its manifest rather than appended to its
  instructions. Core adds the employee's personality, the owner's edit or else that default, to conversation replies
  and shift rounds only, behind a preamble saying it shapes tone and never permissions.
- **Context & settings** in an employee's conversation (also in the desk) opens with **Personality**: the current text,
  **Save personality**, and **Use the default** once edited. Approvers and viewers can read it.
- `employee.personality` stores the edit on the employee record. It does not cancel running work or bump the
  employee's generation, unlike renaming or changing responsibility.

## Checks

- MarketingSoulTests: the role offers its soul and draft instructions leave it out; a reply carries the default, then
  the owner's edit (without the default, with the preamble, generation unchanged), then the default again after
  clearing.
- .NET: Company 689 passed, Integrations 218 passed, Core 43 passed, none failed (the rest skipped for PostgreSQL).
  Node unit tests passed.
- Browser: marketing-shift now opens Context & settings in the desk, finds the default soul, saves an edit, confirms it
  on the employee, and returns to the default; conversation-reply passed.
