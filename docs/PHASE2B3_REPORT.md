# Phase 2B.3 report: Core starts no processes, and the fixtures are fictional

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](ROADMAP.md).

Core no longer starts any process, and an architecture test enforces it. Its last two process calls went to the
roles and integrations they belong to. The company path no longer names a role or a model vendor. Along the way I
found real employer and personal data in the test fixtures, which conflicts with the public-repository rule in
`AGENTS.md`. It is now replaced with fictional data.

## Employer and personal data in fixtures

Tracked test files contained real-looking identifiers:

- an organization (Northwind), with its email domain and Azure DevOps organization;
- repository names (QuoteDesk, PartnerQuoteDesk, NorthwindPortal);
- people with work emails (Riley Park, Mark Hall, Dana, Avery Stone);
- a tailnet machine name.

They were present from the first commit. They were only in tests, not in source, docs or tools. No git remote is
configured, so nothing has been pushed.

They are replaced with a consistent fictional mapping that keeps the tests meaningful:

| Before | After |
| --- | --- |
| Company | Northwind, `northwind.example` |
| Azure DevOps organization | `north` (still a prefix of the company name, which the sharing rule relies on) |
| QuoteDesk | QuoteDesk |
| PartnerQuoteDesk | PartnerQuoteDesk |
| People | Riley Park, Morgan Lee, Avery Stone |
| Tailnet machine | `host-1.tail0000.ts.net` |

A sweep of tracked files for user-profile paths, your username and email, and addresses outside example or
reserved domains found nothing else. The remaining addresses are generic: `git@github.com`, URL credential
fixtures and `you@company.com`.

**Decision for you:** the old values are still in git history, in every commit on this branch. Before you publish
the repository, either rewrite history (for example `git filter-repo --replace-text` with this mapping) or publish
a fresh history. Rewriting changes every commit hash, so I have not done it.

## What changed

### Core starts no process

- **Repository context**
  - The branch, recent commits and uncommitted-change count of a project's checkout came from `git` started by
    Core's `WorkingContext`.
  - It is now the Developer's `RepositoryContext` contributor, read through the `git.read` tool. That brings the
    integration's hardening: no hooks or fsmonitor, bounded output.
  - It is registered before the Developer's PR context, so prompt order is unchanged. It remains owner-only.
- **Tailnet identity**
  - `tailscale whois --json` moved to the new `Integrations.Tailscale` (`TailscaleCli`).
  - `TailnetAccess` receives it as a function, and without one it identifies nobody.
  - This is a platform port, not an employee tool: identifying callers is infrastructure.
- **Test:** `CoreStartsNoProcesses` reads Core's metadata and fails if it references `Process` or
  `ProcessStartInfo`.

### Company path is role- and vendor-neutral

- **Task reason**
  - Blocked execution tasks are saved with `execution-unavailable`.
  - Tasks saved under the old `developer-execution-unavailable` reason are still recognized, by Core and by the
    UI.
- **Routing**
  - `CompanyModelRouting` takes its runtime from the route catalog (`runtimes` in `config/model-routes.json`), and
    its validation is generic: explicit `provider/model` routes on a named runtime.
  - "OpenAI routes on the Codex runtime" is now the Codex connection's own constraint, checked when it is
    constructed.
- **Comments:** the coding contract's comment no longer names PR worktrees or Codex.

### Deliberately left for 2B.5

The remaining vocabulary in Core belongs to legacy-only features:

- work-log PR fields;
- owner-task PR rules;
- thread-title URL rules;
- wiki notices;
- Codex and Claude session names;
- git-host parsing for boss sharing.

You chose to keep each of those features as an optional integration (ADR 0009). Moving their vocabulary now and
then moving the features later would do the work twice, so it moves with them.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 149 passed, 0 failed (146 before, plus `CoreStartsNoProcesses` and 2 repository-context tests) |
| `node --test tests/*.test.mjs`; web typecheck and build | 6 passed; passed |
| Headless Chrome, all 7 smoke tests | all passed |
| Launcher tests | 14 + 14 passed |
| Legacy Developer host booted twice on one fresh data root | both served `/api/session` and `/api/source-status`; no crash |
| `dotnet csharpier check .`, web `format:check` | passed |

**Not verified:** a real tailnet caller lookup, which needs Tailscale and a second device. `TailnetChecks` still
covers identification, caching and host matching through the injected lookup.

## Next: Phase 2B.4, one provider contract

Replace `IModelGateway`, `ICompanyModel`, `ICompanyMeasuredModel` and `ICodingRunner` with the single provider
contract designed in [ADR 0008](adr/0008-model-routes.md):

- one request and one result type;
- usage that can say "unknown" and never records zero;
- owned calls with cancel and confirmed termination;
- authorization checked immediately before dispatch.

The company reservation and settlement path becomes the single source of usage.
