# Selected Google Drive documents into approved company context

The all-three-phase goal remains active. This increment advances private knowledge ingestion, scoped connections
and the integration ecosystem in P1.6, P2.5 and P3.4. It does not establish live Google acceptance or complete those
requirements. The decision is [ADR 0090](../adr/0090-selected-drive-document-copies.md).

## Implemented

- A separate Drive connection has a dedicated service-account secret and an exact allowlist of up to 20 files.
  Local secrets use the existing encrypted company store; hosted connections use the company's credential
  directory. Setup/status inspection does not read private files. Analytics sign-in stays separate.
- Owners can preview one Google Doc, Word document, UTF-8 text or Markdown file in the document editor.
  Fixed endpoints, no redirects/folder traversal/shortcut following, bounded responses, permission/type checks,
  pre/post-read version checks and connection fingerprints constrain the read. Revocation during content
  download prevents the subsequent metadata request. Current company-owner authority is checked before delivery.
- The preview contains an editable source/version/date note and the extracted text. It saves no company document
  and makes no model call. A separate draft save and exact approval reuse existing employee-audience retrieval,
  tenant isolation, version digests and source revocation. The source note is editable, not signed provenance.
- The UI describes independent text copies explicitly: changes or disconnection in Drive do not erase or update
  approved copies. Removing keys or file IDs stops new reads; Documents manages copies separately.
- Failed previews and concurrent edits retain the current editor. Imported text, source filename and audience
  survive reloads. Connection setup, file selection, review, approval and disconnection are available in the UI.

## Verification

- **44 focused backend facts pass:** 20 integration facts (Drive, existing Google Analytics/sign-in and Word
  parsing) and 24 company facts (Drive host flow, connection isolation/setup and document/retention behavior).
- The seven new provider facts cover read-only scope without impersonation, exact file IDs, no private reads
  during setup, versioned Google Doc text, Word and UTF-8 handling, unsupported/forbidden/trashed files, redirects,
  declared and streaming size limits, cancellation without retry, changed sources and removed keys/file IDs.
- The new HTTP fact verifies current-owner and company boundaries, no-store/stateless previews, separate document
  approval, withheld results after settings or role changes, and the documented independence of approved copies.
  Existing encryption-purpose, credential-delivery and private connection isolation checks remain passing.
- **Three Chrome journeys pass:** Drive setup-to-approved-copy, Word import regression, and existing integration
  isolation/Google consent/sign-out. Drive checks include a changed-source refusal, simulated failed read, an edit
  made while a response waits, reload recovery, mobile save/approval, a blank neighboring company and disconnection
  followed by explicit copy revocation. Desktop and 390px mobile screenshots were inspected; no horizontal overflow.
- Company UI checks, frontend typecheck/build, Google integration/host/test builds, targeted formatting and
  whitespace checks pass. The existing frontend bundle-size warning remains. Package graph restore added only
  the reference to the local Word importer: 7 Google, 22 host, 39 company-test and 24 integration-test non-project
  lock entries match their before snapshots. No new package or runtime pin changes.
- One test build initially used a lambda parameter named `_` where `out _` was intended; renaming the parameter
  fixed the fixture. Initial browser controls omitted same-origin headers and were correctly refused; the fixture
  now uses authenticated browser requests and asserts their success. A key-clear assertion now waits for settings
  refresh and reports a boolean instead of echoing the generated fictional key. Application checks were not relaxed.

Compact logs and receipts are in ignored `test-results/drive-*`, including `drive-import-browser.json` and desktop,
mobile-editor and mobile-review PNGs. Fixture processes exit before their verified temporary roots are removed.
Free disk space exceeded 351 GiB at the initial check. All accounts, keys, document content and remote responses
were fictional; no real Google, model or payment call, container launch, commit, push or deployment occurred.

Live service-account authorization, hosted credential delivery, simpler delegated OAuth/Picker onboarding,
automatic private-source refresh, signed provenance, further connectors and real customer usefulness remain open.
