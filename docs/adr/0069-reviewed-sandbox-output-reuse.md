# 0069 — Reviewed sandbox output reuse

Status: Accepted; fictional backend and desktop/mobile browser verification complete.

A script result was durable but disconnected from task completion, reviewed handoffs and organizational memory.
Copying it manually also discarded its source lineage. Reuse should enter the existing artifact review flow.

An owner may select one completed output, or an exact excerpt of at most 12,000 characters, and prepare a task
artifact. The full UTF-8 output stays saved and can be downloaded. The source record pins the script packet,
result digest, filename, full output digest and selected character range. Existing evidence sanitation applies;
the resulting text requires its own artifact approval. Preparation neither executes another script nor publishes,
shares or approves its output. It works after runtime shutdown and while cleanup is unresolved.

The task moves to awaiting review only if it is waiting, blocked, failed or already awaiting review. It cannot
replace running work or append a result to completed/canceled work. An exact selection is retained once even
when HTTP receipts are lost or a fresh operation ID repeats the selection. At most eight excerpts per script are
retained. The result digest, not a transient cleanup revision, identifies the reviewed output. Completed tasks no
longer offer preparation of new scripts; new work needs a new task.

Artifact origin remains revocable. Source job revocation, changed task/employee authority, revoked inputs and
invalid upstream evidence fence new artifact approval, completion, task-context capture/dispatch, handoff,
memory use and publication. These checks are bounded through existing provenance chains. Historical output
and review records remain visible; revocation removes future authority rather than rewriting history. Pausing
new compute or disabling its runtime does not revoke an already completed result by itself.

The optional origin is stored in the existing artifact JSON, omitted for older artifacts. No new table or schema
migration is needed. The host has a dedicated bounded output-review route because a 12,000-character Unicode
excerpt can exceed the generic command transport limit. A tenant cannot supply origin metadata through ordinary
artifact creation. The UI moves focus to the saved result, then reuses existing approval, completion and explicit
employee-handoff controls.

This completes a local result-to-review path for standalone script tasks. It does not compose scripts into plans
or schedules, qualify real containment, measure compute cost, or establish real customer usefulness.
