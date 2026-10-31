# Returning to work on the following day

A fresh tab lost the Home continuation state and prefilled the original business
goal under **Generate my first draft**, even when that work already existed. The
owner had to pass this form to find results. Only three results were exposed, with
no way to find an older one from Home, and the list supplied no dates.

Returning Home now leads with the active shift, when present, then saved results
and unfinished requests. A first-time company still leads with the goal form.
Results show their update time and open the exact saved task. **Find all results**
adds search across requests, employee names and saved draft text. Superseded work
remains excluded from this list rather than competing with its replacement.

**Pick up a request** exposes saved work that has no current result, including an
unrun revision or an interrupted request. It opens the existing task and never
replays execution. A returning owner can reach it without tab-local recovery state.
The daily summary separates today's work from the existing attention backlog and
recognizes ready drafts before optional final approval.

**New task** stays near the greeting. It reveals and focuses the next request,
reuses approved business details and uses **Generate draft** for subsequent work.
A fresh session no longer seeds it with the completed goal. Text already being
edited in the current tab is preserved; an uncertain submitted request is revealed
for recovery rather than discarded. Every new run still needs fresh, exact consent.
Unsubmitted text retains its existing tab-session storage boundary; the durable
cross-session recovery described above applies to saved requests and results.

## Verification

- The expanded Home browser journey passes on desktop and mobile. A browser-only
  clock advances one day, and clearing only the fictional goal-session state
  simulates reopening. The test finds old work beyond the three-result preview,
  opens the correct result, starts a distinct request with fresh consent, reuses
  the specialist and dispatches exactly once. It then clears the tab state again
  and opens an unrun revision from Home without dispatching it.
- Existing first-run, optional website, lost-response recovery, revision-chain,
  copy/export and refinement checks still pass. The scheduled morning-work journey
  also passes, including next-job access, a kept lesson, a measured change and pause.
  All execution in these fixtures is fictional: zero live model calls.
- In the installed app, a fresh tab showed the saved native email above the compact
  next-task entry. It opened directly. New task focused an empty field without
  asking again for the business description. A typed follow-up survived reload;
  consent remained unchecked and no generation was started. The raw email stayed
  unchanged. This is a return-navigation observation, not a second model-quality test.
- Frontend build and repository formatters pass. The existing Vite large-chunk
  advisory remains. Both local web roots receive hash-verified backups and asset
  copies, without restarting hosts or migrating data. Readback preserves company
  task/artifact/request counts and schedule versions, digests, states and occurrences.

Private screenshots, fixture receipts and installation records remain outside
source control. A simulated following day is engineering evidence, not observed
customer retention or an actual unattended overnight run. No new model spending,
publishing, schedule approval or company-context promotion was performed.

Earlier navigation evidence: [native Home walkthrough](2026-10-30-native-home-walkthrough.md),
[unapproved-note isolation](2026-10-30-home-unapproved-note.md),
[combined Home verification](2026-10-29-opus-integration-verification.md),
[disclosure recovery](2026-10-29-home-terms-recovery.md), and
[current-revision navigation](2026-10-30-home-current-revision.md).
