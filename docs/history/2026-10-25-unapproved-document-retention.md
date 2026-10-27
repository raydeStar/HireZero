# Unapproved document deletion and retention — 2026-10-25

Scope: a first bounded part of X6 in the [full product delivery ledger](../CONTEXT_FIRST_DELIVERY.md).
Decision: [ADR 0070](../adr/0070-unapproved-document-erasure-and-retention.md).

## Delivered locally

- Owners can delete an exact never-approved document draft, with separate confirmation. Discard keeps saved
  text until explicit deletion or retention expiry; it neither erases the text nor grants approval.
- Deletion removes the title, text, filename, audience and replacement reference from the active document and
  all historical document operation results. A minimal tombstone and operation fingerprints prevent stale saves
  from recreating it. Retrieval indexes are cleared, the library hides deleted records and document storage is freed.
- Optional retention deletes discarded drafts after 7, 30 or 90 days from discard. The default keeps existing
  drafts. Applying a policy immediately processes expired discarded drafts; the scheduler checks every minute
  while running. Active drafts and previously approved documents are excluded.
- Lost deletion replies can be reconciled after reload without repeating the action. The current editor's copy
  is cleared on confirmed deletion. Old save receipts return the tombstone. A recovered policy receipt reloads
  the current policy, including when the receipt describes a superseded setting.
- Mobile review exposed an ambiguous saved-settings presentation. The final view states the current rule,
  opens an edit form only when requested, and keeps the detailed deletion boundary available in an expandable explanation.

## Verification

- Free disk before the build and at completion: approximately 317 GiB.
- Company test build: zero warnings/errors, 6.58 seconds. Integration test build: zero warnings/errors,
  4.30 seconds. An initial xUnit analyzer complaint was fixed before tests ran successfully.
- `CompanyDocumentTests`: **13 passed**, 2.693 seconds. Coverage includes historical text erasure, operation
  reconciliation and replay, exact version/digest/consent, owner and tenant isolation, approval/deletion race,
  quota recovery, retention cutoff/disable/re-enable, protection of approved packets and migration 22.
- Nine earlier company migration regression facts: **9 passed**, 1.693 seconds. The sandbox migration 21
  regression: **1 passed**, 0.340 seconds. All historical schema checksums were preserved. Total: **23 passing facts**.
- Expanded `documents-browser-smoke.mjs`: passed against the real local HTTP routes/store and isolated Chrome,
  using fictional data. It retains import/retrieval/approval/revocation checks and adds lost deletion reply,
  reload recovery, erasure of an earlier save receipt, current-editor cleanup, explicit retention consent,
  lost policy reply, saved-rule reload and neighboring-company policy isolation.
- Inspected 1440-pixel desktop and 390-pixel mobile screenshots. No horizontal overflow or browser exceptions.
  The final saved-rule view has no unfinished edit form.
- TypeScript/Vite, existing company UI recovery checks, CSharpier on 17 affected C# files, Prettier on the three
  frontend/browser files and scoped whitespace checks passed. Final assets: `index-g13_CY6L.js`, `index-DUWlZy7i.css`.

Ignored local evidence: `test-results/document-retention-build.log`, `document-retention-tests.log`,
`document-retention-migrations.log`, `document-retention-integration-build.log`,
`document-retention-sandbox-migration.log`, `document-retention-web-build.log`,
`document-retention-browser.log`, `document-retention-ui-tests.log`, `documents-browser.json`,
`documents-mobile.png` and `documents-retention-desktop.png`. The browser harness disposed its fixture host and
temporary browser/data root. No live model calls, external requests, real customer deletion or deployment occurred.

## Remaining acceptance

This is logical erasure of never-approved document content from the active store and recovery results. IDs,
hashes, deletion times and audit events remain. Existing backups/recovery files, imported originals and copies
in other browsers are not erased. Restoring an old backup can restore old content, so backup expiry and restore
handling remain operator responsibilities. Previously approved material, task snapshots, complete companies
and connection files still need their own deletion lifecycle. X6 and the full three-phase goal remain incomplete.
