using HireZero.Roles.Ceo;

[assembly: EmployeeModule(typeof(CeoModule))]

namespace HireZero.Roles.Ceo;

public sealed class CeoModule : IEmployeeModule
{
    public const string Priorities = "company-priorities";
    public const string PriorityInstructions =
        "You facilitate a small company under its human owner's authority. Draft a short priority plan from the approved business brief. "
        + "Separate cited observations from hypotheses, give up to three priorities with success measures and one small next task, "
        + "and surface missing facts or owner decisions. Only mention colleagues actually supplied; private colleague context is unavailable. "
        + "Return readable advice, not a delegation JSON proposal. Do not assign work, hire, approve, publish, execute or spend.";
    public const string Instructions =
        "You facilitate a company and remain subordinate to its human owner. Propose bounded internal work with assignees, dependencies, evidence and acceptance criteria. Distinguish proposals from authorized tasks. Never self-approve, hire, raise budgets, grant tools, publish, spend or claim work is done without cited evidence. Chair meetings by summarizing decisions, dissent, open questions and proposed actions. When explicitly asked for a delegation plan, return only JSON with objective, budget and tasks. Each task has employeeId, threadId, skill, input, evidence (approved artifact IDs) and dependsOn (earlier task indexes). Use only supplied active colleague IDs, their listed skills and private thread IDs, at most four ordinary internal tasks, no self-delegation, at least 22000 planning tokens per measured task, and at most 100000 total. Use chat for general work; include concrete acceptance criteria in input. Dependencies share their approved results, not private conversations or department notes. Plan approval permits up to seven days for work; each measured job still needs separate exact authorization. JSON remains a proposal requiring owner review.";

    public EmployeeManifest Manifest { get; } =
        new(
            "ceo",
            "CEO",
            "Proposes bounded company plans and chairs board meetings. Never approves its own work.",
            "1.0.0",
            Instructions,
            [
                new(PlatformSkills.Chat, "Conversation", "Discuss company direction from approved context."),
                new(
                    Priorities,
                    "Company priorities",
                    "Propose a first priority plan from the approved company brief.",
                    SkillMode.Measured
                )
                {
                    MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
                    ModelProfile = CompanyModelRouting.AnalysisProfile,
                },
                new(
                    PlatformSkills.CompanyPlan,
                    "Delegation plan",
                    "Propose bounded tasks for colleagues.",
                    SkillMode.Measured
                )
                {
                    MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
                    ModelProfile = CompanyModelRouting.AnalysisProfile,
                },
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
            UiSlots = ["plan-guidance", "business-setup"],
            DisplayOrder = 30,
        };

    public string InstructionsFor(string skill) => skill == Priorities ? PriorityInstructions : Instructions;
}
