# 0096: Repeat approved public website itineraries on a finite schedule

Date: 2026-10-26

Status: Accepted; fictional lifecycle and owner-journey verification. Production runtime and usefulness remain separate acceptance.

## Context

The proposal calls for recurring useful work and scoped browser automation. Website reviews currently require a
new one-off request for each capture. The existing finite scheduler and exact browser itinerary can support repeat
visits without introducing an open-ended browser agent or a new provider.

## Decision

- Add `browser-task` to the existing daily/weekly scheduler. The owner chooses a saved standalone proposed or
  completed review. Copy its exact origins, steps, screenshot setting, viewport and execution limits into the
  schedule authority, together with its source ID/digest and original runtime identity.
- Require the distinct `authorize-recurring-browsers` consent for the remaining occurrences. Reuse the one-to-31
  occurrence bound, time-zone handling, company context checks, owner authority, active-schedule limit and missed
  slot policy. Saving a draft does not visit a website. No missed-slot catch-up burst is introduced.
- Each due slot atomically creates one work task and one browser job, admits shared compute and purchased
  allowances, and consumes that slot's exact recurring authority. The existing browser worker dispatches once.
  A model worker cannot claim the task, and a manual retry or replacement itinerary cannot reuse the occurrence.
- Hold the occurrence open until resource release is confirmed. Restart recovery uses the original job's receipt;
  it cannot visit the site again. Failed or unknown work blocks further slots for owner review. Admission failure
  consumes the due slot but rolls back partial task/job allocation.
- Pausing or ending a schedule withdraws in-flight authority. Resuming requires unresolved resources to settle.
  Runtime withdrawal stops active work and blocks new slots. Historical completed captures and approved excerpts
  remain reviewable after future occurrences end, subject to their original revocation and retention rules.
- The recurring consent is independent of the source one-off proposal. Discarding that proposal does not silently
  cancel a separately approved schedule; the UI explicitly directs the owner to pause or end the schedule.
  Source proposals from delegated plans or other schedule occurrences are not eligible templates.
- Show the full itinerary, remaining count, per-visit bounds and total reserved compute before consent. Preserve
  lost-response recovery and unsaved drafts. Display saved scheduled captures in the work view, label isolated
  recurring tasks by their actual work type, and link each occurrence to its saved work.
- Captures are evidence, not automatic analysis or authoritative business updates. The owner separately selects
  page text, approves the task result and completes the task. This does not claim real-site usefulness or a
  qualified production browser.
- Omit null optional fields from serialized legacy records, preserving prior schedule and job contracts. No
  schema migration, runtime upgrade or production configuration change is required.

## Verification

See the [recurring website review receipt](../history/2026-10-26-recurring-website-reviews.md). Local fictional
verification is distinct from the operator qualification required by [ADR 0095](0095-qualified-browser-activation.md).
