# 0007: Employees reach the world through registered tools

Status: Accepted (Phase 2)

## Context

ADR 0001 says roles reach the outside world only through integrations, but nothing enforced it: `Roles.Developer`
starts `git`, `gh` and `npm` processes and calls HTTP APIs itself, and Core runs `git` too. Employee manifests
declare tools with risk levels (ADR 0002) that nothing checked. Approvals exist, but each feature wires its own.

## Decision

**Tool contracts** are typed descriptors, `ToolContract<TInput, TOutput>` (`src/HireZero.Core/Tools.cs`), with an id
(`system.action`), a risk level and a description. They live in contract-only assemblies, `src/HireZero.Tools.*`,
which reference Core and nothing else. Roles and integrations both reference the contract assemblies; neither
references the other.

**Integrations implement tools** and register them in the `ToolRegistry`. Each tool id has exactly one handler.

**Roles call tools through a toolbox** (`IToolbox`), obtained for their module. A call:

1. is refused unless the module's manifest declares the tool id;
2. passes the risk gate:
   - `Read`: allowed;
   - `Write`: requires an authority (for example a current owner-approved execution grant) that a registered
     `IToolAuthorityVerifier` accepts for this exact call;
   - `External` and `Spend`: require an owner approval bound to the digest of the tool id and the exact input,
     consumed once by the verifier;
3. is recorded in the audit trail with its caller, risk, input digest, authority and outcome, whether it was allowed,
   refused or failed. In the company workspace the trail is the `co_tool_calls` table (schema version 3).

A handler's exception is recorded and rethrown unchanged; uncertain outcomes are never retried by the registry.

## Consequences

- An architecture test enforces the references: Core references no role, integration or contract assembly; tool
  contracts reference only Core; roles reference only Core and tool contracts; integrations never reference roles.
- The first tools are `git.checkout-inspect` and `git.checkout-review` (`HireZero.Tools.Git`), implemented by
  `HireZero.Integrations.Git`. The company Developer uses them instead of a Core inspector interface.
- Moving the remaining Developer git, GitHub, SemaForge, Wiki and npm calls onto tools is Phase 2B; external
  writes (push, merge, comment) become `External` tools whose owner approvals are digest-bound.
- Tools can later be exposed over MCP from the same descriptors.
