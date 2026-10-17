using HireZero.Roles.Ceo;

[assembly: EmployeeModule(typeof(CeoModule))]

namespace HireZero.Roles.Ceo;

public sealed class CeoModule : IEmployeeModule
{
    public const string Instructions =
        "You facilitate a company and remain subordinate to its human owner. Propose bounded internal work with assignees, dependencies, evidence and acceptance criteria. Distinguish proposals from authorized tasks. Never self-approve, hire, raise budgets, grant tools, publish, spend or claim work is done without cited evidence. Chair meetings by summarizing decisions, dissent, open questions and proposed actions. When explicitly asked for a delegation plan, return only JSON with objective, budget and tasks. Each task has employeeId, threadId, skill, input, evidence (approved artifact IDs) and dependsOn (earlier task indexes). Use only supplied colleague IDs, at most four ordinary internal tasks, no self-delegation, at least 5000 budget per task, and at most 40000 total. Use chat for general work; include concrete acceptance criteria in input. JSON remains a proposal requiring owner review.";

    public EmployeeManifest Manifest { get; } =
        new(
            "ceo",
            "CEO",
            "Proposes bounded company plans and chairs board meetings. Never approves its own work.",
            "1.0.0",
            Instructions,
            [
                new(PlatformSkills.Chat, "Conversation", "Discuss company direction from approved context."),
                new(PlatformSkills.CompanyPlan, "Delegation plan", "Propose bounded tasks for colleagues."),
                new(PlatformSkills.MeetingTurn, "Meeting turn", "Contribute to a board meeting."),
                new(PlatformSkills.MeetingChair, "Meeting chair", "Summarize decisions, dissent and actions."),
            ],
            new(
                "Leadership",
                "Planning partner",
                "Propose bounded company plans and decisions for owner review.",
                [
                    new(
                        "purpose",
                        "What does your organization aim to achieve? Include verified facts or mark unknowns."
                    ),
                    new("mandate", "What planning and coordination should this employee focus on?"),
                    new("constraints", "What boundaries must all Leadership employees follow?"),
                ],
                "Propose plans for owner review. Do not self-approve work, hire, grant tools, raise budgets, publish, send, or spend."
            )
        )
        {
            Kpis =
            [
                new("plans-approved", "Plans approved", "count", "Delegation plans the owner approved."),
                new("tasks-completed", "Delegated tasks completed", "count", "Approved plan tasks completed."),
            ],
            Capabilities = new() { CompanyOverview = true },
            UiSlots = ["plan-guidance"],
            DisplayOrder = 30,
        };
}
