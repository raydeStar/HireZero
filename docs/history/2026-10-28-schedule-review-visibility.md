# Show outdated schedule reviews before their due time

A saved recurring schedule could display `active` after role instructions or approved context changed.
Dispatch correctly refused the outdated approval, but the owner discovered that only when the next run
was due. This undermined the expectation that approved recurring work would happen without supervision.

Schedule list and detail reads now check the same context fingerprint and owner authority used by dispatch.
They expose a read-time review status without changing the saved schedule, digest, version, dates, approval
or occurrence count. Ended schedules remain historical. Drafts and paused schedules with an unchanged
scope can still receive fresh explicit approval; their existing authorization checks are unchanged.

The UI labels outdated reviews **Review needed**, explains that remaining runs cannot start, and directs
the owner to edit an unapproved draft or copy an approved schedule into a new draft. It does not offer a
resume button against changed scope. The attention panel includes an affected active schedule before it is
due, in both company-wide and employee views, with a direct link to the exact schedule.

Verification: 48 focused schedule and attention facts pass, including new cases for pre-due context changes,
changed role instructions, paused draft approval, unchanged audit/approval records, direct targets and
terminal history. Core and web builds pass. Both installed apps have verified backups and retain saved work.
The existing two-occurrence pilot displays its warning with both dates and zero consumed runs preserved.
No replacement was activated and no live model calls were made; 24 of the current 31-run allowance remain.

This is a visibility and navigation fix under existing authorization rules, so no new ADR is needed.
Customer usefulness and recurring-work acceptance remain unproven; the full three-phase goal stays open.
