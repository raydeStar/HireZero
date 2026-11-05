# Settings

Every setting HireZero reads, in one place. Secrets (passwords, keys, client secrets) are not settings. They are
covered in [SECRETS.md](SECRETS.md). The decision record is [ADR 0028](adr/0028-typed-settings.md).

## How settings are read

Optional owner email notices use `HIREZERO_NOTIFICATION_SMTP_HOST`, `HIREZERO_NOTIFICATION_SMTP_PORT` (default
587, STARTTLS required), `HIREZERO_NOTIFICATION_FROM`, and `HIREZERO_NOTIFICATION_SMTP_USERNAME`. They require
hosted sign-in and an HTTPS `HIREZERO_PUBLIC_URL`. Supply the SMTP password as the secret
`HIREZERO_NOTIFICATION_SMTP_PASSWORD`, never in the settings JSON. Configuration alone sends no email:
each owner must opt in from the workspace inbox using a verified sign-in address. See
[ADR 0059](adr/0059-durable-inbox-and-bounded-notices.md) for quotas, expiry and uncertain-outcome handling.

Company owners manage monthly AI-job and token safety limits in **Company → Usage**. The default is 100 jobs
and 1,000,000 planning tokens per UTC month, with an explicit pause for new jobs. Existing unresolved
reservations carry across month boundaries. These database settings do not purchase credits or change a paid
subscription. See [ADR 0061](adr/0061-monthly-usage-admission-and-cost-receipts.md).

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

## Hosted open sign-up

Open sign-up is off by default and requires `--company-hosted`. Local mode refuses
`HIREZERO_OPEN_SIGNUP=1`. The existing OIDC provider (Google for the hosted install) must supply a verified email.
A verified visitor without a membership can choose **Create my company** and becomes its owner.

| Setting | Meaning |
| --- | --- |
| `HIREZERO_OPEN_SIGNUP` | Default `0`. `1` enables the hosted enrollment flow. |
| `HIREZERO_SIGNUP_MAX_COMPANIES` | Default `200`; maximum companies ever created through sign-up on this install. Integer 1–1,000,000. |
| `HIREZERO_SIGNUP_COMPANY_CAP_CENTS` | Default `50` ($0.50); lifetime model allowance saved with each new sign-up company. Integer 0–1,000,000. Changing it affects future companies only. |
| `HIREZERO_DAILY_CAP_CENTS` | Default `500` ($5.00); shared model allowance across **all** companies on this install per UTC day. Integer 0–1,000,000; zero pauses new spending. Applied when open sign-up is enabled. |

The sign-in subject can create only one free company, including through the ordinary company-creation route.
Concurrent enrollment and spending are serialized in the shared database. Model dispatch reserves a conservative
worst case (all input bytes plus API framing and the full approved output target) before sending. Confirmed usage
settles at the reviewed price or reported bill; unknown usage retains its reservation across restarts, on the
company's lifetime cap and on the UTC day it was sent. A dispatch receipt can never authorize a second send. The UI shows the lower of the company's lifetime
remainder and today's shared remainder, in dollars. Existing job, token, monthly and approval limits still apply.

Disabling open sign-up stops enrollment, but does **not** remove existing caps or reservations. The last configured
daily limit remains durable. Signed-up companies keep their isolation and lifetime cap. Never erase their ledger
to recover an uncertain request.

A capped install uses its configured host API model. Codex and saved company model keys are refused at startup;
new company model keys and the separate unbounded Jev/tool-provider paths are unavailable. Supported text roles,
including Marketing, can be hired; roles needing a workspace/coding runtime are excluded for sign-up companies.
Data-root connections, shared search credentials, synthetic integrations, outgoing owner notices and billing setup
are not offered to them. Public-page reads and private company documents remain available. No keys are shared
with a signed-up company. See [ADR 0181](adr/0181-open-signup-and-durable-spend-caps.md).

## Isolated scripts

All five binding settings below are required together; none are enabled by default. Qualification and engine
setup are explicit operator actions described in [the runtime guide](../runtime/sandbox/README.md).

| Setting | Meaning |
| --- | --- |
| `HIREZERO_SANDBOX_DOCKER` | Absolute installed Docker CLI file. |
| `HIREZERO_SANDBOX_ENDPOINT` | `unix:///var/run/docker.sock` or `npipe:////./pipe/dockerDesktopLinuxEngine`; remote endpoints are refused. |
| `HIREZERO_SANDBOX_IMAGE` | Exact qualified local `sha256:` image ID; no tag resolution or pull. |
| `HIREZERO_SANDBOX_INSTALLATION` | Durable installation GUID without hyphens. Retain across restarts. |
| `HIREZERO_SANDBOX_QUALIFICATION` | Absolute private path to the operator-generated qualification JSON receipt. |
| `HIREZERO_SANDBOX_ENABLED` | Default `0`: recovery only. `1` permits new scripts only while qualification and live readiness checks pass. |

Isolated website reviews use a separate qualification and image binding. Configuring any binding field requires
all five; enabling new visits also requires the complete binding. See [browser qualification](../runtime/browser/README.md).

| Name | Meaning |
| --- | --- |
| `HIREZERO_BROWSER_DOCKER` | Absolute installed Docker CLI file. |
| `HIREZERO_BROWSER_ENDPOINT` | `unix:///var/run/docker.sock` or `npipe:////./pipe/dockerDesktopLinuxEngine`; remote endpoints are refused. |
| `HIREZERO_BROWSER_IMAGE` | Exact operator-reviewed local `sha256:` browser image ID; no tag resolution or pull. |
| `HIREZERO_BROWSER_INSTALLATION` | Durable installation GUID without hyphens. Retain across restarts. |
| `HIREZERO_BROWSER_QUALIFICATION` | Absolute private path to the browser-specific qualification receipt. A script receipt cannot enable browser work. |
| `HIREZERO_BROWSER_ENABLED` | Default `0`: recovery only. `1` permits new visits while browser qualification and readiness checks pass. Owner itinerary approval remains separate. |

Qualification expiry or disabling new scripts or website reviews does not disable recovery. Preserve the original configuration
until all existing jobs settle. Operator paths, executable hashes and engine configuration are not exposed in the
company API; it returns a bounded readiness reason and expiry. Shared script/browser allocation remains a safety quota,
not a measured compute bill. See [ADR 0068](adr/0068-qualified-runtime-activation.md) and
[ADR 0095](adr/0095-qualified-browser-activation.md).

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
| `HIREZERO_LISTEN` | Where hosted mode listens, as `http(s)://IP:port`; required when hosted. Local mode optionally accepts an `http://` loopback IP and port, defaulting to `http://127.0.0.1:5065`. |
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
| `HIREZERO_COMPANY_ANALYSIS_MODEL` | Optional route for measured planning, technical reviews and owner memory checks. Defaults to the routine text route; each request still needs exact approval ([ADR 0092](adr/0092-explicit-purpose-model-routing.md)). |
| `HIREZERO_COMPANY_CODING_MODEL` | Route for coding runs; default from `config/model-routes.json`. |
| `HIREZERO_API_MODELS` | `1` uses an API model (Anthropic, OpenAI or OpenRouter key) when the Codex connection is not selected ([ADR 0036](adr/0036-api-model-provider.md)). |
| `HIREZERO_COMPANY_CODEX_EXECUTABLE` | The pinned Codex executable, with `--company-codex`. |
| `HIREZERO_COMPANY_CODEX_IDENTITY` | The reviewed bundle identity of that executable. |
| `HIREZERO_COMPANY_CODEX_HOME` | The dedicated Codex profile home. |
| `HIREZERO_COMPANY_CODEX_XHIGH` | `1` requests `xhigh` for newly reviewed Codex text jobs. The existing review shows the effort. Previously approved schedules retain their recorded effort and limits. Defaults to `0`. |
| `HIREZERO_COMPANY_CODING_READ` | Semicolon-separated folders that live coding runs may read. |
| `HIREZERO_COMPANY_CODING_PACKAGES` | A pre-filled NuGet package folder that live coding runs read offline, as a fallback folder (docs/adr/0045-offline-packages-for-coding-runs.md). |

## Integrations

| Setting | Meaning |
| --- | --- |
| `HIREZERO_SYNTHETIC_WIKI` | `1` turns on the synthetic Wiki pilot. |
| `HIREZERO_SYNTHETIC_ANALYTICS` | `1` serves fictional analytics data to Marketing when Google is not connected. |
| `HIREZERO_SYNTHETIC_ADS` | `1` serves fictional ad data (in the test currency XTS) to Marketing. |
| `HIREZERO_SYNTHETIC_EMAIL` | `1` serves fictional email campaign results to Marketing. |
| `HIREZERO_SYNTHETIC_CRM` | `1` serves a fictional sales pipeline (in XTS) to Marketing. |

## Billing

Billing is off until an operator supplies a reviewed catalog and Stripe account configuration. See
[ADR 0082](adr/0082-billing-host-and-owner-journey.md). Launch prices are not bundled.

| Setting | Meaning |
| --- | --- |
| `HIREZERO_BILLING_CATALOG` | Absolute path to a JSON array of explicit monthly prices and UTC-month allowances; at most 64 KiB and 20 plans. Loaded at startup. |
| `HIREZERO_STRIPE_ACCOUNT` | Expected merchant account ID; checked before provider operations. |
| `HIREZERO_STRIPE_PORTAL_CONFIGURATION` | Reviewed customer portal configuration ID. Initially allow payment details, invoices and cancellation at period end; do not enable plan or quantity changes. |
| `HIREZERO_STRIPE_LIVE_MODE` | Explicit `0` for test mode or `1` for live mode. Required with billing configuration. |
| `HIREZERO_BILLING_ENABLED` | `1` allows owner-approved new subscriptions. Default `0` retains reconciliation and account management without new checkout creation. |

Billing requires hosted sign-in and the configured HTTPS public origin. All configuration fields above except
`HIREZERO_BILLING_ENABLED` must be supplied together. API and webhook signing keys use the separate
[secret mechanism](SECRETS.md), never the catalog or settings file. Once billing is required for a company,
removing configuration does not restore unpaid admission.

A catalog plan may explicitly include `allowance.extendedScriptSeconds` for extended batch scripts. Its value
must be 121–1,800, no greater than `computeSeconds`, with `computeJobs` above zero. Omit it for the standard
120-second tier. This grants a per-job ceiling within the existing monthly compute pool, not extra purchased
seconds or automatic overage. A separately qualified extended runtime and exact owner approval are also
required; see [ADR 0086](adr/0086-paid-extended-scripts.md) and the [runtime guide](../runtime/sandbox/README.md).

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

## Selected Google Drive documents

Under **Company → Integrations → Google Drive documents**, an owner can configure up to 20 individual file IDs
and a dedicated service account key. The ordinary settings file is `google-drive.json` inside that company's
integration directory, with a `fileIds` string array. Keys belong in the secret store, never in this JSON file.
Hosted credential placement is described in [SECRETS.md](SECRETS.md).

Open **Company → Documents → Add a document → Import from Google Drive** to read one selected Google Doc,
Word `.docx`, UTF-8 text or Markdown file. Configuration alone makes no external read. The preview checks download
permission and file version, imports bounded text with source details, and leaves saving and approval to the owner.
Folders and shortcuts are not followed. Text copies have the existing 64 KiB limit, including their source note;
Word downloads have a 2 MiB compressed limit. Metadata/auth responses are bounded to 32 KiB and a preview to 30 seconds.

Imports are editable snapshots, not synchronized records. A copied source note can be edited along with the text;
it is not a signed provenance receipt. Removing Drive access stops new reads but does not remove saved documents.
New or hosted companies have independent settings, credentials and read permission. Real Google authorization and
production service-account policy still require deployment-specific acceptance.

## Company-owned model connections

Owners configure company API keys under **Integrations → AI model connection**. Configuration alone makes no
provider request. Each text job still needs its own review, allowance and authorization. Coding runtimes remain
separate. See [ADR 0085](adr/0085-company-owned-model-connections.md).

The available API models come from the `prices` entries in `config/model-routes.json` (or the existing model-route
catalog override). Each entry needs nonnegative `input` and `output` dollar prices per million tokens and an
explicit `provider/model` route. Review provider availability and prices before configuring it; native agent model
names are not proof of API availability. The catalog loads at process startup. No production API prices are bundled.
An empty catalog shows setup guidance and leaves the existing host connection available.

A company key does not purchase or increase a subscription allowance. Both purchased allowances and monthly
safety limits continue to apply. Usage identifies the company-funded subset as charges paid directly to the API
provider. Commercial discounts or different BYOK plans require a separate reviewed decision.
