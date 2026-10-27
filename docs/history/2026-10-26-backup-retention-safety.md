# Verified backup retention — 2026-10-26

The previous goal turn made verified progress on unapproved memory deletion. This continuation reread the
three-phase proposal and traced whole-company retirement before adding another destructive operation.

## Findings and changes

The audit found that company SQL history, per-company integration directories, model-key envelopes and detached
runtime cleanup records have different lifecycles. `CompanyStore` also recreates initial membership when a
company has no members. Removing database rows alone would therefore neither establish complete erasure nor
prevent accidental bootstrap. Whole-company retirement remains unfinished; its design must settle runtime and
external outcomes, suppress recreation, remove scoped credentials and account for historical/backup copies.

The existing backup path had two reproduced destructive bugs: prefix-only retention deleted other-root,
modified and unrelated folders, and copy-failure cleanup deleted a pre-existing partial directory. Those are
fixed under [ADR 0104](../adr/0104-verified-backup-retention.md):

- An exclusive destination lease prevents cooperating backup runs from sharing the same partial output.
  Existing partials are refused. Failed attempts clean only their newly created files and empty directories.
- Source/destination ancestor checks and explicit tree traversal refuse symbolic links, junctions and reparse
  points. A reserved source manifest collision is refused rather than silently overwritten.
- Retention verifies exact source identity, timestamp, bounded manifest structure, safe unique file paths,
  sizes, hashes and the complete directory contents. Old manifests remain eligible when they pass the same
  checks. Unknown, modified, linked and other-root folders do not consume the retained-backup count.
- Cleanup uses explicit files and empty directories, rechecks candidates, removes the manifest last and reports
  incomplete removal for manual review. The CLI distinguishes a completed backup from folders requiring review.
- SQLite connection strings now use the structured builder, preserving literal path characters.

## Verification

- Before the fix, the ordinary backup test passed while both new destructive regression checks failed with
  fictional data. An earlier restricted-shell attempt also hit directory-rename restrictions; the permitted
  local run isolated the two actual defects from that environment failure.
- **Eight focused backend facts pass**: ordinary SQLite backup and retention, existing partial preservation,
  foreign/modified/unverified folders, duplicate/traversal manifests and unlisted files, destination ownership,
  reserved filenames, failed-copy cleanup, old manifests, locked-file retention failure, and actual Windows
  junctions at the source, destination ancestor and retention candidate.
- The **real PowerShell backup CLI passed** against a fresh fictional workspace fixture. It created the expected
  manifest, retained unrelated notes and printed the manual-review notice. The fixture was removed only after
  the command exited, with its absolute path checked against the intended test directory.
- Host/test builds passed with zero warnings/errors. Five changed C# files passed formatting; tracked whitespace
  checks passed. No frontend code changed, so browser journeys were not rerun for this operator-only fix.
- About 350 GiB was free before the build. All test processes exited; no real backup, customer record, credential,
  runtime engine, model, payment, deployment or publication was changed.

Ignored evidence: `test-results/backup-safety-before-local.log`, `backup-safety-build-final.log`,
`backup-safety-tests.log`, and `backup-safety-cli.log`. This verifies scoped backup retention; it is not whole-company
erasure, filesystem containment against a hostile local writer, production qualification or customer acceptance.
The full three-phase goal remains active. Runtime-host approval, live provider/payment validation and actual
customer observations are still outstanding.
