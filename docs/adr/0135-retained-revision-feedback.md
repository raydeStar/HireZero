# 0135 - Retain the owner's feedback across revisions

Status: accepted, 2026-10-27.

A revision preserved the original request and immediately previous draft, but only included the most recent
feedback. A second correction could therefore lose requirements introduced by an earlier review.

New revision context includes optional `priorFeedback`, containing the earlier immutable revision requests from
oldest to newest. The current request remains separate. Instructions retain earlier requirements unless later
feedback explicitly changes or removes them. Neither current nor earlier feedback becomes a business brief fact.
The exact authorization review displays both earlier and current feedback.

Only the immediately previous draft is included. Old drafts and conversation history are not accumulated.
The existing 24 KiB deliverable context ceiling remains in force; feedback is not silently dropped to fit it.
The ancestry is tenant-scoped, restricted to the same employee/thread, cycle-checked and bounded by the existing
16-level context-validity ceiling. Excessive chains require a separate owner-consolidated request.

The field is omitted when empty. Earlier serialized context and receipts retain their bytes, and their existing
knowledge/revision validity checks still apply. New snapshots also bind the ordered feedback chain. Each revision
still needs a fresh exact preparation and separate authorization; this adds no repair loop or model request.
