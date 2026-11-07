# 0185 — The employee speaks in the chat and knows who you are

Status: accepted, 2026-11-07. Owner request. Builds on [0183](0183-one-sign-off-per-reply.md) and
[0184](0184-editable-personality.md).

## Decision

- **The employee's own lines.** Around model replies, the chat shows short lines in the employee's voice built from
  what really happened, never generated and never stored: typing while a reply or a draft is on its way, an answer when
  the owner approves something ("I'll treat it as final"; for the goal ladder, "I'll start with …"; for a shift, "Each
  morning at … I'll bring you …" before the first round arrives), and a catch-up when the owner returns to news. A role
  phrases the answer to approving its own skills through the `approvedSay` web slot; core has a general one.
- **Only when there is something to say.** The catch-up appears only when this employee's work finished or stopped
  while the owner was away (last visit kept in this browser, at least ten minutes earlier). It is said once in the chat;
  the employee's note then leaves out the same finished work.
- **About you, per person.** Each member writes **About you** (`CompanyMember.About`, at most 2,000 characters) with
  `member.about`, which edits only the caller's own membership; any member, viewers included, may describe themselves.
  Two people in one company are two different people to their employees. Members of the company can read it.
- **About the company, shared.** The company stays the approved business brief that every employee already works from.
  Context & settings shows it next to About you; owners change it in Company knowledge.
- A conversation reply records who asked (`CompanyTask.RequestedBy`). Core adds who the employee is talking with,
  their name and their own About text, after the personality on conversation replies and shift rounds only; for a round
  that is the owner who approved the shift. It is placed behind a preamble saying it changes nothing the employee may do.

## Consequences

- Editing About you changes the instructions of the next reply; a reply prepared for review before the edit is
  withheld and sent again, as with a personality edit.
- The lines are presentation: they approve nothing, start nothing and are not model output, so they carry no AI tag and
  do not enter conversation history.
- The last-visit time lives in the browser, so a new browser starts without a catch-up.
