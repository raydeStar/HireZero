# Keep a recurring review at the right address

Copying an existing schedule showed the newly saved draft but left the original schedule
in the address. Reload could reopen the old approval. Selecting another saved schedule also
left the address unchanged, and browser Back did not restore the corresponding review.

Saving a new or copied draft and selecting a saved schedule now update the address. Back
and Forward restore the matching review and clear the unchecked-by-default authorization
control. The existing task-to-schedule draft recovery path remains intact. These navigation
changes do not authorize or dispatch work, or alter the saved instructions and allowances.

## Verification

- The recurring-work browser journey passes with fictional providers. It copies a completed
  schedule, verifies the new draft's address, reloads it, switches between reviews, uses Back
  and Forward, and checks that consent does not carry across the transition. The mobile
  view fits without horizontal overflow. Model-attempt records are identical before and
  after navigation; the copied schedule remains a draft.
- Existing checks still cover two finite occurrences, lost-save recovery, result review,
  pause/resume, approved feedback, source refresh and exact inbox links.
- The first attempt at the new test stopped at the date guard because the fixture clock
  had advanced beyond the default copied date. The test now selects a future fixture date;
  no product guard was relaxed.
- The frontend build and both required formatters pass. The existing large-chunk warning
  remains. No backend build or live model call was needed.
- Both installed web frontends were backed up and updated, with sixteen matching build-file
  hashes per installation and healthy hosts. In the installed pilot, selecting the old
  schedule changes the address, and Back restores the prepared replacement. Its activation
  decision and saved terms remain unchanged.

No acceptance output was edited, no live model allowance was used, and no schedule was
activated. This closes a returning-owner navigation defect; it does not establish recurring
customer usefulness or retention.
