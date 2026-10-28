# Calendar context for recurring deliverables

The recurring-work audit found that the scheduler tracked each due date, but supplied the same instructions
without that date in the model packet. Relative requests such as a weekly plan had no reliable calendar anchor.
[ADR 0153](../adr/0153-reviewed-scheduled-time-context.md) adds an owner-reviewed option to include it.

New employee schedules default to the visible option. Each captured request receives its stored due time in
the schedule's time zone, cadence, one-based slot and total authorized slots. The execution receipt can display
the actual saved value. The text distinguishes planned time from execution time, fresh research and previous
results. Existing approved schedules and old packets retain their exact authority and serialized contents.
Script, browser and source-refresh schedules do not receive this employee-model option.

The company test build passes with zero warnings/errors. All 41 selected schedule, recurring-script and
recurring-browser facts pass with zero skips. New facts cover the repeated autumn hour, the following week's
changed offset, a delayed start, restart, immutable old packets, legacy omission, exact opt-in approval and
skipped-slot numbering. The complete fictional recurring-work browser journey also passes, including separate
dates in both occurrence packets, readable saved-date review, result editing, separately approved feedback,
pause/resume and mobile layout. The production web and release builds pass; the existing bundle-size advisory
remains. Provider traffic in these checks is fictional.

The tested release is installed in both local workspaces with independent data backups and retained prior
application files. All 180 release files match each installation; accounts, work, model records, schedules and
both audit chains are preserved. The existing pilot schedule remains active with its original inputs, without
the new calendar option. Installation does not amend that approval or consume an occurrence.

No live provider request, runtime qualification job or additional recurring activation is part of this change.
It addresses date context in P2.1/P2.4, not output quality, actual useful return or customer acceptance.
