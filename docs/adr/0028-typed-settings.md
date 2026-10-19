# 0028: Typed settings

Status: Accepted (Phase 3). The reference is [docs/SETTINGS.md](../SETTINGS.md).

## Context

The host read about twenty non-secret settings with `Environment.GetEnvironmentVariable`, spread over `Program.cs`,
the workspace and hosted sign-in. Each was checked where it was read, so one wrong value stopped startup and hid the
next. There was no settings file for a VM or a Kubernetes ConfigMap, and no single list of what could be set.
[ADR 0027](0027-secrets-from-files.md) settled secrets and left this open.

## Decision

- **One class, one name per setting.**
  - `CompanySettings` lists every host setting with its name, group and meaning, and holds the checked, typed
    values: URLs, IP addresses, flags and lists.
  - The host registers it as `IOptions<CompanySettings>`.
  - `Program.cs`, the workspace and hosted mode read it instead of the environment.
- **Two sources, same names.**
  - A value comes from the environment variable of that name, or from the JSON file named by
    `HIREZERO_SETTINGS_FILE`, whose keys are those names. The environment wins.
  - Keeping the existing `HIREZERO_*` names means launchers, the container and deployment docs keep working.
  - ASP.NET Core's own configuration sources still do not choose listeners, as before.
- **Checked together.** All values are parsed before anything starts, and every problem is reported in one error,
  naming the setting and its source. Hosted mode names all of its missing required settings at once.
- **The file stays safe.** A settings file that holds a secret (any name in `CompanySecrets.Known`), or a name this
  version does not know, is refused. This prevents committing a secret by accident and silently ignoring a typo.
- **Documented by test.** A test fails if any setting is missing from `docs/SETTINGS.md`.
- **Tool locations stay local.** Developer-machine tool locations read by integrations (`HIREZERO_AZ`, the Codex and
  Claude commands, `HIREZERO_WIKI_URL`) and the standard `OTEL_*` variables stay where they are used. They are listed
  in the reference.

## Consequences

An operator can configure a host with one reviewed JSON file plus mounted secrets. A wrong setup fails fast with the
full list of problems. Each new host setting is added in one place, and its documentation is enforced. Settings are
read at startup, so changing one means a restart, unlike the secrets that are read at use.
