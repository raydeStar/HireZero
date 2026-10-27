# 0131 - One active review decision per task

Status: accepted, 2026-10-27.

Editing a copy creates a separate artifact. The previous Today projection treated every unreviewed artifact as
an active request, including artifacts from completed, canceled and superseded tasks. This created duplicate
keys and left old drafts asking for attention after the owner had finished the work.

Today now groups artifact review by active task. Multiple drafts share one review item and its existing owner
priority. When the task can complete using an approved, current result, the item instead asks the owner to
complete the task. Meeting results retain their separate meeting workflow. Other unresolved work keeps its
review request; this change does not discard failed or uncertain execution outcomes.

Completion remains an explicit owner command. Only the chosen artifact is approved; earlier drafts, provenance,
receipts and historical inbox notifications remain unchanged. Completed work displays approved results first
and labels unapproved copies as earlier drafts. Canceled and superseded work no longer offers the review command
that the store refuses. No model request or publication is implied.
