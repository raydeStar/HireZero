# 0026: Container, releases and advisories

Status: Accepted (Phase 3).

## Context

HireZero ran from a checkout, through a PowerShell launcher that found a debug build. A client running it needs:
- an image to host;
- a download that runs on their OS;
- a way to know a download is the one published;
- a list of what is inside it;
- assurance that known dependency vulnerabilities have been looked at.

The project's rule against GitHub Actions in routine checks also stands.

## Decision

- **Container image for hosted mode.** The `Dockerfile` is a three-stage build: the web UI on Node, the host on the
  .NET SDK, then the ASP.NET runtime.
  - Base images are pinned by digest, and the restore runs in locked mode.
  - The image runs as the runtime's non-root user, on port 8080, with data in `/data`.
  - It starts `--company-hosted`, so it needs OIDC and either a trusted proxy (`HIREZERO_TRUSTED_PROXY`) or an
    HTTPS certificate, as [ADR 0020](0020-members-roles-and-sign-in.md) requires.
  - Company data stays in `/data`, or in PostgreSQL ([ADR 0025](0025-one-schema-two-databases.md)).
  - Live coding needs the pinned agent runtime on the host machine, so it is not part of the image.
- **A portable release.**
  - `tools/Build-Release.ps1` publishes one framework-dependent host (any OS with the .NET 10 runtime) into `app/`.
    The zip also holds both launchers, the README and the license.
  - `tools/start-company-local.sh` is the POSIX launcher. It and `Start-CompanyLocal.ps1` run either a release's
    `app/` or a checkout's debug build.
  - `-Image` also saves the container image.
  - A release is built from a clean commit with at least 10 GiB free.
- **SBOM from the lockfiles.** A CycloneDX 1.5 file lists every resolved NuGet package of the host, direct and
  transitive, and every npm package bundled into the UI, each with its SHA-512 from the lockfile. Build-only tools
  are left out. It is generated from the committed lockfiles, so it describes exactly what was restored.
- **Signed checksums.**
  - `SHA256SUMS` covers every release file, and an ECDSA P-256 signature (DER, SHA-256) covers `SHA256SUMS`.
  - The signing key is created by `New-ReleaseKey`, kept outside the repository and never committed. The public
    key is published beside the releases.
  - `tools/Test-Release.ps1` rejects a changed, added or re-signed file. `sha256sum -c` and `openssl dgst -verify`
    check the same release without PowerShell.
- **Advisories are triaged, not ignored.**
  - `tools/Check-Advisories.ps1` lists NuGet advisories (`dotnet list package --vulnerable --include-transitive`)
    and npm ones (`npm audit`).
  - It fails on any advisory without a recorded decision in `docs/security/advisories.json`.
  - It queries the package sources, so it runs before every release rather than in routine checks.
  - The first run found GHSA-68fv-2mgg-jv7q in `source-map-js`, a build-only dependency, and the patched version
    was locked.
- **CI is a script, not a service.** `tools/Invoke-Checks.ps1` runs every offline check (formatting, types, build,
  .NET tests, frontend tests, both launchers, release helpers, and with `-Browser` the browser smokes). Any runner
  can call it without workflows that make live calls.

## Consequences

A client can host the image, or run the zip on Windows, macOS or Linux. They can verify both against a published key
and read what is inside. Releases are signed with a project key, not a platform code-signing certificate, so OS
publisher prompts still apply. Authenticode or notarization can be added later on top of the same checksums.
Updating a base-image digest, the SBOM's inputs or the triage record is a reviewed change.
