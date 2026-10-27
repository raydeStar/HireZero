# Reviewed Word text import

The all-three-phase goal remains active. This increment advances private document ingestion in P1.6 and the
shared context used by persistent employees. It does not complete richer document support or real customer
acceptance. The decision is [ADR 0089](../adr/0089-reviewed-word-text-import.md).

## Implemented

- Word `.docx` body text and tables enter the existing editable document draft flow. The original binary is
  never stored. Saving remains separate from approval, and retrieval uses the existing approved-text digest,
  company isolation, selected audience and source-revocation checks.
- The integration resolves the main package part, bounds compressed/expanded sizes, XML size/depth, part count
  and extracted UTF-8 bytes, and refuses unsupported or ambiguous input. It does not invoke Office, extract
  filesystem paths, execute instructions, follow hyperlinks or fetch external entities.
- Failed or stale extraction responses preserve the owner's current editor. Reload restores imported text,
  source filename and audience. Saving is disabled while extraction is pending. Word request failures explain
  that the draft is unchanged and require explicit file selection to retry.
- Mobile inspection exposed an unnecessarily tall omission notice and a reset file picker that looked as if
  nothing had been imported. The editor now names the source file, keeps a concise limitation notice visible
  and offers expandable extraction details. The saved review repeats the same limitations before approval.

## Verification

- Seven focused integration facts pass: paragraph/table extraction, omitted content, custom main parts and
  Strict namespaces, unsafe/ambiguous ZIP names, external main relationships, macros, tracked changes,
  embedded objects/imports, DTD/entities, deep XML, independent size limits, malformed/empty/wrong-format input.
- Fifteen company facts pass, including two new Word facts and the existing document/retention facts. They
  verify stateless extraction, no-store responses, invalid input/request-size rejection, current owner and
  company authority, persistence of source labels, separate approval, employee-only retrieval and revocation.
- The Word Chrome journey passes with fictional in-memory packages. It covers invalid input, a simulated
  connection failure, an owner edit made while extraction is pending, unsaved reload recovery, mobile save
  and explicit approval, lost-save receipt recovery without duplication, scoped retrieval and revocation.
- The existing text/Markdown Chrome journey also passes, including context use, lost-save recovery, draft
  deletion and retention. Company UI checks pass. The fixtures wait for their owned processes to exit before
  removing their verified temporary profiles/data.
- Frontend typecheck/build, core/integration/host/test builds, targeted formatting and whitespace checks pass.
  The existing frontend bundle-size warning remains. Restore only adds the local integration project:
  all 22 host, 39 company-test and 24 integration-test non-project lock entries match the prior snapshots;
  the new integration has no package dependencies or runtime-pin change.

Receipts and compact logs are under ignored `test-results/word-*`, with `word-import-browser.json`, desktop,
mobile-editor and mobile-review screenshots. The existing regression receipt is `documents-browser.json`.
Free disk space was above 350 GiB before the build. All imported content and providers are fictional. No live
model calls, payment actions, container launches, commits, pushes, publications or deployments were performed.

Only body text and tables are imported. PDF/OCR, other document formats, private connector ingestion, full
format fidelity, complete company erasure and real document/customer usefulness remain unverified or unimplemented.
