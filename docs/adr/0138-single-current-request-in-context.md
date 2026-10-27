# 0138 - Send the current task request once

Status: accepted, 2026-10-27.

Preparing a first assignment saves the owner's request as a conversation message and as task input. Previously,
context capture also included that message in history, sending the full request twice. The duplicate consumed
the bounded context allowance and one of the sixteen history positions without adding information.

When capturing a new non-meeting packet, omit a history message only when its author is `owner`, its sequence
equals the task watermark, and its body exactly equals the task input. Apply the check before selecting the last
sixteen history messages. Earlier identical messages, employee messages and different text at the watermark
remain eligible under the existing history rules. The task input and saved conversation message are unchanged.

This is an exact duplicate removal, not semantic compression or a change to history scope. Meetings retain
their ordered turns. Conversation replies already exclude the current message; revisions and scheduled work
retain their existing history rules. Approved facts, selected evidence and feedback are not shortened.

Only newly captured packets change. Saved packets, their digests and previously reviewed schedule authority
are not rewritten. New review receipts measure the smaller packet using the existing byte accounting; no
estimated token or billing saving is presented as measured provider usage. No model call is needed to validate
the deterministic change.
