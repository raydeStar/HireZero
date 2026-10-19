# HireZero

Scaffold a company out of AI employees. Each employee is a module you hire, onboard with approved company
knowledge, talk to privately, assign work and review. Departments can hold several employees of the same kind
without sharing private history. A CEO employee proposes plans and chairs board meetings; you stay the owner.

- **You stay in charge.** Anything consequential (push, merge, send, publish, spend) waits for your explicit
  approval, bound to the exact version you reviewed. Uncertain outcomes are never replayed automatically.
- **Bounded by design.** Model calls run through single-use grants with token budgets reserved up front; usage that
  cannot be confirmed keeps its reservation held until you reconcile it.
- **Modular.** Employees (`src/HireZero.Roles.*`) reach the outside world only through integrations
  (`src/HireZero.Integrations.*`); the core knows nothing about any particular role. Adding an employee is a new
  project with one manifest ([how-to](docs/ADDING_AN_EMPLOYEE.md)).

Today's employees are **Developer** (GitHub PR review and fixes), **Marketing** (briefs and versioned drafts) and
**CEO** (plans and meetings). Where this is going is in the [roadmap](docs/ROADMAP.md).

> Status: development. Automatic model execution is off by default; the company workspace offers explicitly
> approved one-turn replies and drafts through a pinned runtime. See the [latest report](docs/history/PHASE3_IDENTITY_REPORT.md).

## Requirements

- Windows with PowerShell 7 (the launchers are Windows-only for now).
- .NET SDK 10 (any 10.0 feature band) and Node.js 22+ with npm.
- Optional, for live model replies and coding: the pinned Codex runtime and your own dedicated sign-in folder. No
  credentials or runtimes ship with this repo.
- At least 10 GiB free disk space before building.

## Build and run

```powershell
npm ci --prefix web --ignore-scripts
npm run build --prefix web
dotnet build HireZero.slnx
./tools/Start-CompanyLocal.ps1 -DataRoot <new absolute directory>
```

Open <http://127.0.0.1:5065/>. The company workspace is the only startup profile: it needs explicit local owner mode
and a data root, and refuses an existing legacy database. While `HIREZERO_EXECUTION_HOLD=1` is set it refuses to
start, so nothing resumes until you clear the hold. Stop it with Ctrl+C.

### Hosted mode

For real clients, run the host behind HTTPS with members signing in through any OIDC provider
([ADR 0020](docs/adr/0020-members-roles-and-sign-in.md)). Start with `--company-hosted` and set:

| Variable | Meaning |
| --- | --- |
| `HIREZERO_OWNER_LOGIN` | The first owner's email; their first verified sign-in makes them the owner |
| `HIREZERO_PUBLIC_URL` | The public `https://` origin members open |
| `HIREZERO_LISTEN` | The listener, for example `https://0.0.0.0:443` or `http://127.0.0.1:8080` |
| `HIREZERO_TLS_CERTIFICATE` / `_PASSWORD` | A certificate file when the host serves HTTPS itself |
| `HIREZERO_TRUSTED_PROXY` | The reverse proxy's IP when TLS ends there; only its forwarded headers are believed |
| `HIREZERO_OIDC_AUTHORITY` / `HIREZERO_OIDC_CLIENT_ID` | Your provider and the app you registered (code flow with PKCE, redirect `<public url>/signin-oidc`) |
| `HIREZERO_OIDC_CLIENT_SECRET` | Only for a confidential client; never in a file |

Owners invite members by email as owner, approver or viewer; a viewer only reads.

### Database

Company data lives in `workspace.sqlite` in the data root. A hosted install can keep it in PostgreSQL instead, by
setting `HIREZERO_DATABASE` to an Npgsql connection string
([ADR 0025](docs/adr/0025-one-schema-two-databases.md)). The audit key and other per-machine files stay in the data
root either way. The PostgreSQL checks run only when `HIREZERO_TEST_POSTGRES` names a disposable server; each test
uses its own schema and drops it afterwards.

### Container and releases

`Dockerfile` builds hosted mode on base images pinned by digest: it runs as a non-root user and listens on port
8080 behind your HTTPS proxy (set `HIREZERO_TRUSTED_PROXY` to its address), with data in `/data`. Releases come
from `pwsh tools/Build-Release.ps1 -Version X.Y.Z -SigningKey <key outside the repo> [-Image]`. Each release is:
- a portable zip (the host, both launchers, README and license), which runs on any OS with the .NET 10 runtime;
- a CycloneDX SBOM;
- `SHA256SUMS`, with an ECDSA P-256 signature.

Verify a release with `pwsh tools/Test-Release.ps1`, or with `sha256sum` and `openssl`. On macOS and Linux, start
it with `sh tools/start-company-local.sh --data-root <dir>`. `tools/Check-Advisories.ps1` lists dependency
advisories and fails on any without a decision in `docs/security/advisories.json`
([ADR 0026](docs/adr/0026-container-and-releases.md)).

### Telemetry

Traces and metrics (employee jobs, tool calls, model calls and tokens per company and employee) are exported over
OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; the standard `OTEL_*` variables configure the exporter
([ADR 0024](docs/adr/0024-opentelemetry-traces-and-metrics.md)). Without it nothing leaves the machine.

### Pinned agent runtime

`runtime/codex/pin.json` pins the Codex runtime used for live replies and coding
([ADR 0014](docs/adr/0014-pinned-codex-runtime.md)). Start with `-Codex` (and `-Coding` for live coding runs),
naming the pinned executable, its reviewed identity and your dedicated sign-in folder. The host refuses a runtime
that does not match the pin ([ADR 0004](docs/adr/0004-pinned-agent-runtimes.md)). Complete any sign-in yourself in
the browser; never paste credentials into chat or copy auth files between machines.

### Model routes

Every model HireZero uses is chosen by purpose in `config/model-routes.json` (for example `company.text`). To use
different models for one data root, point `HIREZERO_MODEL_ROUTES` at your own JSON file with the routes you want
to change ([ADR 0008](docs/adr/0008-model-routes.md)). Changing a route changes spend: review it like code.

## Checks

All checks are synthetic: no live model, GitHub or GPU calls.

```powershell
dotnet test --solution HireZero.slnx                       # all .NET tests (xUnit v3 on Microsoft.Testing.Platform)
dotnet test --project tests/HireZero.Core.Tests            # one project
dotnet csharpier check .                                   # C# formatting (dotnet tool restore once)
npm run typecheck --prefix web
npm run format:check --prefix web
node --test tests/*.test.mjs                               # frontend unit tests
node tests/company-browser-smoke.mjs                       # headless Chrome smoke tests (build first)
pwsh tests/company-launcher.test.ps1                       # launcher test (mocked process)
sh tests/company-launcher.test.sh                          # POSIX launcher test (fake dotnet)
pwsh tests/release.test.ps1                                # SBOM, checksums and signatures
pwsh tools/Invoke-Checks.ps1 -Browser                      # all of the above in one run (any CI runner)
```

Use the smallest set that covers a change. Apply formatting with `dotnet csharpier format .` and
`npm run format --prefix web`.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/HireZero.Core` | Company, conversations, tasks, approvals, budgets, storage |
| `src/HireZero.Roles.*` | Employees: Developer, Marketing, CEO |
| `src/HireZero.Tools.*` | Tool contracts shared by roles and integrations |
| `src/HireZero.Integrations.*` | External systems: Git and GitHub CLI, npm, model runtimes, Microsoft 365, Azure DevOps |
| `src/HireZero.Host` | The company workspace's composition root |
| `web/` | React frontend (`company/` workspace, `roles/` employee panels, `lib/` shared client) |
| `runtime/` | Pinned external agent runtimes |
| `config/` | Model route catalog |
| `tests/` | xUnit projects, Node and browser smoke tests, fixtures |
| `tools/` | The company launcher and setup helpers |
| `docs/` | [Roadmap](docs/ROADMAP.md), [architecture](docs/ARCHITECTURE.md), [decisions](docs/adr/), [history](docs/history/) |

## Contributing

Read [AGENTS.md](AGENTS.md) first. Never commit credentials, private or employer data; fixtures are fictional.
Publication history and the original secret-scan review are in [docs/history](docs/history/).

## License

MIT. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).
