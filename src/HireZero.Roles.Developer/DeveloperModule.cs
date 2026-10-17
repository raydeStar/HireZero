using HireZero.Roles.Developer;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;
using HireZero.Tools.Jev;
using HireZero.Tools.Node;
using HireZero.Tools.SemaForge;
using HireZero.Tools.Wiki;

[assembly: EmployeeModule(typeof(DeveloperModule))]

namespace HireZero.Roles.Developer;

public sealed class DeveloperModule : IEmployeeModule
{
    // The manifest legacy Developer services use for their toolbox.
    public static EmployeeManifest Shared { get; } = new DeveloperModule().Manifest;

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
                new(GitCommands.Read.Id, ToolRisk.Read, "Read PR checkouts: revisions, status, diffs and files."),
                new(GitCommands.Write.Id, ToolRisk.Write, "Prepare and commit an approved fix in its own worktree."),
                new(GitCommands.Push.Id, ToolRisk.External, "Push an approved fix to its pull request branch."),
                new(GitHubCommands.Read.Id, ToolRisk.Read, "Read pull requests, reviews, checks and run logs."),
                new(
                    GitHubCommands.Write.Id,
                    ToolRisk.External,
                    "Merge an approved pull request or request a Codex review."
                ),
                new(GitHubSignInTools.Start.Id, ToolRisk.External, "Reconnect the GitHub CLI when the owner asks."),
                new(GitHubSignInTools.Status.Id, ToolRisk.Read, "Show the state of a GitHub reconnect."),
                new(
                    NodeTools.InstallPinned.Id,
                    ToolRisk.Write,
                    "Install the pinned PostgreSQL test package when the owner asks."
                ),
                new(SemaForgeTools.Read.Id, ToolRisk.Read, "Read SemaForge tasks, runs and context packets."),
                new(
                    SemaForgeTools.Write.Id,
                    ToolRisk.Write,
                    "Record tasks and scope, approve or cancel approved SemaForge runs."
                ),
                new(WikiTools.Read.Id, ToolRisk.Read, "Read Wiki knowledge and agent briefs."),
                new(WikiTools.Origin.Id, ToolRisk.Read, "Show the owner where the Wiki pilot runs."),
                new(WikiTools.Contribute.Id, ToolRisk.Write, "Propose an owner-approved knowledge draft to the Wiki."),
                new(
                    WikiTools.Publish.Id,
                    ToolRisk.External,
                    "Publish a proposed draft under the owner's automatic policy."
                ),
                new(JevTools.Classify.Id, ToolRisk.Read, "Classify a request with the owner's configured Jev key."),
                new(JevTools.Status.Id, ToolRisk.Read, "Check whether Jev is configured."),
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
