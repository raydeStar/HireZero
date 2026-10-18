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
> approved one-turn replies and drafts through a pinned runtime. See the [latest report](docs/history/PHASE2B5I_REPORT.md).

## Requirements

- Windows with PowerShell 7 (the launchers are Windows-only for now).
- .NET SDK 10 (any 10.0 feature band) and Node.js 22+ with npm.
- Optional, for live model replies: your own model sign-in and the pinned agent runtime (OpenClaw needs Node 24.16+,
  installed per data root by `tools/Install-OpenClawRuntime.ps1`). No credentials or runtimes ship with this repo.
- At least 10 GiB free disk space before building.

## Build and run

```powershell
npm ci --prefix web --ignore-scripts
npm run build --prefix web
dotnet build HireZero.slnx
./tools/Start-CompanyLocal.ps1 -DataRoot <new absolute directory>
```

Open <http://127.0.0.1:5065/>. The company workspace needs a fresh data root and refuses an existing legacy
database. Stop it with Ctrl+C.

### Startup profiles

The host has one profile per composition root, selected explicitly ([architecture](docs/ARCHITECTURE.md)):

| Profile | Start with | What it serves |
| --- | --- | --- |
| Company | `tools/Start-CompanyLocal.ps1 -DataRoot <dir>` | The multi-employee company workspace on port 5065 |
| Developer | `tools/Start-HireZero.ps1` | The original single-agent Developer workspace on port 5064 (retired in Phase 1) |
| Recovery | `tools/Start-Home.ps1` | Developer UI with text-only chat and read-only PR discovery |
| Held | `HIREZERO_EXECUTION_HOLD=1` | Read-only status while execution is held for verification |

### Pinned agent runtime

`runtime/openclaw/package.json` and its lockfile pin the OpenClaw runtime (currently 2026.10.4). Install it into a
data root with:

```powershell
./tools/Install-OpenClawRuntime.ps1 -DataRoot <data root> -NodePath <node 24.16+ executable>
./tools/OpenClawSignIn.ps1 -Owner -DataRoot <data root>
```

The host refuses to start an OpenClaw runtime whose version differs from the pin
([ADR 0004](docs/adr/0004-pinned-agent-runtimes.md)). Complete any sign-in yourself in the browser; never paste
credentials into chat or copy auth files between machines.

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
node --test tests/*.test.mjs                               # frontend and hook unit tests
node tests/company-browser-smoke.mjs                       # headless Chrome smoke tests (build first)
pwsh tests/launcher.test.ps1                               # launcher tests (mocked processes)
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
| `src/HireZero.Host` | Composition roots (startup profiles) |
| `web/` | React frontend (`company/` workspace, `roles/` employee panels, `shell/` legacy Developer UI) |
| `runtime/` | Pinned external agent runtimes |
| `config/` | Model route catalog |
| `tests/` | xUnit projects, Node and browser smoke tests, fixtures |
| `tools/` | Launchers, runtime installer, sign-in helpers |
| `docs/` | [Roadmap](docs/ROADMAP.md), [architecture](docs/ARCHITECTURE.md), [decisions](docs/adr/), [history](docs/history/) |

## Contributing

Read [AGENTS.md](AGENTS.md) first. Never commit credentials, private or employer data; fixtures are fictional.
Publication history and the original secret-scan review are in [docs/history](docs/history/).

## License

MIT. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).
