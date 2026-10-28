# Preserve completed model answers

Inspection during the weekly-plan quality investigation found a separate transport defect. The native text
connection appended every assistant delta to one buffer and ignored completed snapshots once that buffer
contained text. Fictional protocol tests reproduced progress text mixed into the result, a stale fragment
replacing the completed answer, and unfinished text being accepted as a draft.

There is no evidence that this defect caused the repetitive alterations posts. Their writing-quality finding
remains open; this fix must not be counted as an improvement in raw model usefulness.

## Change

The connection now tracks assistant messages by item identity, uses each completed snapshot as the authoritative
text, excludes commentary, and preserves stable partial-answer paragraphs and legacy messages without a phase.
An incomplete or commentary-only result is withheld. Observed commentary and incomplete deltas still count
toward the existing character stop; a completed snapshot does not count the same characters twice. Known usage
is retained on failure, and no request is replayed.

This follows the [app-server item contract](https://learn.chatgpt.com/docs/app-server#items), checked against
schemas generated locally from the existing pinned runtime, version `0.165.0-alpha.1.2`. Its assistant-message
phase is optional and permits `commentary`, `partial_answer` and `final_answer`; deltas require an item ID.
The runtime pin, model route, effort, allowance, instructions and scheduling policy did not change.

## Evidence

- Before the fix, the mixed-phase fixture returned `CheckingFinal ca` instead of the two completed answer
  paragraphs. A legacy message returned its obsolete streamed fragment. Unfinished and commentary-only
  messages were incorrectly accepted.
- Six focused xUnit facts now cover completed-answer fidelity, legacy phase compatibility, stable partial
  answers, incomplete output, commentary-only output and character accounting. Native-start counts remain
  one in every fixture.
- Eleven tests passed: the six new facts, two existing effort facts, and the existing native connection,
  measured queue/accounting and conversation check groups. Debug and Release builds had no warnings or errors.
- CSharpier and web formatting completed. No live model call was made in this cycle.
- Both idle local installations were updated after separate application and data backups. All 162 installed
  files matched the staged release in each installation; existing work, accounts, saved schedules and audit
  integrity were preserved. Only the integration assembly and its symbols changed among server files.

The raw evaluation ledger remains at eighteen of 31 authorized requests, with thirteen remaining. Reported
input plus output remains 121,166 tokens; actual subscription-credit charges are unavailable. Earlier raw
artifacts are unchanged, and the fresh organising-business usefulness assessment is still pending.
