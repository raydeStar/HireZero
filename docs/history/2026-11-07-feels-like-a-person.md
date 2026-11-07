# Feels like a person · November 7, 2026

The owner asked what could make the employee feel more like a person, and chose: first-person wording, a catch-up only
when there is news, live status while they work, an answer when you approve something, and teaching the employee about
you (per person) and the company (shared). Decision record: [0185](../adr/0185-the-employee-speaks-and-knows-who-you-are.md).

## What changed

- **Wording.** The composer's notices, the empty chat and a draft's next step are in the employee's voice ("Here's my
  draft…", "Saved. Chip will start once you approve it below."). A finished reply's receipt is a quiet "What this reply
  cost".
- **Typing.** While a reply is on its way the employee types ("Reading your message and writing back…", then "Still
  writing…" after 30 seconds); a running draft says "Working on … I'll put the draft here when it's done."
- **Answers to approvals.** Approving a draft, the goal ladder or a shift gets a reply in the chat saying what happens
  next; the shift's answer comes before its first round.
- **Catch-up.** Coming back after ten minutes or more to work that finished or stopped while you were away, the
  employee says so first, with Read it / Show me. Nothing new, nothing said.
- **About you / About the company** in Context & settings. About you is each member's own and reaches replies and
  shift rounds with their name; the company is the shared business brief.

## Checks

- .NET: `AboutYouTests` (a reply knows who it is talking with and what they said; members describe only themselves;
  empty clears), with `MarketingSoulTests`.
- Browser: marketing-shift (ladder and shift answers in order, About you saved), conversation-reply (typing while
  pending; reply counts ignore the employee's own lines), employee-review.
