# 0043: Context after onboarding, and company facts

Status: Accepted. Extends 0041.

## Context

Fill from sources (0041) drafted a department's context only inside the hiring guide. After onboarding, the
Departments panel showed an empty form with raw field ids, and saved versions as raw JSON. Changing approved context
meant retyping all of it.

Company-wide knowledge (a knowledge version with no department) already reached every employee's context. But no
screen could write it, and it had no fields.

## Decision

- **Context is edited from its latest version.** The Departments panel opens a department's latest saved version
  in labelled fields, with each field's help text. Saving revises a pending draft in place, or creates a new draft
  beside the approved one. Versions are shown as readable fields. Nothing changes what employees use until the
  owner approves a version.
- **Fill from sources works after onboarding.** The same panel offers Fill from sources through the department's
  guide employee. It uses the same audited reads, one owner-authorized turn and policy, and only fills the form.
- **Company facts have fixed, role-agnostic fields**, defined in core: product, customers, stage, facts and
  boundaries. A Company facts section edits them the same way. `context.draft` with `Decision = "company"` drafts
  them through any active employee. The task records `ContextScope = "company"`, and its instructions name the
  company fields instead of the role's. Core still knows no role.

## Consequences

- Company facts are saved as JSON over those fields, like department context, and they are used once approved.
- Needs you names each draft by its department and the hire it finishes. A company draft reads
  "Company: approve shared knowledge".
