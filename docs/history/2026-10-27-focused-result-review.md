# Focused deliverable review

Opening a saved result inherited Chat mode, leaving the draft narrower than the idle composer. Model/account
help also forced horizontal overflow in the split composer. [ADR 0145](../adr/0145-focused-deliverable-review.md)
defaults non-conversation results to the full Work panel. At the tested desktop viewport, the draft now has
approximately 800 pixels instead of 340. Show in chat preserves the explicit split layout; Expand work returns
to focused review and moves keyboard focus with it. Model/account controls and help wrap within the composer.

Verification:

- Production TypeScript/Vite build passes. The existing bundle-size advisory remains.
- Both existing fictional browser journeys pass: business drafting and recurring schedules. They use the
  already-built fixture backend with the new physical web assets and call no live model or external service.
- Recurring review assertions cover full Work mode by default, a result width above 700 pixels, split composer
  bounds, preservation of an unfinished owner edit across both view changes and refresh, and keyboard focus
  after expansion. Saving and approving the edited copy still preserves the original result, measured receipt,
  budget and remaining schedule.
- Desktop screenshots show readable full-width copy and wrapped controls in the optional split view.
- The release is installed in both local workspaces with application/data recovery backups. All 180 release
  files match the published hashes in each install. Both services are healthy; accounts, saved work, measured
  records and audit chains are preserved.
- The exact owner-approved pilot schedule remains active and unchanged, with zero runs consumed. Its next two
  occurrences remain November 2 and 9 at 09:00 America/Denver. No new model calls were made.

No role instructions, recurring authority or model policy changed. Real recurring execution and usefulness of
those future results remain pending; the fictional journeys do not establish those outcomes.
