# 0144 - Edit completed recurring text results

Status: accepted, 2026-10-27.

The useful first weekly plan required an owner-edited copy. Recurring text results hid that same editor, leaving
owners unable to make ordinary corrections in the review flow. The store already permits separately reviewed
owner-authored artifacts on unfinished work; the UI excluded every scheduled occurrence.

Expose the existing **Edit a copy** and draft editor for scheduled work that is awaiting review after a completed
measured text request. Running, failed or uncertain requests, conversation-only updates and isolated browser or
script outputs retain their existing UI. Existing member permissions still apply.

Saving creates a distinct owner-authored artifact requiring its own approval. The generated artifact, execution
receipt, occurrence accounting and finite schedule are unchanged. Unfinished edits retain the existing tab
recovery behavior. The editor explains that a correction affects this result only: it does not silently modify
the instructions or references approved for later occurrences. There is no model request, automatic revision,
publication or additional spending. Task completion still requires an approved result.

This is a UI change over the existing artifact command, not a new backend permission or execution path. It does
not weaken the prohibition on retrying or reassigning a scheduled occurrence as independent model work.
