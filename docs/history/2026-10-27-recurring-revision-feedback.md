# Revision feedback survives recurring work

The recurring shortcut previously copied the original request and approved result, but omitted the feedback
that produced an accepted revision. New drafts now retain the complete feedback chain in order, under
[ADR 0140](../adr/0140-revision-feedback-in-recurring-drafts.md). The owner can inspect the source history and
edit the instructions before saving. Existing saved drafts and approved schedules remain unchanged.

Combined instructions exceeding the existing 2,000-character limit remain intact, with saving disabled until
the owner consolidates them. Nothing is silently truncated. Incomplete, cyclic or cross-conversation revision
chains cannot seed the shortcut.

The production TypeScript/web build and .NET host build pass. The expanded fictional recurring browser journey
also passes: two earlier revision requests reach the draft in order; oversized feedback requires explicit
consolidation; owner edits survive refresh; and both feedback requirements reach the scheduled model context.
The existing accepted-output reference, two separately reviewed occurrences, finite allowance, pause/resume
and mobile checks pass as part of that journey. No live model call is made.

An initial assertion compared the submitted feedback's trailing whitespace with the stored text. The backend
already normalizes that whitespace. The corrected assertion verifies the complete saved feedback, and the
journey then passed. This was a test expectation correction, not a content-truncation workaround.

This establishes instruction continuity. Owner usefulness acceptance and real recurring customer use remain
open; no actual pilot result was approved and no real recurring schedule was activated during this pass.

The release is installed with separate main and pilot data backups. All 181 staged application files match the
installation. Existing main-workspace accounts and work are preserved, with an intact audit chain. Pilot tasks,
artifacts, employees, knowledge and measured-job records match the pre-update snapshot; its audit is intact and
the native completion count remains nine. Both workspaces still have no schedules. The review host is reopened
on the updated installation with the real result still awaiting owner review.
