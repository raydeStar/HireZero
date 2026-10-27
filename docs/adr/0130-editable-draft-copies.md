# 0130 - Edit a copy without another model request

Status: accepted, 2026-10-27.

The weekly-content walkthrough needed small corrections after generation. The existing manual-artifact form
could save them, but it started empty beneath the result and lost unfinished edits on refresh. This encouraged
moving the work into separate files or requesting another model run for an ordinary edit.

Text results now offer **Edit a copy** when the task accepts owner-authored work. The action fills the existing
editor with the exact stored text and focuses it. It does not change the original artifact, its receipt or its
approval. Saving uses the existing `artifact.create` command and produces a separate owner-authored artifact
that still needs review. No model request, approval, publication or new execution authority is implied.

Unfinished text uses the existing member-scoped tab storage, additionally keyed by company and task. It survives
refresh and navigation within that tab. Closing the tab is not durable storage; the UI distinguishes this from
saving the draft. A confirmed save clears the recovery text. Failed or unconfirmed saves retain it. A later task
state change leaves unfinished text readable for copying, without allowing a write to an ineligible task.

Opening another copy cannot replace nonempty unfinished edits. Owners can finish or clear their text first.
Storage failure leaves the in-memory editor usable and asks the owner to retain a copy before leaving. The editor
shows word and character counts and respects the existing 12,000-character limit without truncating copied text.

The feature is role-neutral UI over existing commands. It does not reinterpret a manual edit as a native model
result or create a new backend revision relationship. The original and edited copy remain separate artifacts of
the same task. Structured delegation proposals and scheduled work retain their existing workflows.
