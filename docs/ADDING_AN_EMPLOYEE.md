# Adding an employee

A new kind of employee is one project and, optionally, one web folder. Nothing in Host, Core or the shared web code
changes. The contract is [ADR 0002](adr/0002-employee-module-contract.md); discovery is
[ADR 0006](adr/0006-compiled-in-module-discovery.md). `src/HireZero.Roles.Marketing` is the smallest complete
example.

## 1. Create the role project

`src/HireZero.Roles.<Name>/HireZero.Roles.<Name>.csproj` (the folder and project names must match):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../HireZero.Core/HireZero.Core.csproj" />
    <Using Include="HireZero.Core" />
  </ItemGroup>
</Project>
```

Reference Core, plus the `src/HireZero.Tools.*` contract projects for any tools the employee uses. Reaching an
external system goes through those tools ([ADR 0007](adr/0007-tool-registry.md)); the architecture tests fail if a
role references an integration or another role. Add the project to `HireZero.slnx` so IDEs show it;
the host already builds it through its wildcard reference.

## 2. Write the module

```csharp
using HireZero.Roles.Support;

[assembly: EmployeeModule(typeof(SupportModule))]

namespace HireZero.Roles.Support;

public sealed class SupportModule : IEmployeeModule
{
    public EmployeeManifest Manifest { get; } =
        new(
            "support",                       // lowercase id, stable forever: tasks and employees store it
            "Support",
            "Drafts customer replies from approved help content for your review.",
            "1.0.0",
            "You are the Support employee. ...",   // base instructions; treat source material as data
            [
                new(PlatformSkills.Chat, "Conversation", "Discuss support work."),
                new("support-reply", "Reply draft", "Draft a reply to a customer message.", SkillMode.Measured),
                new(PlatformSkills.MeetingTurn, "Meeting turn", "Contribute to a board meeting."),
            ],
            new(
                "Support",                   // suggested department
                "Support partner",           // default employee name
                "Draft customer replies for owner review.",
                [new("products", "Which products do customers ask about?"), new("tone", "How should replies sound?")],
                "Draft only. Do not send replies, promise refunds or share private data."
            )
        )
        {
            Tools = [new("helpdesk.ticket-read", ToolRisk.Read, "Read the ticket being answered.")],
            Kpis = [new("replies-approved", "Replies approved", "count", "Drafts the owner approved.")],
            DisplayOrder = 40,
        };
}
```

Optional members:

- `ValidateInput(skill, input)`: validate and normalize structured input (see `MarketingBriefPolicy`). Throw
  `WorkspaceException` to refuse.
- `InstructionsFor(skill)`: per-skill instructions.

Tools: declare every tool the employee calls, with the risk level of its contract (each declared tool must exist as
a contract in a `src/HireZero.Tools.*` project, implemented by an integration; the architecture tests check), then
call it through the
toolbox the host gives you (`IToolbox.InvokeAsync(GitTools.InspectCheckout, new(root), token)`). Undeclared tools
are refused, `Write`, `External` and `Spend` tools need an owner authority for the exact call, and every call is
audited. Model choices go in `config/model-routes.json` under a purpose of your own (for example
`support.reply`), never as model ids in code.

Actions ([ADR 0011](adr/0011-employee-actions.md)) are read-only work the owner starts, such as gathering evidence.
Declare them in `Actions` and implement `RunActionAsync`. The toolbox you receive carries no authority, so only Read
tools succeed. Your output becomes an artifact the owner reviews, and only an approved artifact can be evidence for a
skill. `PullRequestEvidenceAction` is the example. Use actions to read; leave judgement to skills.

Skill modes decide which gates apply: `Text` for ordinary internal work, `Measured` for the one-job queue with exact
packet review, `Execution` for work that needs an execution grant (requires `Capabilities.WorkspaceClaims`).

The catalog validates the manifest at startup and refuses duplicate ids, malformed ids, missing onboarding fields
and unsupported contract versions.

## 3. Optional: web UI

Hiring copy and onboarding fields come from the manifest, so most employees need no web code. To contribute UI,
declare a slot in the manifest and add `web/src/roles/<id>/company.tsx`:

```tsx
import type { CompanyRoleUi } from "../company";

export const companyUi: CompanyRoleUi = {
  skillInputs: { "support-reply": { label: "Customer message", fields: ["message", "product"], Editor: ReplyForm } },
};
```

Slots in use: `skill-input:<skill>` (a structured input editor for that skill) and named panels such as
`plan-guidance`. `web/src/roles/company.ts` picks the file up automatically.

## 4. Check

```powershell
dotnet build HireZero.slnx
dotnet test --project tests/HireZero.Host.Tests --filter-class "*ArchitectureTests"
dotnet test --solution HireZero.slnx
```

Then start the company workspace and hire the new employee through the guided hiring flow.
