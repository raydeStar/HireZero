# Phase 2B.4 report: one model provider contract

Date: 2026-10-17. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

HireZero now has one model provider contract, `IModelProvider`, with a how-to for adding a vendor. Runtime
constraints moved into the providers that impose them. A model call or coding run whose usage wasn't reported is no
longer counted as zero tokens without any trace. The safety behavior of the company path is unchanged.

## Decision and why it differs from the plan

[ADR 0010](../adr/0010-model-provider-contract.md) supersedes the provider section of ADR 0008. ADR 0008 planned a new
abstraction to replace four. Up close, two things changed that:

- **The company contract already does what matters.** That covers:
  - exact input admission that refuses unknown overhead;
  - complete usage and receipts, or failures carrying known usage;
  - withholding tool calls and invalid output;
  - an authority watch that cancels a call when its task stops being current;
  - a bounded drain, a termination hold and late-receipt settlement;
  - the measured one-job extension.

  Replacing it would put those guarantees at risk to gain nothing.
- **The other two contracts only serve the legacy profile.** `IModelGateway` (14 call sites) and `ICodingRunner`
  (about 25) are ported or deleted in 2B.5. Migrating them now would do the work twice.

So the company contract became the platform contract. The legacy contracts are left as they are and retire with the
legacy profile.

## What changed

- **One contract, one file.** These types moved into `src/HireZero.Core/ModelProviders.cs` under new names, with
  the contract written out at the top:

  | Before | After |
  | --- | --- |
  | `ICompanyModel` | `IModelProvider` |
  | `ICompanyMeasuredModel` | `IMeasuredModelProvider` |
  | `CompanyModelCall` | `ModelCall` |
  | `CompanyModelReply` | `ModelReply` |
  | `CompanyTokenUsage` | `ModelUsage` |
  | `CompanyInputAccounting` | `ModelInputAccounting` |
  | `CompanyTransportException` | `ModelTransportException` |
  | `UnavailableCompanyModel` | `UnavailableModelProvider` |

  Only type names changed. Persisted JSON uses property names, so existing data roots are unaffected.
- **Provider how-to.** [ADDING_A_MODEL_PROVIDER.md](../ADDING_A_MODEL_PROVIDER.md) covers what each member must do
  (counting, withholding, failures, cancellation, no retries), the measured extension, routing, the one composition
  point, and the offline test matrix a new provider must pass.
- **Unreported usage is visible.**
  - The legacy budget ledger gains an `unreported` column, migrated in place on existing databases.
  - A model call without usage is recorded at a conservative estimate: input at about four characters per token,
    plus the full output cap.
  - A coding run without usage is recorded at zero tokens and flagged. Its run and minutes still count against
    limits.
  - `BudgetUse` reports the unreported count, and the budget panel shows "N without reported usage (estimated)".
- **ADR correction.** ADR 0008 said routes are overridden by a `model-routes.json` in the data root. The
  implementation uses `HIREZERO_MODEL_ROUTES` naming a file. ADR 0010 records the correction.

## Tests

`BudgetUsageTests` (3 new):

- Model calls without usage count their estimate and are flagged.
- Coding runs without usage are flagged and still count their run and minutes.
- A ledger created before the column existed is migrated in place and keeps its rows.

Every existing company check still passes unchanged, covering:

- admission and reservations;
- measured jobs;
- cancellation drain;
- termination hold;
- late receipts;
- the Codex fixture runtime.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` (clean rebuild) | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 152 passed in three consecutive full runs (149 before, plus 3 new). One earlier run had a single failure; see below. |
| Web build and format check, Node tests | passed; 6 passed |
| Headless Chrome, all 7 smoke tests; launcher tests | all passed; 14 + 14 passed |
| Legacy Developer host on a fresh data root, booted twice | both served `/api/session` and `/api/budgets`; the ledger table gained `unreported` |
| `dotnet csharpier check .` | passed |

**One flaky failure, now mitigated.** `HostSuite.Recovery` failed once in the full run right after a clean rebuild,
taking 6.5 s. Its waits for background worker states were 5 s, so it timed out under the load of six test
assemblies running in parallel. It passed in five later runs. I raised those waits to 15 s, which hides no failure:
the state is either reached or not.

Not verified: a real provider call. Live model calls are outside routine checks.

## Next: Phase 2B.5, port and retire

This is the largest remaining step. Every legacy feature moves into the company workspace as an optional
integration (ADR 0009):

- Microsoft 365 intake
- coding-agent session steering
- Azure DevOps
- Jev triage
- tailnet multi-user access
- the daily brief

The Developer's legacy services become company skills:

- PR review
- fix loop
- wiki drafts
- preparation
- Trusted Postgres

Its company coding joins the provider contract as an execution mode. The Developer panels merge into the company UI,
and modules get their own storage migrations. Then `IRole`, `roles.json`, `IModelGateway`, `UsageMeter`, the
Developer, Recovery and Held profiles and `web/src/shell` are retired, taking the remaining legacy vocabulary out of
Core with them.

I'd split 2B.5 into one sub-step per feature, each landing with its own tests, so you can review and redirect
between them.
