# 0143 - Independent startup tasks for separate local workspaces

Status: accepted, 2026-10-27.

The local task-registration helper always used the same two Windows task names. Registering a second install
would replace the first install's startup and backup definitions, even when its data and listener were separate.

Registration and update helpers now accept an optional `TaskPrefix`, defaulting to `HireZero` for compatibility.
The startup and backup names are `<prefix> company` and `<prefix> backup`. Removal and update restart only the
selected names. Separate workspaces use distinct install directories, data roots, listeners and backup folders;
their existing per-install log folders remain separate as well.

For example, register a second published install with `-TaskPrefix 'HireZero sample'`, and supply the same prefix
when updating it with `Update-CompanyLocal.ps1 -Target <second install> -TaskPrefix 'HireZero sample' -Stop`.
Use `Register-CompanyLocalTasks.ps1 -DataRoot <second data root> -TaskPrefix 'HireZero sample' -Remove` to remove
only its startup and backup tasks. The helper still runs as the current user with limited privileges and keeps
secrets outside task definitions.

This is sign-in startup and process recovery, not a promise of availability while the computer is off, asleep or
signed out. Application schedules still need their own explicit approval. Moving a local workspace uses a
verified backup copied into a new empty data root; the original remains available for recovery.
