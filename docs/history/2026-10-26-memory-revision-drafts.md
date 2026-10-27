# Owner-reviewed memory revisions — 2026-10-26

Made verified local progress on Phase 3 memory maintenance under
[ADR 0094](../adr/0094-owner-reviewed-memory-revisions.md). All three product phases remain in scope and incomplete.

## Delivered

- Memory reports can include supported suggested wording for a cited record. The owner opens a separate saved
  draft, edits it and explicitly approves replacement. The earlier record remains active until that approval.
- Suggestions retain their original type, audience, work citation and exact report provenance. All selected
  records must fit the target audience; narrower private context cannot generate broader shared guidance.
- Changed sources invalidate unsaved applications and saved drafts. Discard remains possible. Approved revisions
  become owner-authoritative and remain usable after their source report becomes historical.
- Reopening a suggestion reuses the existing draft. A lost response recovers its saved receipt without another
  write or model request. Unsaved notes and edited suggestions survive reload; unfinished editors are not replaced.
- Added a clear discard-unsaved-edits action. Historical reports and ordinary memory revisions remain supported.

## Verification

- 30 focused xUnit facts/cases pass across memory analysis and organizational memory. Six new facts cover
  edited suggestion acceptance, old-record preservation, retrieval exclusion before approval, restart persistence,
  repeated opens, stale supporting records, provenance removal refusal, exact authorization, owner/company
  boundaries, invalid suggestions, audience containment and serialization compatibility.
- The extended real local HTTP/Chrome journey passes with fictional model replies. It checks an unfinished note,
  an exact report with quotes, lost analysis and draft responses, usage reconciliation without replay, edited-draft
  reload, separate acceptance, preserved history and disabled stale suggestions. It performs two fictional model
  calls, including one uncertain outcome; opening the revision performs no additional model call.
- The existing memory Chrome regression passes: ordinary replacement, date expiry, renewal, source citations,
  direct inbox links, result-linked feedback, audience selection, citation-read recovery and revocation. Both
  browser processes exit with code 0 and clean up their isolated profiles/databases after process exit.
- Desktop and 390-pixel revision-review screenshots were inspected. The proposed statement, earlier statement,
  rationale, review date and approval controls are readable; both browser journeys report no horizontal overflow.
- Core, Host and Company test builds, TypeScript/Vite and the company UI contract check pass. Vite retains its
  existing bundle-size advisory. Relevant C# and frontend sources are formatted.
- Compact ignored receipts: `test-results/memory-revisions-xunit.log`, `memory-revisions-browser.log`,
  `memory-revisions-regression.log`, `memory-analysis-browser.json`, `memory-browser.json`,
  `memory-revision-desktop.png` and `memory-revision-mobile.png`.

## Remaining acceptance

Fictional suggestions prove the workflow, authority and recovery mechanics. They do not prove real model
conflict resolution, suggestion quality, customer usefulness or retention. Production provider/payment acceptance,
commercial terms and isolated-runtime qualification remain separate. No live provider call, payment, runtime
upgrade, container launch, push or deployment was performed.
