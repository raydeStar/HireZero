# 0184 — An editable personality per employee

Status: accepted, 2026-11-06. Owner request. Builds on [0183](0183-one-sign-off-per-reply.md).

## Decision

- A role may offer a default personality, its **soul**, through its manifest (`EmployeeManifest.Soul`). Marketing's
  is `src/HireZero.Roles.Marketing/soul.md`. Roles no longer append it to their own instructions.
- Each employee may carry the owner's edit (`CompanyEmployee.Personality`, stored on the employee record; no new
  table). `employee.personality` saves or clears it. An empty body returns to the role's default.
- Core adds the personality, the owner's edit or else the role's soul, after the instructions of **conversation
  replies and shift rounds only**, behind a preamble: it shapes how the employee talks with the owner, never what they
  may do, and every rule before it still applies. Drafts, ladders and other deliverables leave it out, so customer
  copy keeps the business's voice.
- Editing a personality is not an authority change. Unlike `employee.update`, it does not bump the employee's
  generation or cancel queued or running work. It is owner-only, and read-only for a retired employee.
- The owner edits it under **Context & settings** in the employee's conversation, prefilled with the current text,
  with **Use the default** when edited. Approvers and viewers can read it.

## Consequences

- A reply prepared before an edit no longer matches its sealed instructions digest and is withheld; the owner sends
  it again. An active shift keeps running and its next round uses the new personality, since the shift's approval does
  not cover tone.
- The personality is owner-written text placed in the instructions. It is the owner's own direction, capped at 4,000
  characters, and the preamble keeps it from widening permissions; nothing it says changes what the server allows.
