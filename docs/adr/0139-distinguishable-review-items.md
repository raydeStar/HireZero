# 0139 - Identify the conversation and request in work review

Status: accepted, 2026-10-27.

Several results from the same employee can share a skill name. Showing that name and a generic review status
alone makes the owner open each item to discover which request it belongs to.

Work lists, the cockpit, Today, recent results and the opened work header now show the saved conversation title
and a request number. Numbers follow saved task order within that conversation, retaining completed, canceled
and superseded tasks so later requests do not change numbers when earlier work leaves the review queue.
Conversation replies have a separate reply sequence. The same label appears before and after opening work.

These are display labels derived from the existing snapshot, not new task IDs or inferred model summaries.
Navigation, priority, snoozing, approval and execution continue to use their existing exact IDs and versions.
No content, authority, model route or stored task is changed. Unknown/non-task needs retain their existing labels.
Long conversation names wrap, and Today includes the context in accessible control names.
