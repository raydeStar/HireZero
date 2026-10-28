# Owner edits for recurring text results

The accepted first plan needed an edited copy, but the recurring review UI hid the editor from every scheduled
result. [ADR 0144](../adr/0144-edit-completed-recurring-text-results.md) exposes the existing editor after a
scheduled text request completes and its task awaits review. Owners can correct the result without requesting
another model response. The editor explicitly distinguishes this correction from changes to future schedules.

The existing artifact command already supported this operation; no backend permission, model instruction,
schedule definition, runtime pin or output policy changed. The original generated draft remains intact and the
copy is labeled owner-authored. Review and task completion remain separate steps.

Verification:

- The production web build and the company test project build pass.
- Three focused xUnit facts pass: owner edits preserve receipts and future schedule authority; recurring
  deliverables retain their limits and separate result approval; uncertain occurrences cannot escape their
  original execution through retry or reassignment.
- The existing fictional recurring-work browser journey passes, including copying exact generated text,
  editing, refresh recovery, saving a separate draft, approving that copy and completing the occurrence.
  Original artifact data, measured receipts, budgets and the exact remaining schedule are unchanged by the edit.
  The next authorized occurrence still runs normally. No live model or external service was called.
- The application is installed in both local workspaces with separate application/data recovery backups.
  Each install's 180 release files match the published hashes. Accounts, saved work, model connections and
  schedules are preserved; both audit chains remain intact.

The owner-approved two-run pilot remains active on its original dates, with zero occurrences consumed. The first
real recurring result has not run yet; fictional lifecycle checks do not establish its usefulness or retention.
