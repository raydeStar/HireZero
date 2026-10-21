# 0045: Offline packages for coding runs

Status: Accepted. Extends 0014.

## Context

A live coding run (0014) works in a sandbox with network off unless the grant allows it. The run's home folders point
at an empty scratch profile, so NuGet's global package folder is empty there, and restoring packages needs the
network. On the first real run, the Developer could write a change and its tests, but it could not build or run them.
The owner ran the checks instead.

Turning the network on for every run would let anything that runs in the sandbox reach the internet. That is the
wrong default for checking one's own work.

## Decision

- **An owner-prepared package cache** is named with `HIREZERO_COMPANY_CODING_PACKAGES`
  (`Start-CompanyLocal.ps1 -CodingPackages`, `start-company-local.sh --coding-packages`). The owner fills it once with
  the repository's locked packages, for example `NUGET_PACKAGES=<cache> dotnet restore --locked-mode`.
- The cache is **one more read-only folder**, held to the same rules as `-CodingRead`: an existing, specific folder
  that contains neither the user profile nor the sign-in home or scratch, has no links, and does not overlap the
  checkout.
- In the run, it is a **NuGet fallback folder** (`RestoreFallbackFolders`). Restore takes every package from it and
  writes nothing there. The vulnerability audit is turned off (`NuGetAudit=false`): it needs the network, and its
  warning would fail the warnings-as-errors build. The lock files already pin every package.
- **Network stays off.** A simulated sandbox (an empty profile, and a package source that does not exist) built the
  solution with no warnings and passed its tests without downloading anything.

## Consequences

- When the lock files change, the owner refills the cache. A missing package fails the restore clearly; it is never
  downloaded.
- The owner still reviews every diff and runs the checks before any push. The run's own test results are evidence,
  not approval.
- Web dependencies (npm) are not covered. Frontend checks still run outside the sandbox.
