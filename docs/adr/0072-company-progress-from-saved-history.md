# 0072 — Company progress from saved history

Status: accepted. Date: 2026-10-25.

## Problem

The context-first proposal needs observable onboarding, corrections, first-task completion, repeat use and costs.
The existing event, profile revision and usage stores contain part of this evidence, but the owner cannot see a
coherent summary or export it. Counting page visits, automatic schedule runs or unpriced jobs as success would
produce misleading pilot results.

## Decision

Add a company-scoped read-only progress projection and a Company → Progress view. Read the existing records in one
transaction. Refreshing or downloading the projection does not create events, mutate records, dispatch work or
contact an analytics service. Every authenticated member can read these aggregate measures, matching the existing
usage/history read boundary; the host resolves company membership before the route executes.

The versioned JSON export contains only the company ID, task IDs, counts, dates, states and costs. It excludes
business facts, employee names, prompts, result text, actor identities and provider receipt contents.

Definitions:

- Onboarding time is the elapsed wall-clock time between the first saved `business.identified` event and first
  `business.profile-approved` event. Missing or reversed dates produce no duration. Time away is included.
  Confirmation records owner confirmation, not independently verified identification accuracy.
- Generated briefs are distinct profile IDs with a model-origin revision. Field edits count saved changed fields
  in owner-origin draft revisions after generation and before the first approval of that profile. Repeated edits
  count repeatedly; unchanged saves and approval/revocation alone add no field edits. Manual-only briefs are excluded.
  Edits are not automatically classified as factual mistakes.
- Work totals use ordinary work tasks, excluding conversations, context drafts and tool-action records. Completed
  work uses current persisted task state. Completion dates require explicit `task.completed` history. The first
  tracked assignment is the earliest saved `assignment.prepared` task, with a direct link to its current work view.
- Member request days are distinct UTC dates of task assignment events with a member actor, excluding every task
  linked to a scheduled occurrence. Scheduled tasks have a separate count. These are requests, not visits, distinct
  users or customer retention. Missing actor/date history cannot become an observed return visit.
- Model cost totals reuse saved usage entries and price snapshots. Provider-reported charges and estimates remain
  separate; unpriced settled jobs and unsettled holds remain visible. The average for completed work appears only
  when every completed task has usage records and all attached records are settled and priced. It includes all
  model attempts attached to those tasks, including unsuccessful attempts, and excludes unrelated onboarding/chat
  costs. It is not full job cost: compute, tools, storage and subscription fees remain outside this measure.
- Earlier records without complete event, revision or usage history trigger a coverage notice. No past events,
  prices, actors or milestones are invented. The export does not classify a company as a real customer or trial.

## Consequences and verification

No schema migration, telemetry service, pricing choice or runtime change is required. The projection is recalculated
on request and may be more expensive for a large company; a future aggregate store must preserve these definitions
and source coverage. The UI exposes refresh, last-checked time, retry after read failure and download of that exact
snapshot. The original data remains the source of truth.

Focused tests cover recorded versus missing milestones, idempotent receipts, field-edit denominators, current
task completion, chat and schedule exclusions, replay/restart, company isolation and cost uncertainty. Browser
verification uses the fictional onboarding-to-completion journey, a downloaded JSON file, failed-refresh recovery,
mobile layout and a link back to the completed task. Real identification accuracy, perceived usefulness, paid
conversion, cohort retention and the proposed pilot still require actual authorized observations.
