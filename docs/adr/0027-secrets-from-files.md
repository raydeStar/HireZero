# 0027: Secrets from files, vault-neutral

Status: Accepted (Phase 3). The guide is [docs/SECRETS.md](../SECRETS.md).

## Context

HireZero's secrets were read from environment variables:
- the OIDC client secret;
- the TLS certificate password;
- the database connection string;
- the model and integration API keys.

The audit key was a file beside the database, created on first use. The Data Protection keys that encrypt stored
Microsoft 365 tokens were protected with DPAPI on Windows and not at all elsewhere. Phase 3 asked for "secrets from a
vault". The owner did not want HireZero to depend on Azure, or on any single vault.

## Decision

- **Read secrets by name, from files first.** `SecretSource` in Core resolves a name such as
  `HIREZERO_OIDC_CLIENT_SECRET` from the first of:
  1. the file named by `HIREZERO_OIDC_CLIENT_SECRET_FILE`;
  2. a file of that name, or its lowercase form, in systemd's `$CREDENTIALS_DIRECTORY` or in `/run/secrets`;
  3. the environment variable itself.

  This is how Docker, Kubernetes, systemd and vault agents hand secrets to a program. Any vault works this way,
  whether HashiCorp Vault or OpenBao, a cloud secret manager through its CSI driver, 1Password, Infisical, Doppler
  or SOPS, and HireZero takes no SDK or service dependency for it.
- **Read at use, so rotation needs no restart.**
  - The OIDC client secret is re-read for every code exchange, and API keys for every call.
  - The PostgreSQL password, kept out of the connection string, goes through Npgsql's periodic password provider.
  - A test rotates a real role's password while a store keeps working.
- **Keys at rest, on every OS.**
  - `HIREZERO_KEY_ENCRYPTION_KEY` (32 bytes) wraps the Data Protection keys with AES-256-GCM, through Data
    Protection's own encryptor extension point.
  - `HIREZERO_KEY_ENCRYPTION_KEY_PREVIOUS` still opens keys wrapped before a rotation.
  - Without a key-encryption key, Windows keeps DPAPI. Elsewhere the keys are stored unencrypted, and the startup
    report says so.
  - Key protection is now configured once by the host. The Microsoft 365 integration no longer configures it.
- **A shared audit key.**
  - `HIREZERO_AUDIT_KEY` (64 hex characters) supplies the audit chain's key, so several hosts on one database
    verify the same chain.
  - If an `audit.key` file already exists with a different key, startup is refused, because a fork would break
    verification.
- **Report sources, never values.** At startup the host logs each configured secret's origin and location, and how
  stored keys are protected. It also notes a secret in a plain environment variable, and one overridden by a file.
- **No vault client yet.** A direct client for leases or dynamic credentials can be added later as an optional
  integration project, with Core and roles unchanged. OpenBao, the open-source, API-compatible fork of HashiCorp
  Vault, is the natural first choice.

## Consequences

The same build runs with secrets from a laptop's environment, Docker secrets, a Kubernetes volume, systemd
credentials or a vault agent. Rotation is a file change for the OIDC secret, API keys and the database password.
Rotating the key-encryption key needs the previous key until the old Data Protection keys expire, which is 90 days
by default. The audit key is never rotated in place, because it is the chain's identity.

Configuration that is not secret is still read from environment variables. Binding it through `IOptions` remains open.
