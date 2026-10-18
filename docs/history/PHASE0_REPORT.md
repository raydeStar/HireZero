# Phase 0 report: clean foundation

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

Phase 0 made the repository read like a product, made the build reproducible on any .NET 10 SDK, pinned the agent
runtime, and moved the checks onto a standard test framework. Behaviour is unchanged except where noted under
"Behaviour changes".

## What changed

### Plan and decisions

- [ROADMAP.md](../ROADMAP.md) is the canonical plan (phases 0–4, exit criteria, what is deliberately deferred).
- Five ADRs in [docs/adr](../adr/): three layers, the employee module contract (proposed), the owner-approval safety
  model, pinned runtimes, and offline checks on xUnit.
- [ARCHITECTURE.md](../ARCHITECTURE.md) describes the system as it is, including known layering violations.
- `AGENTS.md` points at the roadmap and records the new runtime-pin and formatting conventions.

### Repository hygiene

- Removed ZIP-export leftovers: `SOURCE-MANIFEST.json`, `PACKAGE-VALIDATION.json`, `README.upstream.md`.
- Moved 13 dated session/handoff notes and `PROVENANCE.md` to [docs/history]() with an index.
- Moved the marketing brief fixture from `docs/` to `tests/fixtures/one-job-marketing-brief.json`.
- Rewrote `README.md` as a product README (requirements, build, profiles, runtime, checks, layout).

### Build

- `global.json` now accepts any .NET 10 SDK (`10.0.100`, `latestFeature`). It previously required 10.0.302+, so the
  repository did not build on this machine's 10.0.203 SDK.
- `Directory.Build.props` centralizes target framework, nullable, implicit usings, warnings-as-errors, the latest
  .NET analyzers and code-style enforcement. Per-project files now declare only what differs.
- Added `.editorconfig` (formatting plus style rules as suggestions), CSharpier 1.3.0 as a local dotnet tool,
  Prettier 3.9.9 for the frontend, and `typecheck`, `format`, `format:check` scripts in `web/package.json`.
- Applied CSharpier to all C# and Prettier to `web/`. This is a large mechanical diff (about 290 files) that undoes
  the dense one-line style. It is a separate commit listed in `.git-blame-ignore-revs`; run
  `git config blame.ignoreRevsFile .git-blame-ignore-revs` once so `git blame` skips it.

### Host

- `Program.cs` is now a 20-line profile dispatcher. The original single-agent composition moved verbatim to
  `DeveloperWorkspace.cs`.
- New `LocalHosting.cs` holds the loopback-only listener setup, security headers and local-request check that were
  copy-pasted across four profiles.
- Removed `DiscoveryWorkspace` (a standalone discovery page reachable only from tests). Its reset and discovery
  store checks are kept; only the checks of the removed HTTP page went with it.

### Pinned OpenClaw runtime

- `runtime/openclaw/package.json` pins **OpenClaw 2026.10.4** (latest stable on npm, published 2026-10-15) and its
  Node range; `package-lock.json` pins all 412 dependencies, resolved only from registry.npmjs.org.
- `tools/Install-OpenClawRuntime.ps1` installs exactly that tree into `<data root>/Runtime`, with dependency
  lifecycle scripts disabled. It checks free disk space and the Node version, runs OpenClaw's own bundled-plugin
  setup, and writes a receipt.
- The OpenClaw integration embeds the pin and refuses to start any other version, at all four process start sites;
  `tools/OpenClawSignIn.ps1` checks it too.
- Previously nothing recorded which OpenClaw version was expected, so there was no "old version" in the repo to
  upgrade from. The pin is the update.

### Tests

- All six .NET test projects run on xUnit v3 (Microsoft.Testing.Platform) via `dotnet test`. Each of the 64
  historical check groups is one test, so one failure no longer hides the others (verified with a deliberate
  canary failure: 1 failed, 21 passed in that project).
- Added focused tests for the runtime pin (`OpenClawRuntimeTests`).
- The company test executable keeps its `--company-browser-fixture`, `--measured-browser-fixture` and
  `--codex-inspect` entry points for the Node smoke tests. The `--conversation-checks` shortcut is replaced by
  `dotnet test --project tests/HireZero.Company.Tests --filter-method "*Conversation"`.

## Behaviour changes

- The Recovery profile now sends the same `Referrer-Policy` and `Content-Security-Policy` headers as the other
  profiles. The restored UI renders under that policy in the other profiles.
- An OpenClaw runtime that is not exactly 2026.10.4 is refused with a message naming both versions. **Existing data
  roots with an older hand-installed runtime must run the installer before live chat works again.**
- `.gitignore` now tracks the repository's `runtime/` pins while still ignoring data-root `Runtime/` folders.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` (analyzers, warnings as errors) | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 70 passed, 0 failed (Core, Developer, Company, Host, Integrations, Policy) |
| Baseline before changes (old runners) | Core 420, Developer 642, Company 364, Integrations 54, Policy 34 assertions plus the Host checks, all passing |
| `node --test tests/*.test.mjs` | 6 passed |
| Headless Chrome: company, measured queue, conversational reply smoke tests | all passed |
| `tests/launcher.test.ps1`, `tests/company-launcher.test.ps1` | 14 + 14 passed |
| `npm run build` / `typecheck` / `format:check` (web) | passed (existing bundle-size warning) |
| `dotnet csharpier check .` | 237 files clean |
| Installer with this machine's Node 22.15 | refused before creating anything, as designed |

Not verified: an actual OpenClaw 2026.10.4 install and live chat. That needs Node 24.16+ (not installed here) and
your own model sign-in, and a live model call is outside routine checks. The remaining Node browser smokes (chat,
onboarding, employee review, task handoff) were not run; the three that were cover the same fixture host.

## Deliberately not done in Phase 0

Each of these depends on the Phase 1 employee contract. Doing them first would have orphaned working Developer
features:

- Unifying the three employee definitions (`IRole`, `EmployeeDefinition`, `CompanyRole`).
- Retiring the Developer, Recovery and Held profiles and the legacy `web/src/shell` UI.
- Removing role vocabulary from Core and moving git/GitHub calls into integrations (Phase 2).

Also open: the `source-map-js` advisory in the frontend's transitive dependencies and Vite's bundle-size warning
(Phase 3 hygiene).

## Next steps: Phase 1, the employee module contract

Goal: adding an employee is a new role project plus a web role folder, enforced by an architecture test.

1. **Design the contract with you** (ADR 0002 → Accepted): `IEmployeeModule` and its manifest (identity and
   versions, typed skills, required tools with risk levels, KPIs, onboarding fields, UI slots, owned storage).
2. **Build the module host** in Core: discovery, a registry, per-module storage migrations, and an architecture test.
3. **Port Marketing and CEO first** (small, already company-native), then **Developer**, mapping its legacy
   services (PR review, fix loop, wiki drafts, preparation, Trusted Postgres) onto skills and tools.
4. **Merge the UI**: move developer panels into `web/src/company` through module UI slots; delete `web/src/shell`.
5. **Retire** `IRole`, `roles.json`, the unused `EmployeeTeam` path and the Developer/Recovery/Held profiles.

Decisions needed from you before step 1:

- **Discovery model**: modules compiled into the build and found by assembly scan (simpler, safer), or loaded from
  a plugin folder at runtime (third-party employees, but a real security boundary to design)?
- **Legacy Developer features**: the Developer profile also carries Microsoft 365 intake, coding-agent session
  steering, Azure DevOps, Jev triage, tailnet multi-user access and daily briefs. Port each into the company
  workspace, or drop some?
- **Legacy data**: migrate existing `workspace.sqlite` data into company mode, or start company data fresh (the
  current behaviour)?
