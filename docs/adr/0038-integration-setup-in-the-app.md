# 0038: Integration setup in the app

Status: Accepted.

## Context

The owner walkthrough (docs/history/2026-10-19-owner-walkthrough.md, finding 11) found that Google, Google Ads, Azure
DevOps, OpenRouter and the coding-agent apps could only be set up by editing JSON files in the data root, and by
delivering secrets as environment variables or files ([ADR 0027](0027-secrets-from-files.md)). Microsoft 365 and
GitHub already had in-app setup.

Two kinds of values are involved:
- **settings** that are not secret, such as a GA4 property, an ads customer id or an organization name;
- **secrets**, such as a service account key, an API key or an OAuth refresh token.

## Decision

- **A neutral setup contract.** An integration that can be set up in the app implements `IConfigurableIntegration`
  (Core). Its `IntegrationSetup` names the settings file and declares:
  - its fields: id, label, help, length, whether the field is a list, and a check that returns why a value is refused;
  - its secrets: the secret's name (the same name `SecretSource` reads), a label and help.

  Integrations still know nothing about roles, and Core knows nothing about any particular integration.
- **Settings go to the same files.** The Host merges the declared fields into the integration's existing JSON file and
  leaves every other key untouched. Hand edits keep working, and the file stays the single source.
  - Values that name a program to run stay file-only and are never declared: the Azure CLI path (`az`) and the
    agent apps' `steer` commands. A form must never be a way to make HireZero run a program.
  - Each value is checked by the integration's own rule before anything is written. The file is written atomically.
  - Each change writes an `integration.configured` event that names the integration and the fields, not their values.
- **Secrets: pasted only in local mode, stored encrypted, never shown again.**
  - **Hosted mode refuses** to accept a secret. It shows each secret's name, whether it is present and where from,
    and points to docs/SECRETS.md: a vault or the platform delivers secrets there.
  - **Local mode** accepts a pasted secret for a declared name only, up to 64 KiB. Host secrets (database, audit,
    key-encryption and sign-in) are never accepted. Each value is encrypted with the host's existing Data Protection
    keys into `<data root>/secrets/NAME.secret`, the same protection stored Microsoft 365 tokens get
    (docs/SECRETS.md, section 4). Those keys are wrapped with `HIREZERO_KEY_ENCRYPTION_KEY` on any OS when it is set,
    and otherwise with DPAPI for the signed-in Windows user. Without either, on macOS and Linux they are protected
    only by file permissions, and the files are created owner-only (0600 in a 0700 directory). Using the OS keychain
    there is a later improvement.
  - **Write-only.** The API never returns a secret's value. Status says only "missing", "set in the app", or "from the
    environment, a file or a credential directory".
  - **Lowest precedence.** `SecretSource` reads the app store last, after `NAME_FILE`, credential directories and
    environment variables. A secret delivered by an operator or a vault always wins, and the status says so.
  - **Audited by name.** Setting or removing a secret writes an `integration.secret` event with the secret's name and
    never its value.
  - Integrations read settings and secrets at use, so their status updates at once. But an integration registers its
    tools only if it is active when HireZero starts. One that becomes active later says "Restart HireZero to start
    using it", rather than claiming to work. Registering it live is a later improvement.

## Why local paste is safe enough

- The workspace listens only on loopback and requires the exact Host header, which defeats DNS rebinding. Every
  change needs the exact Origin, a custom header and a JSON body, so another web page cannot submit a secret. The
  content security policy allows only the app's own scripts.
- Local mode has one owner. The secret never leaves the machine, and it is never echoed back to any page.
- The remaining risk is the same as for the GitHub CLI's or Codex's own sign-in files: a program running as the same
  user can read what that user can read. A key-encryption key, or DPAPI on Windows, protects copies and backups of the data root. People
  who want more can deliver the same names from a vault, which takes precedence.

## Consequences

- The Integrations section shows a form per configurable integration, with each secret's status. In local mode it
  also has a write-only secret field.
- docs/SECRETS.md gains a section on the local store.
- The first integrations declared are Google Analytics and Search Console, Google Ads, Azure DevOps (organizations
  only), OpenRouter, and the coding-agent apps (the app list only).
