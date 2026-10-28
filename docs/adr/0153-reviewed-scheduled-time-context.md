# 0153 - Give recurring work an approved calendar anchor

Status: accepted, 2026-10-28.

The scheduler knew an occurrence's due date, but employee requests contained only the repeated instructions
and approved business context. Relative requests such as a weekly content plan had no reliable calendar anchor.

New employee schedules default to a visible, editable option to include their scheduled date in each request.
The option belongs to the definition and therefore to the exact owner-approved digest. It adds a bounded
`scheduledOccurrence` record to the captured context: due time with local UTC offset, named time zone, cadence,
one-based slot number and total authorized slots. It uses the stored due slot, including daylight-saving
adjustments and missed slots, rather than the current clock. Captured packets retain their original value.
The model instructions distinguish this planned time from execution time, fresh research, prior outcomes or
authority. Schedule review discloses the option; the request receipt displays the value actually supplied.

Absent or false opts out and is omitted from serialized definitions. Older approved schedules and captured
contexts keep their exact bytes and authority; installing this change does not add dates to them. Changing an
approved schedule still requires a new draft and separate review. Script, browser and source-refresh schedules
do not acquire this employee-model option. No previous output is automatically added to subsequent requests.

This supplies reliable time context. It does not prove output usefulness or useful customer return.
