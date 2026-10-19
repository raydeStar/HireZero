# 0022: Tamper-evident audit log

Status: Accepted (Phase 3).

## Context

HireZero already keeps company events, which record each approval and its actor since
[ADR 0020](0020-members-roles-and-sign-in.md), as well as an append-only table of tool calls, and attempts with
model usage and receipts. All of it is ordinary rows: anyone who can write the database file can change or delete
history without a trace. Phase 3 asks for an audit log of approvals, tool calls and model calls that shows tampering,
and that can be exported.

## Decision

- **One chain per company.** `co_audit` is append-only. Each entry has:
  - its sequence number and time;
  - its kind: `event`, `tool-call`, `model-call` or `coding-run`;
  - its subject id and actor;
  - a canonical JSON body;
  - the previous entry's hash, and its own hash.
- **The hash is keyed.** Each hash is an HMAC-SHA256 over the previous hash and the entry, with a 256-bit key kept in
  the data root beside the database, not inside it. Changing, reordering or removing any entry, or recomputing the
  chain without the key, breaks verification at that entry.
- **What is recorded:**
  - every company event, which covers every approval (artifact, plan, knowledge, meeting, execution and
    publication reviews), member change and structural change, with its actor;
  - every tool call: allowed, refused or failed, with input digest and authority;
  - every model call and coding run, at each change of its attempt's phase or its grant's state, with route,
    usage and provider receipt.

  Entries are written in the same transaction as the change they record, where there is one. A chain starts with an
  entry noting how much earlier history predates it.
- **Verify and export.**
  - Members can verify the chain: whether it is intact, the number of entries and the head hash.
  - Owners and approvers can export it as NDJSON, each entry with its hashes, ending with a line giving the
    verified head and count.
  - An exported head is an anchor: kept elsewhere, it also reveals truncation, because a later verification must
    reach that head.

## Consequences

Edits to the database show up as a broken chain. Someone holding both the database and the key file can still
rewrite history, so the export's heads are the anchor against that, and an external timestamping or write-once store
could later hold them automatically. Bodies keep digests and identifiers, not secrets or third-party text.
