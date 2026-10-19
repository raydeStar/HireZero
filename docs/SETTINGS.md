# Settings

Every setting HireZero reads, in one place. Secrets (passwords, keys, client secrets) are not settings. They are
covered in [SECRETS.md](SECRETS.md). The decision record is [ADR 0028](adr/0028-typed-settings.md).

## How settings are read

Each setting has one name, and you can give it in either of two places:

1. **An environment variable** of that name, which works everywhere.
2. **A JSON settings file**, named by `HIREZERO_SETTINGS_FILE`, whose keys are the same names. This suits a VM, or a
   Kubernetes ConfigMap mounted as a file:

   ```json
   {
     "HIREZERO_DATA_ROOT": "/data",
     "HIREZERO_PUBLIC_URL": "https://hirezero.example.com/",
     "HIREZERO_LISTEN": "http://0.0.0.0:8080",
     "HIREZERO_TRUSTED_PROXY": "10.0.0.2",
     "HIREZERO_OIDC_AUTHORITY": "https://login.example.com",
     "HIREZERO_OIDC_CLIENT_ID": "hirezero",
     "HIREZERO_OWNER_LOGIN": "owner@example.com"
   }
   ```

When both give a value, the environment variable wins.

At startup, all settings are read and checked together. If anything is wrong, HireZero does not start, and it lists
every problem at once:

```
The settings are not valid. Nothing was started:
- HIREZERO_LOCAL_OWNER (environment) must be 1 or 0.
- HIREZERO_TRUSTED_PROXY (settings file) is not an IP address.
```

The settings file is also checked for two mistakes:
- **A secret in it.** Settings files tend to be copied and committed, so a secret there is refused.
- **A name this version does not know.** A misspelled name is refused rather than silently ignored.

The startup log names each setting that was given and where it came from (`environment` or `settings file`).
Inside the host, the checked values are available to code as `IOptions<CompanySettings>`.

## Workspace

| Setting | Meaning |
| --- | --- |
| `HIREZERO_DATA_ROOT` | Absolute directory for company data, keys and locks. Required. |
| `HIREZERO_OWNER_LOGIN` | The first owner's identity; an email address in hosted mode. |
| `HIREZERO_LOCAL_OWNER` | `1` for one-person local owner mode (loopback only). |
| `HIREZERO_EXECUTION_HOLD` | `1` refuses startup until the owner clears the hold. |
| `HIREZERO_SETTINGS_FILE` | A JSON settings file, as above. |

## Hosted mode (`--company-hosted`)

| Setting | Meaning |
| --- | --- |
| `HIREZERO_PUBLIC_URL` | The https origin members open, for example `https://hirezero.example.com/`. Required. |
| `HIREZERO_LISTEN` | Where the host listens, as `http(s)://IP:port`. Required. |
| `HIREZERO_OIDC_AUTHORITY` | Your identity provider's issuer URL. Required. |
| `HIREZERO_OIDC_CLIENT_ID` | HireZero's client id at the provider. Required. |
| `HIREZERO_OIDC_SCOPES` | Space-separated scopes; default `openid profile email`. |
| `HIREZERO_TRUSTED_PROXY` | The reverse proxy's IP address; needed when listening on plain http beyond loopback. |
| `HIREZERO_TLS_CERTIFICATE` | A certificate file, when HireZero serves https itself. |

Hosted mode names all of its missing required settings at once.

## Models and Codex

| Setting | Meaning |
| --- | --- |
| `HIREZERO_COMPANY_TEXT_MODEL` | Route for conversations (`vendor/model`); default from `config/model-routes.json`. |
| `HIREZERO_COMPANY_CODING_MODEL` | Route for coding runs; default from `config/model-routes.json`. |
| `HIREZERO_COMPANY_CODEX_EXECUTABLE` | The pinned Codex executable, with `--company-codex`. |
| `HIREZERO_COMPANY_CODEX_IDENTITY` | The reviewed bundle identity of that executable. |
| `HIREZERO_COMPANY_CODEX_HOME` | The dedicated Codex profile home. |
| `HIREZERO_COMPANY_CODING_READ` | Semicolon-separated folders that live coding runs may read. |

## Integrations

| Setting | Meaning |
| --- | --- |
| `HIREZERO_SYNTHETIC_WIKI` | `1` turns on the synthetic Wiki pilot. |
| `HIREZERO_SYNTHETIC_ANALYTICS` | `1` serves fictional analytics data to Marketing when Google is not connected. |
| `HIREZERO_SYNTHETIC_ADS` | `1` serves fictional ad data (in the test currency XTS) to Marketing. |

## Other variables

These are standard variables of other tools, or developer-machine tool locations. They are read where they are used,
not through `CompanySettings`:

| Variable | Meaning |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME` and other `OTEL_*` | OpenTelemetry export ([ADR 0024](adr/0024-opentelemetry-traces-and-metrics.md)). |
| `HIREZERO_WIKI_URL` | The Wiki integration's loopback origin; default `http://127.0.0.1:5094`. |
| `HIREZERO_CODEX_COMMAND`, `HIREZERO_CODEX_HOME`, `HIREZERO_CLAUDE_COMMAND`, `CODEX_HOME` | Local agent app locations, for the agent-app integrations. |
| `HIREZERO_AZ` | The Azure CLI executable, for the Azure DevOps integration. |
| `HIREZERO_TEST_POSTGRES` | Tests only: a disposable PostgreSQL server for the PostgreSQL checks. |
| `HIREZERO_RELEASE_DIRTY` | Release builds only: allow building from uncommitted changes, for a dry run. |
