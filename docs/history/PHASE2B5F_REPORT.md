# Phase 2B.5f report: Azure DevOps as an optional integration

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

The legacy Azure DevOps integration only ever read: the owner's open pull requests and what needs them (failing or
pending checks, merge conflicts, blocking votes, open comment threads), signed in through the owner's Azure CLI with
no stored token. The company version is a faithful read-only port. There were no writes to decide on, and Azure
DevOps names are now out of Core.

## How it works

1. **Configure** `azure-devops.json` in the company data root:

   ```json
   { "organizations": ["<your organization>"], "az": "<optional full path to az.cmd>" }
   ```

   - At most five organizations, each validated.
   - Until it exists, the Integrations card shows Azure DevOps as not active, and why.
   - Sign-in uses `az login`, as before; a sign-in problem shows when you read.
2. **Check Azure DevOps pull requests** (the Developer's `ado-pulls` action; choose all, or one configured
   organization):
   - reads up to 25 of your open pull requests per organization, with their checks, threads, conflicts and votes;
   - marks the result as untrusted text, for your review;
   - only listed organizations are contacted, and there is no background watch.
3. **Pull request attention** (measured): over the approved read, it ranks what needs you, with the problem and next
   step for each. It cannot comment, vote, merge or run anything.

## What changed

- **`HireZero.Tools.AzureDevOps`** (contracts only): `azuredevops.status` and `azuredevops.pullrequests.attention`,
  both Read.
- **Integration:**
  - `AzureDevOpsToolProvider` is configured from the data root, refuses reads with the reason until configured,
    contacts only listed organizations, and enforces bounds.
  - It uses the same client and fixed `az account get-access-token` call as the legacy profile, with HTTP clients
    private to Azure DevOps, so the company profile still has no general HTTP client factory.
  - The client can take a chosen `az` path; the legacy default is unchanged.
- **Host:** registers the tools only when configured, and adds Azure DevOps to the integrations status.
- **Developer:** the `ado-pulls` action, the `pr-attention` skill and the two Read tools.
- **Out of Core:**
  - **Remote URLs:** Core now reads only GitHub-style remote URLs itself. Integrations register the formats they
    understand (`IGitOriginFormat`). The Azure DevOps format (dev.azure.com, ssh and visualstudio.com URLs) lives in
    its integration, and the legacy host registers it, so legacy behaviour is unchanged. Without a registered
    format, a `.../_git/...` URL is no longer mistaken for a GitHub origin.
  - **Sharing rule:** the rule matching abbreviated organization names now uses a flag on the origin, which the
    Azure DevOps format sets, instead of the host name.
  - **Thread titles:** they recognize any `.../_git/{repository}/pullrequest/{n}` link without naming a product.
  - **Legacy Developer:** skips non-GitHub projects where it used to skip Azure DevOps ones.
  - **Architecture test:** now also bans Azure DevOps names from Core.

## Still legacy until 2B.5j (or the brief's own phase)

The legacy watcher, brief items, project context, boss work log and owner endpoints stay with the legacy profile.
The legacy intake's notification sender list still contains a lowercase product pattern; it goes with that pipeline,
as in 2B.5d1.

## Tests

- `AzureDevOpsToolProviderTests` (2): nothing is read until organizations are listed (bad names and a missing `az`
  path count as not configured); only listed organizations are contacted, only for your own open pull requests, with
  the bearer token; an unlisted organization and an oversized read are refused before any request.
- `GitRemoteTests` (2): GitHub-style origins are read without any integration, and another host's `_git` path isn't
  mistaken for one. A mutation check confirms the second test fails without the guard.
- `Microsoft365CompanyTests` (2 more):
  - the `ado-pulls` read becomes reviewed evidence, and ranking needs it approved;
  - with Azure DevOps configured, the host registers its tools and still has no general HTTP client factory;
  - the host reports Azure DevOps as not active, with its reason, when unconfigured.
- The legacy Azure DevOps checks pass unchanged, registering the format the way the legacy host does.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 230 passed in two consecutive runs |
| CSharpier, web typecheck and format check; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** a real read. It needs `azure-devops.json`, your Azure CLI sign-in and your organization.

## Choices made (each reversible)

- **Read-only.** Commenting, voting, work items and pipelines would be new features, each a one-call publication
  under ADR 0016 and each your decision.
- **Ranking skill included,** like inbox triage.
- **No background watch** in the company workspace.
- **Legacy GitHub-origin behaviour kept.** Other hosts still parse as before, except owners starting with `_`.

## Next

2B.5g, Jev triage, then tailnet access (2B.5h) and the daily brief (2B.5i).
