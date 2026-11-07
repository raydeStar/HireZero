# The employee on the other side · November 6, 2026

The owner asked what makes HireZero feel like a person on the other side, and chose to push on it before recording:
one sign-off per reply, the morning round arriving as a conversation, and a personality again. Decision record:
[0183](../adr/0183-one-sign-off-per-reply.md).

## What changed

- **One sign-off.** Send in a conversation prepares the reply, checks the server's sealed packet is the one just
  prepared, and authorizes it with the same `measured.run` command. The spend limit sits beside Send ("up to
  $0.05"). If anything after preparing fails, the prepared reply waits with its usual review controls; nothing
  retries. "Prepare one reply for review first" stays under +.
- **The round is a message.** In the desk, the shift's latest round arrives as a message from the employee: "Good
  morning! Here's my round for today", what's changed, where they'd focus, "Here's what I'd do today" with **Do it**,
  and "One question for you" with **Answer**, which starts a reply quoting the question. While the round runs, the
  message shows typing dots and "Watch me work"; before the first round it says when that will be.
- **The note speaks for itself.** Work is named by what the owner asked for (the goal's conversation title), not
  "Marketing draft". On shift it adds "Today I'm focused on …" from the latest round and "I'm on shift; my next round
  is …", which replaces "I'm free".
- **A soul.** `src/HireZero.Roles.Marketing/soul.md` gives the Marketing employee a personality for conversation
  replies and morning rounds: first person, warm and plain, a view of their own, honest, one question at a time,
  mindful of the owner's time. Drafting prompts are unchanged, so customer copy keeps the business's voice.

## Not yet

- Follow-ups on approved work (the next round asking how it went) and a daily email to the owner.
- Owner-editable personality per employee.
- The earlier `marketing-hire` project treated SOUL.md as a boot-owned workspace file; its text was not kept, so this
  soul is written fresh.

## Checks

- Web type check, format and build; MarketingSoulTests and the company test project (687 passed, 5 skipped, 0
  failed).
- Browser: marketing-shift (round as a message, "Here's what I'd do today", the on-shift note line, Answer quoting the
  question), conversation-reply twice in a row (the review-first path, then one-click Send completing one reply with
  one fake start), goal-start, goal-ladder, company-chat, team, measured, action-recovery.
- The conversation-reply step that failed intermittently after the desk merge passed in both runs.
