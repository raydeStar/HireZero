# 0058: Finite recurring work with explicit owner authority

Status: Accepted. Extends the company queue, measured execution, context and source freshness contracts.

## Decision

A company may retain 100 schedule records and have at most ten active schedules. The first two work types are
private employee deliverables and checks of one to three sources cited by an approved business brief. Daily or
weekly schedules use an explicit time zone, first date, local time and finite allowance of one to 31 occurrences.
The first date must be at least one minute away and within 90 days when saving a draft. Weekly work repeats on
the first date's weekday. Spring clock gaps move to the first valid minute; repeated autumn times run once at
the earlier UTC instant. A clock gap longer than three hours is refused rather than guessed.

Saving is not authorization. The owner reviews the exact definition, retained context and limits, then explicitly
authorizes the remaining occurrences. Changes to an approved definition require a new draft and approval; drafts
can be edited with an exact version. Pause, resume and end also require the current version and digest. Resume
keeps the original finite allowance and does not replay an earlier occurrence.

The host must be running. Each due slot is claimed durably before work starts. On a late tick, earlier due slots
are recorded as missed and only the newest slot can run, within a one-hour grace period. Older slots are skipped.
Missed slots count toward the approved total, preventing backlog bursts and indefinite extensions of authority.
Migration 16 adds schedules and a run ledger with a unique company/schedule/slot constraint.

## Employee work

Approval pins the initiating owner, active employee identity/generation/responsibility, company and department
knowledge, employee notes, role instructions, model route, runtime identity and measured policy. Changing these
requires new approval. An explicitly conflicting/outdated business fact prevents execution. Current approved
documents within that employee's audience may be retrieved per run; this scope is stated at approval. Each
schedule has its own private thread. General conversation history is excluded.

Each occurrence prepares and consumes one measured conversation request inside a transaction under the schedule's
persisted approval. This is a new explicit recurring capability, not a change to ordinary one-job proposals:
the automatic employee queue still never scans or starts arbitrary measured proposals. The same packet limits,
current-context checks, provider serialization, deadline, cancellation, usage settlement and unknown-outcome
holds apply. No tools, external actions, publishing or developer execution are authorized by this schedule type.
Results are private employee replies linked from run history.

The owner acknowledges existing measured-provider soft limits and chooses a per-run existing-credit fallback
allowance. A schedule-wide token planning budget covers the approved number of occurrences; every request also
reserves against its ordinary task, company, department and employee budgets. Actual usage settles all scopes.
The UI labels API-provider allowances in US cents; that adapter also refuses an estimated request cost above
its allowance. These are admission/reservation controls, not a claim that an external provider enforces a hard credit cap.
Unknown usage retains reservations and prevents resuming the schedule until reconciled through existing controls.

## Source checks and lifecycle

Source schedules pin the exact approved profile and selected citations. The host supplies the existing guarded
source-refresh integration callback; Core remains role-neutral and integration-independent. The manual refresh
limits, robots and public-address protections, shared deadline and company hourly quota still apply. Checks
retain old facts and feed the existing comparison and owner-assessment workflow. Schedule pause/revocation is
checked before reads and at completion; late results lose authority to update freshness.

Failures and unknown outcomes block subsequent occurrences. Startup recovery uses already-recorded terminal
results where available and never re-dispatches an interrupted occurrence. Pausing fences in-flight employee
work, withholding late content while allowing actual usage to settle. All company workers are restored at host
startup, including companies not opened in a browser. Runtime creation is serialized so concurrent requests cannot
start duplicate company recovery workers.

## Experience and remaining scope

Recurring work lives in the Company hub. It shows dates, clock rules, scope, allowances, consent, current state,
remaining slots and linked run history. Unsaved drafts and operation receipts are company-scoped and survive
reload. Drafts edit in place; approved schedules can be copied into a new draft. Results open directly in the
private conversation; source checks open Business brief. Today lists drafts needing approval and blocked schedules.

This does not add cron syntax, an always-on hosting guarantee, notification delivery outside the app, monthly
subscription allowances, premium execution, billing or open-ended tool automation. Permanent schedule/history
deletion remains part of the unfinished retention work. Real customer demand and repeat usefulness require
observed use rather than synthetic scheduled jobs.
