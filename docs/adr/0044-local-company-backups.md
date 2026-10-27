# 0044: Local company backups and staying running

Status: Accepted.

Retention safety is tightened by [ADR 0104](0104-verified-backup-retention.md): only verified snapshots of the
selected data root are pruned. Links, existing partial folders, modified snapshots and unknown/other-root folders
are preserved or refused; uncertain cleanup is reported for manual review.

## Context

A company-local install keeps everything in one data root: the SQLite database, the Data Protection keys,
app-entered secrets, integration settings and the audit key. Nothing backed it up. A plain file copy of a running
SQLite database can catch a write halfway through. The company also ran only while someone started it, so a reboot
took it offline.

## Decision

- **`HireZero.Host --company-backup <data root> <destination> [keep]`** takes a backup and exits, starting nothing:
  - each `.sqlite` file is copied through SQLite's online backup, so it is consistent while the company runs, and
    the copy must pass `PRAGMA integrity_check`;
  - every other file is copied as it is, except locks and SQLite side files;
  - a `manifest.json` lists each file with its size and SHA-256 digest;
  - the backup is written to a `.partial` folder and renamed only when complete. Only then are backups beyond the
    newest `keep` (default 14) removed;
  - the destination must lie outside the data root.
- **Nothing leaves the machine.** The keys stay DPAPI-protected, so only the same Windows user can restore the
  secrets. Copying backups elsewhere is the owner's choice.
- **`tools/Backup-CompanyLocal.ps1`** runs it. **`tools/Register-CompanyLocalTasks.ps1`** registers two scheduled
  tasks for the current user, with no administrator rights:
  - "HireZero company" starts the company at sign-in and restarts it if it stops;
  - "HireZero backup" runs nightly, or at the next chance if the machine was off.

  The tasks carry only non-secret environment (the script refuses names that look like keys). Output goes to
  `<install>/logs`.
- **`tools/Update-CompanyLocal.ps1`** stops the scheduled task before swapping in a new build and starts it again
  afterwards, so the task's restart never races the swap.

## Consequences

- Restoring means stopping the company and copying one backup's files into an empty data root.
- Backups share the machine's fate. Off-machine copies are a separate, owner-approved step.
