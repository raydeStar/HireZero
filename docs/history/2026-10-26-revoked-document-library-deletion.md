# Revoked document library deletion — 2026-10-26

This turn made verified progress on the proposal's deletion controls. Owners can now revoke access and then
separately delete a document's library source, including its earlier document save/review response copies.
The action requires the exact version, digest and its own confirmation. See
[ADR 0099](../adr/0099-revoked-document-library-deletion.md).

The UI distinguishes source deletion from retained task excerpts, results, other drafts, originals, provider
copies, downloads, open browsers, backups and database recovery files. A lost reply retains the same operation
ID across reload; checking its receipt never sends another delete. Separate revision drafts survive and still
require approval. Automatic retention remains limited to discarded, never-approved drafts.

The Chrome test also reproduced an existing list-refresh race: an older library response could make a deleted
document's title reappear after successful deletion. The source text stayed deleted, but the UI was misleading.
Library requests now reject obsolete results and errors. The same delayed-response test passes with this fix.
Mobile confirmation copy was shortened and repeated state guidance removed.

## Verification

- **15 focused backend facts passed**, zero failures/skips, in `CompanyDocumentTests`. Two new facts cover exact
  owner consent, revoke-first admission, stale versions/digests, cross-company refusal, tombstones after reopen,
  removal of source/index/operation-response content, replay without resurrection, blocked task dispatch, retained
  immutable packets, separate revision continuity and audit integrity. Existing owner-access coverage includes
  the new command. Unapproved deletion and automatic retention regressions remain covered.
- **1 company UI recovery check passed**.
- **1 expanded Chrome journey passed** across desktop/mobile. It covers upload, review, scoped retrieval,
  revocation, never-approved deletion, retention, reviewed-source deletion, double-click prevention, a lost
  deletion response, reload recovery, retained historical excerpts and the stale-library-response regression.
  Initial test timing was corrected to await the committed deletion before reloading, rather than treating the
  pending-operation banner as proof of commit. Desktop and mobile deletion screenshots were visually inspected.
- Core/dependency/Host and Company Tests builds passed; the final Host build passed with zero warnings/errors.
  TypeScript/Vite passed with the existing large-chunk advisory. The first Host dependency build encountered an
  internal compiler access violation in unchanged Web tools; the narrow retry and subsequent build passed.
  No SDK, package or runtime pin was changed to work around it.
- C# and frontend formatting, browser-script syntax and changed-file whitespace checks passed. More than 10 GiB
  remained free. The fixture runner waits for its own processes to exit before removing its temporary directory.

Evidence remains in ignored `test-results/document-source-backend.log`, `document-source-ui.log`,
`document-source-browser-before.log`, `document-source-browser-final.log`, `documents-browser.json`, build logs,
and `document-source-deletion-desktop.png` / `document-source-deletion-mobile.png`.

## Remaining work

This removes the library source; it does not erase historical task copies or complete companies, retire backups,
recall provider data, or claim physical media erasure. Those requirements remain open under X6. All checks used
fictional stores and providers. No user documents, live model calls, payments, deployments or outreach were used.
The complete three-phase goal remains active.
