# 0023: Trusted PostgreSQL in live coding runs

Status: Accepted (follows [ADR 0019](0019-retiring-the-legacy-profiles.md)).

## Context

The `postgres-setup` action installs HireZero's trusted PostgreSQL runner and its pinned binaries under
`<data root>/Runtime/trusted-postgres`. ADR 0019 deferred the other half of the legacy feature: letting a coding run
use that runner for a repository whose tests need PostgreSQL. In the legacy profile, the owner saved a database
profile per repository. A run got the profile file through `HIREZERO_POSTGRES_PROFILE` and the runner's usage line
in its brief.

## Decision

- **Profiles are owner-approved configuration.** A profile is a database name plus environment templates over
  `{host} {port} {user} {password} {database} {url} {dataDir}`. Each one is stored per repository (`owner/name`, any
  case) by `Integrations.Node` in company storage, under the `trusted-postgres-profiles` ModuleStorage owner
  ([ADR 0018](0018-module-storage-migrations.md)).
  - `postgres.profile` (Read) reads a profile and `postgres.save-profile` (Write) saves or removes one. Both validate
    the profile.
  - The owner sets a profile with the Developer's `postgres-profile` action. Its reading names the one change, and
    approving the publication saves it once ([ADR 0017](0017-publishing-a-step-from-an-action.md)). A database of
    `off` removes the profile.
- **A run gets the toolchain only when both are ready.** The Developer adds the toolchain to a live coding run when
  the run's repository has a profile and `postgres.status` reports the binaries installed and the runner intact. The
  repository is the pull request's for `pr-fix`; for other work it is read from the checkout's origin. The run then
  gets:
  - the toolchain folder as a read-only root;
  - the profile, written as a file that `HIREZERO_POSTGRES_PROFILE` names;
  - the runner's usage line in its prompt.
  Without the profile, the install or the tools, the run is unchanged.
- **The coding contract stays product-neutral.** `CodingRunRequest` gains `ReadRoots` (extra read-only folders) and
  `InputFiles` (variable name to file text). The pinned Codex runner applies the owner's toolchain-folder rules to
  each read root, and refuses one that overlaps the checkout. It writes input files to a folder beside the run's
  scratch that the sandbox can read but not change. Each name must start with `HIREZERO_`; there are at most four
  files, each up to 16 KB. The folder is deleted with the scratch. The synthetic runner refuses both fields rather
  than ignore them.

## Consequences

Core still names no product, roles start no processes, and integrations know no role: the role decides what a run
needs, `Integrations.Node` stores profiles and `Integrations.Codex` enforces the sandbox. The execution grant's
digest does not cover the profile or the toolchain. Both are owner-approved separately, and they add only read
access, so the grant's write, network and model bounds are unchanged. Literal values of secret-named profile
variables are registered for masking, as before. Proof that a test command ran through the runner, which the legacy
fix loop checked, is not ported. The company path reviews the diff instead.
