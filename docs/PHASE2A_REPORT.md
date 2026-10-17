# Phase 2A report: tools and model routes

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](ROADMAP.md).

Phase 2 restores the layers: employees reach the world only through registered, audited tools, and models are
chosen by purpose. It is split in two. 2A, reported here, lays the foundations and proves them end to end on the
company Developer. 2B ports the legacy Developer and unifies the model provider contracts.

## Why the split

A survey of the code found:

- **One client for every external call.** All of Developer's git, GitHub, SemaForge, Wiki and npm access funnels
  through a single 600-line client, `GitHubReviewClient`, plus several typed HTTP clients. About 8 test fakes
  subclass that concrete client.
- **No shared shape for model calls.** Twelve model and agent contracts share no request shape, usage shape or
  accounting path. The legacy ones record missing usage as zero tokens.

Moving all of that at once would have put the safety model at risk. 2A builds the mechanisms and moves one real
path onto them; 2B moves the rest behind tests that already pass.

## Decisions

- [ADR 0007](adr/0007-tool-registry.md): employees reach the world through registered tools.
  - Typed contracts live in `HireZero.Tools.*` assemblies.
  - Integrations register handlers.
  - Roles call through a per-module toolbox that enforces the manifest and a risk gate.
  - Every call is audited.
- [ADR 0008](adr/0008-model-routes.md): model routes come from one catalog. It also records the design of the single
  provider contract that 2B will implement.

## What changed

### Tool registry (`src/HireZero.Core/Tools.cs`)

- **Contracts:** `ToolContract<TInput, TOutput>` carries the id, risk and description.
- **Callers:**
  - `ToolCaller` (module, employee, task);
  - `ToolAuthority`;
  - `IToolAuthorityVerifier`.
- **Toolbox:** `ToolRegistry.For(manifest)` returns an `IToolbox`. A call is refused unless:
  - the manifest declares the tool;
  - the declared risk equals the contract's;
  - a handler is registered.
- **Risk gate:**
  - `Read` runs.
  - `Write`, `External` and `Spend` need an authority that a verifier accepts for the exact input digest. Approvals
    are meant to be consumed once, and the tests prove a reused approval is refused.
- **Audit:** every call (completed, refused or failed) records time, tool, risk, module, employee, task, input
  digest, authority, outcome and detail. A failing handler is recorded and rethrown, never retried.
- **Storage:**
  - The company store implements the audit as table `co_tool_calls`. This is company schema version 3, an
    append-only, checksummed migration.
  - `/api/company/tool-calls` serves the trail, and `/api/company` lists the registered tools.

### Git integration

- New `HireZero.Tools.Git` contracts:
  - `git.checkout-inspect` (Read);
  - `git.checkout-review` (Read).
- New `HireZero.Integrations.Git`:
  - the read-only checkout inspector moved out of `Integrations.Codex`, now `GitCheckoutInspector`;
  - its hardening is unchanged;
  - `AddGitTools`;
  - `FindGit`.
- `ICompanyWorkspaceInspector` is gone from Core.
- `CompanyDeveloperBridge` inspects and reviews checkouts through its toolbox.
- The Developer manifest now declares exactly those two tools. The earlier placeholder tool ids are gone.
- The company host registers the Git tools when it finds Git.

### Model routes

- `config/model-routes.json` maps eight purposes to model ids and display names. It is embedded in Core and read
  through `ModelRoutes`.
- `HIREZERO_MODEL_ROUTES` can point at an owner-maintained override file.
- **Hard-coded model ids removed from source: 20 lines in 7 files.** Covered:
  - company routing defaults and host fallbacks;
  - the OpenClaw gateway's default, display names, provider model list and agent model map;
  - the text-only gateway;
  - Developer's triage, validation, escalation, diff review and builder models;
  - Developer's activity messages, which now use display names from the catalog.
- **Kept as-is:** `openclaw/default` and `openai-chatgpt-responses`. These are runtime protocol constants, not model
  choices.

### Tests

- `ToolRegistryTests` (7):
  - declared reads run and are audited;
  - undeclared tools and understated risk are refused;
  - external tools need an approval for the exact input and use it once;
  - unknown authority kinds are refused;
  - failures are recorded without retry;
  - a tool registers only once.
- `ModelRoutesTests` (4):
  - company defaults come from the catalog;
  - unknown purposes are refused;
  - display and native names work;
  - a source scan proves no `.cs` file under `src` names a model id, and every purpose constant is configured.
- `ArchitectureTests`, two new rules:
  - tool contracts reference only Core;
  - every declared tool exists as a contract with the same risk.
  - Roles may now reference tool contracts, and nothing else beyond Core.
- `IntegrationPathChecks` now asserts that the company Developer's checkout inspection appears in the company
  audit trail as a completed `git.checkout-inspect` call.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 99 passed, 0 failed (86 before, plus 13 new) |
| `node --test tests/*.test.mjs` | 6 passed |
| Headless Chrome, all 7 smoke tests | all passed (run 5 seconds apart; the Phase 1 back-to-back flake did not recur) |
| Launcher tests | 14 + 14 passed |
| Web build and `format:check`; `dotnet csharpier check .` | passed |

Not verified: live model calls and a real GitHub account, both outside routine checks. The tool gate's
`Write`/`External` paths are exercised by unit tests with fictional tools. No production tool uses them yet; that
starts with GitHub push and merge in 2B.

## Next: Phase 2B

1. **`Integrations.GitHub` and the rest of Git as tools.**
   - Reads: PRs, reviews, checks, evidence, pipeline runs.
   - Local writes under execution grants: worktree, reset, commit.
   - External writes as digest-approved `External` tools: push, merge, review-request comment.
   - Test fakes become tool handlers instead of subclasses.
2. **SemaForge, Wiki, Jev and Trusted Postgres as tools.** After this, `Roles.Developer` starts no process and opens
   no HTTP client, and an architecture test can forbid `Process` and `HttpClient` in roles.
3. **Remove role vocabulary from Core:**
   - `WorkingContext` git calls;
   - `GitRemote`;
   - work-log PR fields;
   - owner-task PR classification;
   - wiki notices;
   - Codex and Claude names.
4. **One provider contract** per ADR 0008. The company settlement path becomes the single usage source, and no
   path records unknown usage as zero.
5. **Port the legacy Developer services onto skills and tools**, merge its panels into the company UI, then retire
   the legacy profiles. This needs your call on which legacy-only features to keep: Microsoft 365 intake,
   coding-agent session steering, Azure DevOps, Jev triage, tailnet multi-user access and the daily brief.
6. **MCP:** expose registered tools to external agents and consume MCP servers as integrations.
