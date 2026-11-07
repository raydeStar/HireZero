# 0183 — One sign-off per reply, and an employee with a soul

Status: accepted, 2026-11-06. Owner decision ("signoffs should be one, not three"). Amends how the web app asks for a
conversational reply's authorization; the server's reply policy ([0047](0047-conversation-packet.md)) is unchanged.

## Context

A chat reply took three owner actions: prepare it, open the sealed context to review it, then tick a box and press
**Send & authorize**. It was safe and it felt like filling in a form, not talking to a colleague. The owner wants the
employee to feel like a person on the other side.

## Decision

- **Send is the one sign-off.** In a conversation, Send prepares the reply, reads the server's sealed packet for it,
  checks that the packet is the one just prepared (same attempt and digest), and authorizes that one reply with the
  same `measured.run` command. The composer shows the spend limit beside Send ("up to $0.05"); the server still
  refuses a reply whose worst case exceeds it.
- If any step after preparing fails, nothing is retried: the prepared reply waits in the conversation with its review
  and **Send & authorize** controls, exactly as before.
- **Prepare one reply for review first** stays available under + for anyone who wants to read the context before it
  goes. Save message only is unchanged.
- Nothing else is loosened. Replies stay text-only; publishing, sending elsewhere, spending and work still need their
  own approval. Shifts keep their one consent for a finite run of rounds.
- **The Marketing employee has a soul.** `src/HireZero.Roles.Marketing/soul.md` describes how they talk with the
  owner: first person, warm and plain, a view of their own, honest about what they do not know, one question at a
  time. It is added to the instructions for conversation replies and morning rounds only; drafting instructions do not
  include it, so customer copy keeps the business's voice. In the desk the morning round arrives as a message from the
  employee, and their note names work by what the owner asked for.

## Consequences

- One click now starts a paid model request in a conversation. The cap on each reply, the company's daily cap and the
  free allowance are unchanged and enforced before sending.
- Changing the soul changes the instructions digest of replies and rounds, so a reply prepared under the previous text
  is no longer current; it is prepared again.
- Later: a standing reply allowance for replies outside the composer. The soul became editable per employee in
  [0184](0184-editable-personality.md).
