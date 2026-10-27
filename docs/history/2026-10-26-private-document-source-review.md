# Private document source provenance and reviewed updates

Date: 2026-10-26

## Delivered

Drive import previews now issue company-bound protected source receipts. Saving records the exact source version,
dates and text digests separately from editable document text, with an explicit indication when that text was
edited. Approved retrieval carries source metadata without the opaque receipt. Plain source notes and older
imports are not retroactively treated as verified observations.

Owners can check a recorded copy against its currently allowed Drive file. The result distinguishes unchanged
text/version, metadata-only changes and changed text, while preserving the saved document. An explicit action
starts a revision with the same employee audience; unfinished drafts and pending receipts prevent replacement of
editor contents. The owner reviews corrections, saves a draft, explicitly revokes the earlier approved document
and separately approves its replacement. This source check grants no automatic approval or task authority.

[ADR 0118](../adr/0118-private-document-source-receipts.md) records the decision and its limits.

## Verification

- Checked available disk before builds: approximately 319 GiB free, above the 10 GiB floor.
- Company and integration test projects built with zero warnings/errors. Web typecheck/build passed; the existing
  chunk-size advisory remains (main JavaScript approximately 596 kB before gzip).
- Thirty-nine focused company facts passed with zero failures/skips across documents, Drive hosting and dependent
  memory context. Four new facts cover company-bound/tamper refusal, edited text, source-bearing retrieval without
  receipt leakage, explicit revision, revocation of prepared work, deletion/replay erasure and legacy wire/digest
  compatibility. The HTTP journey covers source comparison categories, no automatic mutation, neighboring-company
  refusal and a document changing during a read.
- Seven Drive adapter facts passed with fake HTTP, including bounded reads, permission/connection changes,
  unsupported/malformed files and version consistency. Source metadata and both content/copy digests are checked.
- The expanded real local HTTP/store/browser journey passed: source receipt survives reload, approved retrieval,
  unchanged comparison, simulated source-check network failure, changed source review, preserved unrelated draft,
  audience-preserving revision, refusal to approve over the old copy, explicit revocation and replacement approval,
  company isolation, connection removal, retained approved-copy access and later revocation.
- Inspected 1440px desktop and 390px mobile source-comparison screenshots. Source details, comparison outcome and
  revision controls are readable with no horizontal overflow.
- Targeted formatting checks passed for 13 C# files and four web/browser files. No schema migration, package update,
  live Google/model call or external action was required.

Private evidence: `test-results/drive-source-build.log`, `drive-source-integration-build.log`,
`drive-source-web-build.log`, `drive-source-tests.log`, `drive-source-integration-tests.log`,
`drive-source-owner-tests.log`, `drive-source-browser.log`, `drive-import-browser.json` and
`drive-source-check-{desktop,mobile}.png`.

An intermediate HTTP test decoded the new response as the adapter-only preview and rejected its added receipt;
the host now exposes an explicit typed preview contract. An intermediate browser check used the wrong title-field
label; its selector was corrected. Final runs passed. Temporary fixtures clean up their own profiles/databases
after processes exit; compact logs and screenshots remain.

## Boundaries

The prepared script-qualification source supplement now contains 47 entries against 549 previously selected
files, resulting in 565 files with no unexpected drift. No Linux transfer, image build, script qualification or
application execution activation occurred. No commit, push, deployment, publication or real account connection
was performed.

This is local fictional proof of provenance and an owner-reviewed update flow. It does not prove live Google
acceptance, automatic private-source refresh, customer usefulness, full company erasure or completion of all
three product phases. Those requirements remain open.
