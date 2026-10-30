# Home continues the current revision

After a first Home draft, requesting a refinement creates a separate revision task and
supersedes the original. Home retained the original task ID in its saved goal. Its
**Continue my work** button therefore reopened the rejected draft instead of the revision
request, even though the feedback had saved successfully.

The isolated browser journey reproduced that wrong destination before the fix. Home now
follows the saved task's revision chain within the same employee and conversation. It opens
the current request or delivered revision after reload, while earlier drafts and receipts
remain in history. A revision awaiting authorization is described as ready for review of its
model and allowance, rather than implying that the model has run.

This changes navigation only. It does not approve a revision, dispatch a model, promote a
result, or rewrite any artifact. Missing, ambiguous, cross-conversation or cyclic references
do not cause an arbitrary jump to different work. Existing approval and recovery rules stay
in force.

## Verification

- The focused goal-flow checks pass, including multiple revisions in unordered snapshots,
  a delivered or canceled revision, missing targets, employee/conversation boundaries,
  conflicting successors and cycles. Existing exact-terms and no-replay checks also pass.
- The Home browser journey passes with fictional providers. It covers Refine, return Home,
  the pending revision, a second revision after reload, and the delivered replacement on a
  390-pixel mobile viewport. Following these links adds no measured requests.
- The same journey retains its existing optional-website, single-consent, requested-asset,
  lost-response and disclosure-recovery checks. Synthetic test artifacts are fixture data;
  no owner acceptance output was edited.
- The frontend build and both required formatters pass. The existing large-chunk warning
  remains. No backend code changed or broad regression rerun was needed.
- Both local web frontends were backed up and updated. All sixteen build-file hashes match
  each installation, and both hosts remain healthy. No server restart or company-data write
  was needed. Backups and installation receipts are retained privately.

This cycle made no live model calls, purchases, publishing actions or schedule changes.
It establishes the returning-owner navigation behavior, not customer retention or a new
usefulness verdict on the earlier raw drafts.
