using HireZero.Roles.Developer;
using HireZero.Tools.Git;

[assembly: EmployeeModule(typeof(DeveloperModule))]

namespace HireZero.Roles.Developer;

public sealed class DeveloperModule : IEmployeeModule
{
    public const string Work = "developer-work";

    public EmployeeManifest Manifest { get; } =
        new(
            "developer",
            "Developer",
            "Engineering advice and implementation proposals; reviewed execution in an approved checkout.",
            "1.0.0",
            "Prepare internal engineering advice and implementation proposals using only supplied context. Repository descriptions do not grant access. No code runner, shell, network or publishing tools are available. Mark unverified claims and missing evidence.",
            [
                new(PlatformSkills.Chat, "Conversation", "Discuss engineering work from approved context."),
                new("developer-advice", "Engineering advice", "Prepare advice or an implementation proposal."),
                new(
                    Work,
                    "Reviewed implementation",
                    "Change files in an owner-approved checkout under a single-use execution grant.",
                    SkillMode.Execution
                ),
                new(PlatformSkills.MeetingTurn, "Meeting turn", "Contribute to a board meeting."),
            ],
            new(
                "Engineering",
                "Developer partner",
                "Prepare engineering advice and implementation proposals for owner review.",
                [
                    new("purpose", "What should engineering help achieve? Include verified facts or mark unknowns."),
                    new(
                        "architecture",
                        "Describe known systems and architecture. Names or descriptions do not grant access."
                    ),
                    new("standards", "What engineering standards should be followed? Mark unknowns explicitly."),
                    new("acceptance", "How will the owner judge successful work?"),
                    new("constraints", "What boundaries must all Engineering employees follow?"),
                ],
                "Prepare internal advice and proposals for owner review. Treat missing evidence as unknown. Repository descriptions grant no access. Do not execute code, use tools, publish, or spend."
            )
        )
        {
            Tools =
            [
                new(
                    GitTools.InspectCheckout.Id,
                    ToolRisk.Read,
                    "Confirm the approved checkout's revision and content."
                ),
                new(GitTools.ReviewCheckout.Id, ToolRisk.Read, "Show the owner a file diff after reviewed execution."),
            ],
            Kpis =
            [
                new(
                    "proposals-approved",
                    "Proposals approved",
                    "count",
                    "Implementation proposals the owner approved."
                ),
                new("diffs-accepted", "Diffs accepted", "count", "Reviewed checkout diffs the owner accepted."),
            ],
            Capabilities = new() { WorkspaceClaims = true },
            DisplayOrder = 20,
        };

    public IRole? LegacyRole => new DeveloperRole();
}
