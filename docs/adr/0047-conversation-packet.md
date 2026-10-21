# 0047: A larger packet for conversational replies

Status: Accepted. Amends the one-job measured policy for replies only (see 0036 and 0041).

## Context

Every measured job was capped at 4 KiB of application packet (context plus instructions). That cap came from the
first Codex-only, single-reply design. Conversation history is the only part trimmed to fit; facts, notes and
evidence are never dropped.

A real company found two problems:
- The marketing employee's fixed content was about 2.9 KB of context: approved department facts (2.2 KB),
  responsibility and the message. The role's instructions brought it to about 4 KB. So every reply was sent with
  **no history at all**: the employee remembered nothing said earlier in the conversation.
- Approving the company-wide facts (1.7 KB) would have pushed the fixed part over 4 KiB, refusing **every** reply.

The 4 KiB packet is about 1,000 input tokens. On the API model in use (about $0.10 per million input tokens), a
16 KiB packet costs about $0.0004.

## Decision

- A conversational reply has its own named policy, `oauth-measured-conversation-v1`:
  - **16 KiB** of application packet;
  - 6,000 tokens of packet headroom and a 14,000-token planning reservation;
  - the same output target (1,000 tokens), visible-text stop (2,000 characters) and 60-second deadline.
- It is one of exactly three known policies. Both providers still refuse any other, and the worst case is still
  checked against the job's allowance before anything is sent.
- History is still trimmed oldest-first to fit. Facts, notes and evidence are never dropped.
- Other measured work keeps 4 KiB (`oauth-measured-v1`); context drafts keep 24 KiB.

## Consequences

- Replies can see the conversation and the approved company and department facts together.
- A reply reserves up to 14,000 tokens of the employee's and department's token budgets while it runs. Actual
  usage, usually 1,000 to 4,000 tokens, is what is charged.
- A reply prepared under the old policy is no longer current after the upgrade, and is withheld. The owner
  prepares it again.
