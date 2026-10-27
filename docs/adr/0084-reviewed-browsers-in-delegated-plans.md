# 0084: Reviewed browsers in delegated plans

Date: 2026-10-26

Status: Accepted; fictional lifecycle and desktop/mobile verification. Production qualification remains separate.

## Context

Delegated assignments could run reviewed scripts after ADR 0083, but public website reviews still refused all
plan tasks. Browser admission also did not exclude a model request on the same task, and isolated jobs could
compete while another execution retained an unresolved resource hold. This made a coordinated website-review
and analysis workflow incomplete.

## Decision

- Ordinary tasks in an approved executing plan can prepare public website reviews. Their predecessors must be
  completed with current approved results. The browser packet records the exact parent plan and predecessor
  revisions/digests through the same `CompanyPlanAuthority` contract used by scripts.
- Plan authority does not authorize visits. The owner reviews the exact websites, steps, viewport, screenshot
  choice and limits, then grants one-time consent. The existing network restrictions remain. No predecessor
  body, employee note or private file is implicitly supplied to the browser.
- Model, script and browser admission is mutually exclusive on a task. An isolated execution holds its place
  until resource release is confirmed, including after output has arrived. The owner may prepare another exact
  proposal while a resource is unresolved, but cannot run it. Separate assignments keep their existing company
  and plan limits. Shared compute accounting does not expand or spend the plan's model-token ceiling.
- Fresh execution requires a current unexpired parent. Result history can remain valid after the parent is
  completed and its execution window closes. Source, dependency, employee, task and approval revocations still
  fence dispatch and downstream reuse; invalid late captures are withheld while cleanup remains accountable.
- A reviewed browser excerpt can satisfy its plan assignment and become an explicitly selected script input
  for the next employee. Deleting raw captures preserves existing exact excerpt proofs, while revoking browser
  authority continues to invalidate downstream work through those proofs.
- Owner controls explain competing work and disable execution approval until it clears. A stale task revision
  refreshes state and preserves the browser itinerary for explicit resubmission. Completed plan tasks display
  saved-evidence copy and cannot approve unused proposals.

The browser `Plan` field is optional. Script plan authority has the same serialized shape as ADR 0083, and
existing standalone approvals retain their saved digests. No database migration or runtime-pin change is needed.
This decision does not activate a production runtime, permit signed-in browser actions, authorize arbitrary
downloads, or implement recurring browser/scripts, repository orchestration or premium execution.

Verification: [delegated browser and script journey](../history/2026-10-26-delegated-browser-script-journey.md).
