# Accepted meeting decisions become reviewed organizational memory

Date: 2026-10-26

## Result

Closed meetings now offer owners **Remember a decision**. The handoff preserves an unfinished memory draft,
opens an editable passage with an exact minutes quote, and requires separate approval before employee retrieval.
Owners can inspect the source, retry a failed source read without losing text, and open the exact cited meeting
from the saved decision. Accepting minutes or remembering a decision creates no follow-up work.

[ADR 0117](../adr/0117-accepted-minutes-as-reviewed-memory.md) records source identity, approval and compatibility
decisions. The core remains role-agnostic; memory commands retain existing owner authorization and company scoping.

## Verification

- Checked available disk before the build: approximately 319 GiB free, above the 10 GiB floor.
- Company test project built with zero warnings/errors. Web typecheck and production build passed; the existing
  bundle-size advisory remains (main JavaScript approximately 592 kB before gzip).
- Forty-four focused facts passed across `CompanyMemoryTests`, `CompanyMemoryAnalysisTests` and
  `CompanyOrchestrationNoticeTests`, with zero errors/failures/skips. Five new facts cover accepted-only exact
  citations, separate approval, cross-company refusal, audience isolation, prepared-work revocation, changed-source
  fences, draft-deletion replay erasure and legacy digest/wire compatibility.
- The real local HTTP/store/browser inbox journey passed through proposed discussion, separately accepted minutes,
  an unfinished-memory draft, the explicit decision handoff, edited-draft reload, simulated source-read failure,
  explicit retry, saved-draft exclusion, separate approval, cited retrieval and return to the exact meeting.
  No follow-up tasks were created and no live emails or model requests were sent.
- The existing memory/result-feedback browser regression also passed, covering lost-save receipt recovery,
  replacement approval, review-date expiry, result citations, feedback draft preservation and source revocation.
  Its source-warning check prompted a small clarity fix: work-result and meeting-source exclusions now identify
  the actual source type. The 44 focused server facts passed again after that change.
- Inspected draft and approved-review screenshots at 1440px desktop and 390px mobile. Text and controls remain
  readable, the approved source is visible, and neither viewport has horizontal overflow.
- Targeted Csharpier and Prettier checks passed. Repository diff whitespace check passed (existing line-ending
  normalization notices remain).

Private local evidence is retained under `test-results/`: `meeting-memory-build.log`,
`meeting-memory-web-build.log`, `meeting-memory-tests.log`, `meeting-memory-browser.log`,
`meeting-memory-browser.json`, `meeting-memory-existing-browser.log`, `inbox-browser.json` and
`meeting-memory-{draft,review}-{desktop,mobile}.png`.
Two intermediate browser checks failed because the new test selected the rationale instead of the quote and
expected the injected exception instead of the API's user-facing error. Both test expectations were corrected;
the final complete run passed. These failures did not require application changes.

## Runtime and delivery boundary

The pending script-qualification supplement was refreshed locally: 34 entries against 549 previously selected
source files, resulting in 562 source files, with no unexpected baseline drift. This is preparation only; no Linux
transfer, image build, script qualification, runtime activation or execution-setting change occurred in this pass.
Temporary browser fixtures clean up their own profiles/databases after process exit; compact logs and screenshots
remain. No commit, push, deployment, external publication or customer data was involved.

This advances P2.4/P3.5 and provenance/isolation/recovery acceptance. Fictional verification does not prove actual
meeting usefulness, semantic retrieval quality, hosted provider acceptance, customer retention or willingness to
pay. The full three-phase goal remains open.
