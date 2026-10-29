# Vultr Agent Rush: engine repo name and extraction list

Prep for the Nov 3 to 8 build. This note picks a name for the public MIT engine repo and lists what to lift from
HireZero, what to rewrite thin, and what stays private. Nothing here has been copied, built or run yet.

Read against `main` at `ce5ab0c` (Oct 27). Dependencies were found by matching each file's references against the
types declared in `src/HireZero.Core`, so they are a static read, not a compile. Confirm with a build on Nov 3.

## Repo name

Pick: **`hirezero-engine`**. It says what it is, it points judges and readers back to the product, and it leaves
"HireZero" as the paid thing built on top.

| Option | For | Against |
| --- | --- | --- |
| `hirezero-engine` | Clear lineage, free marketing for the product | Ties the public repo to the product name |
| `zeroblast` | Echoes Challenge 1 ("Blast Radius Zero") and HireZero | Reads as a hackathon name after the event |
| `contained-runs` | Plain description of the job | Generic, says nothing about who made it |

Check the name is free on GitHub before creating it.

## Lift as-is

These depend only on the small Core slice below, so they should move with namespace changes only.

| From HireZero | Notes |
| --- | --- |
| `runtime/sandbox/` | Script worker Dockerfile, pin, runner, image check |
| `runtime/browser/` | Browser worker Dockerfile, pin, driver, protocol, runner, image check |
| `runtime/qualification-host/daemon.json`, `pin.json` | Docker and runsc settings. Skip `wsl.conf` and the WSL parts of its README; the Vultr box is plain Ubuntu |
| `src/HireZero.Integrations.Sandbox/` Docker files | `DockerSandbox`, `DockerBrowser`, `DockerCommands`, `DockerDuplexCommands`, `DockerCapabilities`, `DockerRuntimeProfile`, `QualifiedDockerSandbox`, `QualifiedDockerBrowser`, `SandboxQualification`, `BrowserQualification`, `BrowserWorkerProtocol`, `BrowserWorkerRecovery` |
| `src/HireZero.Integrations.Web/PublicBrowserNetwork.cs` | The host broker that allows only approved HTTPS origins. Uses only `Browser.cs` and `Contracts.cs`; leave the rest of that project behind |
| Core slice | `Browser.cs`, `BrowserExecution.cs`, `BrowserResults.cs`, `Sandbox.cs`, `ContentGuard.cs`, `Contracts.cs`, plus `CompanyJson` copied out of `CompanyContracts.cs` (the only type the adapters use from it) |
| Tests | `DockerRuntimeProfileTests`, `DockerBrowserTests`, `BrowserRuntimeTests`, `SandboxTests`, `SandboxRuntimeTests` (the last two use a company id and `CompanyJson` in a few places; swap in a plain id), and `browser-driver`, `browser-protocol`, `browser-runner`, `browser-worker-fixture`, `sandbox-image`, `sandbox-worker` under `tests/` |
| Root files | `LICENSE`, `THIRD_PARTY_NOTICES.md` (trim to what ships), `global.json`, `Directory.Build.props`, `nuget.config` |
| ADRs, as background | 0065, 0066, 0075 to 0077, 0095, 0100, 0109 to 0111, 0113, 0119, 0120, 0146 |

## Rewrite thin

These exist in HireZero but are tied to `CompanyStore` (the company database), so lift the logic, not the file.

| Piece | Where the logic lives | What the engine needs |
| --- | --- | --- |
| Receipt chain | `CompanyAudit.cs`: `AuditHash`, `Audit`, `VerifyAudit` | A standalone append-only log: HMAC-SHA256 over seq, time, kind, subject, actor, body and previous hash, with export and verify. About 150 lines |
| Approval packet | `CompanyBrowserContracts.cs`, `CompanySandboxContracts.cs` | One exact packet (origins, steps, limits, digest) and a one-run consent record. Drop plans, assignments and billing |
| Runners | `CompanyBrowserRunner.cs`, `CompanySandboxRunner.cs` | A plain runner that persists the result and release before cleanup. Keep the never-replay rule |
| Model provider | `ApiModelProvider.cs`, `ModelRoutes.cs`, `ModelProviders.cs` | One OpenAI-compatible provider: base URL, key and model from config, `max_tokens` on every call, per-run token budget, monthly ceiling. Vultr is `https://api.vultrinference.com/v1` plus a key. The current file hard-codes the Anthropic, OpenAI and OpenRouter URLs and takes `CompanyMeasuredJob`, so start from its request and limit code rather than the class |
| Itinerary drafting | New | The employee turns a goal into a browser itinerary as JSON; the owner approves the exact packet. A fixed template the model fills in is the fallback |
| Demo UI and `deploy/vultr/` | New | Read-only replay page, judge login, and a script that builds the VM so it can be destroyed instead of stopped |

## Stays private

- Roles: `src/HireZero.Roles.*` and `web/src/roles/*` (Marketing, Support, CEO, Developer)
- Company workspace: the rest of `src/HireZero.Core` (memory, documents, schedules, plans, measured queue, billing, members), `src/HireZero.Host`, `web/`
- Billing: `src/HireZero.Integrations.Stripe`
- Every other integration and tool project (Google, Microsoft 365, GitHub, Azure DevOps, OpenRouter tools, Codex, Jev, Wiki, Postgres and the rest)
- `docs/product/`, the pilot material, `tools/` install and backup scripts, and anything holding real receipts or pilot data

## Watch for

- The Core slice throws `WorkspaceException` from `Contracts.cs`; keep that file whole rather than renaming the
  exception mid-sprint.
- `QualifiedDockerBrowser` checks that its receipt is bound to the exact image, installation id, endpoint and
  engine, so a receipt from the local WSL host will not carry over. Re-run qualification on the Vultr VM. The
  host wiring that reads the receipt from settings (`CompanyBrowserRuntime.cs`, `CompanySandboxRuntime.cs`) stays
  private; the engine needs a small replacement.
- Update (Oct 29): the script runtime now passes local qualification. After the task-accounting fix in ADR 0146
  (`nproc` 31 inside the runtime, 32 actual workload tasks), all six standard and extended cases passed on the
  local host and a protocol v3 receipt was issued (`docs/history/2026-10-27-script-task-accounting-correction.md`).
  Lift the sandbox files from `main` at or after that commit, along with `SandboxQualificationTests`,
  `SandboxTaskLimitFixture` and `SandboxResourceObserver`. The script runtime is a realistic stretch goal now,
  but it still needs its own qualification run on the Vultr VM, and the browser stays the hero.
- In the README, say plainly what came from HireZero and what was built Nov 3 to 8.
