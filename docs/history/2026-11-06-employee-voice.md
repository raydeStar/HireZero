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
  "Marketing draft". On shift it adds "Today’s focus: …" (the first sentence of the latest round’s focus) and "I'm on shift; my next round
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

## Real run on Vultr (follow-up branch)

A throwaway copy of the follow-up branch ran on the VM in local owner mode (scratch data, a 300-cent daily cap, the
Vultr key as a systemd credential), reached through an SSH tunnel, then stopped and deleted. Fictional bakery, goal
"Get 30 people to pre-order our holiday pie boxes before Thanksgiving."

| Step | Result | Tokens |
| --- | --- | --- |
| First draft | Caption, email and three moves to reach 30; order route left in brackets | 4,634 |
| One-click reply to "I only have about 20 minutes a day. What should I do first?" | No review step. "Start with the ordering route and the deadline… That's your 20 minutes today… One question: which route do you want?" | 2,964 |
| Goal ladder, approved from the chat | Focus on getting people from seeing the boxes to ordering | 3,030 |
| Shift from Start shift (5 mornings, first at the default about eight minutes away) | Round arrived on time as a message: "Good evening! Here's my round for today", Thanksgiving 20 days out with "My view: close orders around 22–23 November", two jobs with the first drafted, a lesson that nothing is in front of customers yet | 10,433 |

Ledger: $0.0077 for the four requests. The responsibility no longer doubles the goal's full stop, and the copy says
"using Vultr". The round asked for this week's numbers rather than whether the draft went out, which the follow-up
rule allows when a bigger question would change today's work; its lesson made the same point.

Fixed from what the run showed: the note named the round by its schedule thread ("I finished Recurring: … morning
round") and read "Today I'm focused on The step to work on is…". Scheduled work now keeps its kind, and the line
reads "Today's focus:" with the round's first sentence.
