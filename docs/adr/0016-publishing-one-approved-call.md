# 0016: Publishing one approved external call

Status: Accepted (Phase 2B.5e1). Extends [0012](0012-company-publication.md) and
[0015](0015-publishing-an-approved-artifact.md).

## Context

Publications authorize exact local write calls, then one external call that the module binds after them, because
its input depends on their results (a commit's hash, a Wiki revision). Some effects have no local step at all:
sending a reply to a message (2B.5d2) or a steering message into a coding-agent session (2B.5e2). The external call
is known in full when the publication is proposed.

## Decision

- **No local calls.** A publication may have zero local calls when its proposal carries `External`, the digest of
  that one external call. `External` is part of the proposal, so the owner's approval covers the exact call.
- **Binding.** When the approved external digest is set, `BindPublication` accepts only that digest. With local
  calls and no `External`, nothing changes.
- **Outcomes, unchanged:**
  - the external call is authorized once and consumed before it runs;
  - its outcome is published only when confirmed, otherwise unknown, and never retried;
  - an unknown outcome is reconciled only by reading.
- **No landing without the send.** A publication with no local calls can never finish as landed without its
  external call.

## Consequences

Sending a reply and steering a session can be ported as owner-approved publications without new authority kinds.
Each still needs its own decision on scope (the `Mail.Send` permission; what a steer may allow the agent to do).
