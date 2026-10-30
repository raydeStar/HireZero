# Home can start work alongside an unapproved note

The installed local walkthrough found a false blocker: a company with a manual knowledge draft,
but no business identity or structured profile, was sent to the Business Brief editor instead of
receiving Home's business sentence, goal and optional website form.

Home and `goal.context` now distinguish an unrelated unapproved note from an actual business brief.
Starting a goal creates a separate profile from the owner-confirmed inputs. The previous note keeps
its body, digest, state and version, and cannot enter the model context. Existing business identities
still require their current review; already-approved company knowledge is not silently replaced.

## Verification

- All 10 `CompanyGoalStartTests` passed, including preservation and exclusion of an old unapproved
  note, idempotent context preparation, and the existing unconfirmed-business refusal.
- The isolated Home browser walkthrough passed with a seeded fictional note: optional website,
  one consent, normal first results, lost-response recovery, direct email drafting, and desktop/mobile
  controls. The note remained unchanged and its unsupported claim was absent from the model packet.
- Web build/typecheck and Release Core build passed. The first test build was interrupted after it
  stopped progressing; the build with shared compilation disabled completed with no warnings or errors.
- Both local installs received the tested Core assembly and web assets through the existing backup
  routine. Each install verified 218 files. All 20 companies retained their identities, profiles,
  knowledge, employees, threads, tasks, artifacts and measured jobs; audit chains remained intact.
- Exact schedule readback, including current-review status, matched the baseline. The two previously
  approved weekly occurrences remain authorized under their existing terms. Automatic execution
  settings and model connections were preserved.
- Installed Home now shows the direct form. The next internal request is prepared there with the
  public website and an unchecked consent control; it has not been dispatched.

No live model calls, editorial copies, new schedule approvals, publishing or hosted deployment were
performed. The browser fixture establishes flow behavior, not raw model usefulness or customer
acceptance. Private backup/install receipts and screenshots remain under `.pilot/`.
