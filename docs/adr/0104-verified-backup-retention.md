# ADR 0104: Verify backup ownership and contents before retention

Status: accepted, 2026-10-26. Updates ADR 0044; supports the X6 data-retention boundary.

## Context

The local backup command selected old directories by name prefix alone. A shared destination could therefore
lose another data root's backups, a modified snapshot or an unrelated folder. A failed copy also removed a
pre-existing `.partial` directory. Directory traversal followed links in source trees, allowing a nominally
scoped backup to include material outside the selected root. These were unsafe foundations for data retirement.

## Decision

Serialize cooperating backup runs to the same destination with an exclusive file lease. Refuse an existing
partial path rather than adopting it. Track files newly created by this attempt; failure cleanup removes only
those files and empty directories, never a recursive pre-existing tree. Source and destination ancestors,
source entries and retention candidates must not contain symbolic links, junctions or other reparse points.
The source's root `manifest.json` is reserved: refuse a collision instead of overwriting an original file.

Retention counts only complete, verified snapshots of the exact selected absolute data root. Require the exact
timestamped directory format, matching creation time, a bounded manifest, unique recognized properties, safe
relative file paths, matching lengths and SHA-256 hashes, and no unlisted files or directories. New manifests
declare schema 1; older manifests without a schema field remain eligible only when all other checks pass.
Malformed, modified, linked, foreign-root and unknown folders are left for manual review. Their presence may
leave more than `keep` folders on disk; it does not justify deleting more verified snapshots.

Recheck a deletion candidate immediately before cleanup. Delete the verified file list and empty directories,
without recursive deletion. Remove the manifest last so an interrupted cleanup retains its inventory. A cleanup
failure does not invalidate the newly completed backup: return the remaining folder in `NeedsReview` and report
that manual review is required. Such a folder can have partially removed data; it is not reported as an intact
preserved backup or as successfully removed. The CLI distinguishes completed removal from manual review.

Checksums establish consistency, not authenticity against a hostile local writer. The data root and backup
destination remain trusted local storage; this is not protection against another process actively replacing
filesystem objects between checks. Verification adds read I/O, deliberately trading retention speed for evidence
before deletion. SQLite copies still use the online backup API and pass `integrity_check`; the collection of
separate databases and files is not an atomic multi-file snapshot.

## Scope and evidence

This change affects operator backup/retention, not customer records or whole-company erasure. Full retirement
still needs runtime settlement, company bootstrap suppression, scoped credential removal and treatment of
historical/remote copies. No real backup is removed during development.

Focused tests reproduce the old destructive behavior, then cover source-root isolation, modified/unlisted data,
manifest traversal/duplicates, existing partials, destination ownership, failed-copy cleanup, legacy manifests,
Windows locked-file cleanup and actual directory links. Results belong in the dated verification receipt.
