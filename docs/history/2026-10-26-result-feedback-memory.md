# Feedback from reviewed results into future work

The all-three-phase goal remains active. This increment advances the owner-feedback portion of P3.5 and the
learning loop in the proposal; it does not complete advanced memory maintenance or establish real output quality.
The decision is [ADR 0088](../adr/0088-result-feedback-memory.md).

## Implemented

- An owner action beside an approved result opens a company-scoped feedback source in Memory. Starting the
  draft selects the result, its opening excerpt and originating employee. The owner writes the actual guidance,
  can adjust the quote and audience, saves the draft and separately reviews the exact memory before approval.
- An existing unfinished memory draft blocks replacement and retains its text. Feedback drafts preserve their
  source, quote and audience across reloads. Source-read failures retain the draft, disable saving the unverified
  citation and offer an explicit retry. Saved citations remain visible while their options load.
- The memory source catalog and direct source read now apply the same authority checks as citation saving.
  Revoked results and work with canceled tasks or revoked supporting context are not offered as valid sources.
- Approved feedback enters bounded retrieval for its selected audience and the next prepared employee request.
  Drafts remain excluded; source or memory revocation fences future dispatch. Existing expiry, revisions and
  current-context checks remain in force. No model-generated feedback, automatic approval or silent fact changes.
- Shared API errors now distinguish failed reads from uncertain writes. GET connection failures, malformed
  responses and timeouts offer a read retry. Writes retain unknown-outcome messaging and receipt recovery;
  neither path replays automatically.

## Verification

- All ten focused memory facts passed. The added result-feedback fact verifies company isolation, exclusion
  before approval, employee-only retrieval, inclusion in the next prepared request with exact source provenance,
  and refusal to dispatch that request after memory revocation. Existing tests now also check that revoked
  artifacts and revoked supporting context disappear from memory source discovery and cannot be read as sources.
- Company UI checks passed, including six new transport cases covering GET and POST under lost connections,
  malformed responses and timeouts. Existing company-scoped lost-write receipt and duplicate guards still pass.
- The expanded memory Chrome journey passed. It preserves an unrelated unfinished draft, starts feedback from
  the result, preserves the feedback draft after reload, recovers a simulated citation-read outage, verifies
  explicit approval and employee audience, and excludes the feedback after its source is revoked. The new
  capture, recovery and approval steps run at 390px; desktop and mobile screenshots were inspected without
  horizontal overflow. Existing memory revision, expiry, renewal and lost-save receipt steps also pass.
- The recurring-script Chrome regression passed against the shared result-screen and API changes.
- Core, host and test-project builds, frontend typecheck/build, formatting and whitespace checks passed.
  The existing frontend bundle-size warning remains. An initial browser assertion ran before citation options
  loaded, exposing the blank-selection issue. A later fixture revocation request used the wrong transport field;
  correcting it completed the source-revocation check without relaxing application behavior.

Receipts and compact logs are under ignored `test-results/memory-feedback-*` and `test-results/memory-browser.json`.
The existing memory screenshot is `test-results/memory-mobile.png`. Fixture data and providers are fictional.
Temporary fixture processes exit before their owned profiles/data are removed. Free disk space remained above
312 GiB. No live model calls, payments, container launches, runtime changes, commits, publications or deployments.

Semantic conflict detection, model-generated memory proposals, memory retention/deletion and observed customer
usefulness remain open, alongside the other uncompleted requirements in the delivery ledger.
