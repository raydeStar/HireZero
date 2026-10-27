# 0128: Preserve feedback and prepare a separate revision request

Date: 2026-10-26

Status: Implemented; focused backend and browser verification passed.

## Context

The first real deliverable was useful but too generic. The existing “Ask for changes” action only changed an
artifact status: it captured neither the requested changes nor a follow-up assignment. Result-linked long-term
memory also requires an approved result, so it cannot substitute for revising an unaccepted draft.

## Decision

- Ask for specific written feedback and preserve the unfinished form in member/company-scoped browser storage.
  Save that feedback beside the exact reviewed artifact; retain the original output and usage receipt.
- For an independent internal text draft supported by the measured runtime, create a separate revision task for
  the same employee and conversation. Replace the original assignment in the active queue while retaining its
  history. Carry the original assignment and selected model, but create a fresh budget root and authorization.
- Automatic workers cannot claim revision tasks. Preparing an exact context/model/allowance review is separate
  from authorizing another request. A repeated command returns its original operation receipt.
- Include only the immediately preceding draft and the reviewer's feedback in a typed revision context. The draft
  remains unapproved reference material, not company knowledge, approved evidence, or external permission.
  Omit conversation history from these requests to avoid repeating the assignment or including earlier drafts.
  Revalidate the exact source/digest/version and its original knowledge lineage before dispatch and result reuse.
- Return a complete replacement deliverable. Do not merely critique the old draft or describe intended edits.
- Plan, recurring, repository and action outputs keep their existing authority workflows. They can save review
  feedback without creating an independent task that could bypass their parent or execution approval.
- Long-term memory remains a separate explicit decision. Requesting a revision does not approve company facts or
  silently add feedback to future unrelated tasks.

No schema migration or additional provider permission is required. Better real output and owner acceptance still
require an observed result; fixture checks cannot prove usefulness.
