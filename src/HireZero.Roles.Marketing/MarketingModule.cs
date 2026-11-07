using HireZero.Roles.Marketing;
using HireZero.Tools.Advertising;
using HireZero.Tools.Analytics;
using HireZero.Tools.Crm;
using HireZero.Tools.Email;

[assembly: EmployeeModule(typeof(MarketingModule))]

namespace HireZero.Roles.Marketing;

public sealed class MarketingModule : IEmployeeModule
{
    public const string Draft = "marketing-draft";
    public const string StarterPlan = "marketing-starter-plan";
    public const string GoalDraft = "marketing-goal-draft";

    public Task<string> GatherContextAsync(string skill, IToolbox tools, DateOnly today, CancellationToken token) =>
        skill == MarketingShift.Skill
            ? MarketingSignals.GatherAsync(tools, today, token)
            : throw new WorkspaceException("This skill has no automatic read plan.", 422);

    public EmployeeManifest Manifest { get; } =
        new(
            "marketing",
            "Marketing",
            "Campaign briefs, drafts and KPI digests from your analytics, for your review. No publishing, outreach or spend.",
            "1.0.0",
            MarketingText.Instructions,
            [
                new(PlatformSkills.Chat, "Conversation", "Discuss marketing work from approved context."),
                new(
                    StarterPlan,
                    "Marketing starter plan",
                    "Prepare a practical one-week plan from approved company knowledge for owner review.",
                    SkillMode.Measured
                )
                {
                    MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
                },
                new(
                    GoalDraft,
                    "Marketing draft",
                    "Produce the marketing asset requested in the owner's goal from supplied business context.",
                    SkillMode.Measured
                )
                {
                    MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
                },
                new(
                    MarketingLadder.Skill,
                    "Goal ladder",
                    "Work out what to grow, the steps customers take and where to focus first, for owner approval.",
                    SkillMode.Measured
                )
                {
                    MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
                },
                new(
                    MarketingShift.Skill,
                    "Morning round",
                    "On an owner-approved shift: read the goal ladder, weekly numbers and recent work, choose up to three jobs for the day and draft the first.",
                    SkillMode.Measured
                )
                {
                    MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
                    ShiftContext = true,
                    ReadContextPolicy = MarketingSignals.Policy,
                },
                new(
                    Draft,
                    "Campaign draft",
                    "Draft an asset from a versioned campaign brief for internal review.",
                    SkillMode.Measured
                ),
                new(
                    MarketingAnalytics.Digest,
                    "KPI digest",
                    "Write a KPI digest from approved analytics evidence: what changed, unusual days, next checks.",
                    SkillMode.Measured
                ),
                new(
                    MarketingExperiments.Skill,
                    "A/B test plan or readout",
                    "Write an experiment plan or a result readout from approved A/B test sizing or results.",
                    SkillMode.Measured
                ),
                new(
                    MarketingAds.Review,
                    "Ads review",
                    "Review ad performance from approved ads evidence and propose changes for your approval.",
                    SkillMode.Measured
                ),
                new(
                    MarketingLifecycle.Review,
                    "Email and pipeline review",
                    "Review email and sales-pipeline results from approved evidence and propose next steps.",
                    SkillMode.Measured
                ),
                new(PlatformSkills.MeetingTurn, "Meeting turn", "Contribute to a board meeting."),
            ],
            new(
                "Marketing",
                "Marketing partner",
                "Prepare marketing drafts and recommendations for owner review.",
                [
                    new("purpose", "What does your organization do? Add verified facts or mark what is unknown."),
                    new("audience", "Who are you trying to reach? Include only what you know."),
                    new("positioning", "What makes your offering different? Mark unverified ideas as unknown."),
                    new("voice", "How should writing sound? Add your preferences."),
                    new(
                        "claims",
                        "Which claims are approved, and what supports them? State unknown if none are approved."
                    ),
                    new("constraints", "What boundaries must all Marketing employees follow?"),
                    .. MarketingLadder.Fields,
                ],
                "Draft for internal owner review. Treat missing facts as unknown. Do not invent claims, publish, send outreach, or spend."
            )
        )
        {
            Tools =
            [
                new(AnalyticsTools.Status.Id, ToolRisk.Read, "Show which analytics source is connected."),
                new(AnalyticsTools.Traffic.Id, ToolRisk.Read, "Read website traffic when you ask for analytics."),
                new(AnalyticsTools.Search.Id, ToolRisk.Read, "Read search performance when you ask for analytics."),
                new(
                    AnalyticsTools.Daily.Id,
                    ToolRisk.Read,
                    "Read your site's daily visitors and sign-ups when you fill in the weekly numbers."
                ),
                new(AdsTools.Status.Id, ToolRisk.Read, "Show which ads account is connected."),
                new(AdsTools.Performance.Id, ToolRisk.Read, "Read ad performance when you ask for it."),
                new(EmailTools.Status.Id, ToolRisk.Read, "Show which email account is connected."),
                new(EmailTools.Campaigns.Id, ToolRisk.Read, "Read email campaign results when you ask for them."),
                new(CrmTools.Status.Id, ToolRisk.Read, "Show which CRM is connected."),
                new(CrmTools.Pipeline.Id, ToolRisk.Read, "Read the sales pipeline's totals when you ask for them."),
                new(
                    AdsTools.Campaign.Id,
                    ToolRisk.Read,
                    "Read one campaign's status and budget when you propose a change."
                ),
                new(
                    AdsTools.Change.Id,
                    ToolRisk.External,
                    "Pause a campaign or set its daily budget, only as a change you approved."
                ),
            ],
            Actions =
            [
                MarketingAnalytics.Definition,
                MarketingAds.Definition,
                MarketingAdChange.Definition,
                MarketingLifecycle.Email,
                MarketingLifecycle.Pipeline,
                MarketingExperiments.Design,
                MarketingExperiments.Significance,
            ],
            Kpis =
            [
                new("drafts-approved", "Drafts approved", "count", "Drafts the owner approved without revision."),
                new("review-rounds", "Review rounds", "rounds", "Average owner review rounds per approved draft."),
            ],
            UiSlots =
            [
                "skill-input:" + Draft,
                "business-setup",
                "draft-checks:" + StarterPlan,
                "draft-checks:" + MarketingLadder.Skill,
                "draft-checks:" + MarketingShift.Skill,
                "home-card",
                "home-lead",
            ],
            DisplayOrder = 10,
        };

    public string ValidateInput(string skill, string input) =>
        skill == Draft ? new MarketingBriefPolicy().Validate(input) : input;

    public string InstructionsFor(string skill) =>
        skill switch
        {
            Draft => MarketingText.DraftInstructions,
            StarterPlan => MarketingText.StarterPlanInstructions,
            GoalDraft => MarketingText.GoalDraftInstructions,
            MarketingLadder.Skill => MarketingLadder.Instructions,
            MarketingShift.Skill => MarketingShift.Instructions + MarketingSoul.Text,
            MarketingAnalytics.Digest => MarketingAnalytics.DigestInstructions,
            MarketingExperiments.Skill => MarketingExperiments.Instructions,
            MarketingAds.Review => MarketingAds.ReviewInstructions,
            MarketingLifecycle.Review => MarketingLifecycle.ReviewInstructions,
            // Conversation replies: the employee's own voice.
            _ => MarketingText.Instructions + MarketingSoul.Text,
        };

    public Task<string> RunActionAsync(
        string action,
        IReadOnlyDictionary<string, string> input,
        IToolbox tools,
        CancellationToken token
    ) =>
        action switch
        {
            MarketingAnalytics.ReadAction => MarketingAnalytics.ReadAsync(
                input,
                tools,
                DateOnly.FromDateTime(DateTime.UtcNow),
                token
            ),
            MarketingAdChange.Action => ReadChangeAsync(input, tools, token),
            MarketingLifecycle.EmailAction => MarketingLifecycle.ReadEmailAsync(
                input,
                tools,
                DateOnly.FromDateTime(DateTime.UtcNow),
                token
            ),
            MarketingLifecycle.PipelineAction => MarketingLifecycle.ReadPipelineAsync(
                input,
                tools,
                DateOnly.FromDateTime(DateTime.UtcNow),
                token
            ),
            MarketingAds.ReadAction => MarketingAds.ReadAsync(
                input,
                tools,
                DateOnly.FromDateTime(DateTime.UtcNow),
                token
            ),
            // Pure arithmetic over the owner's numbers: no tool is called.
            MarketingExperiments.DesignAction => Task.FromResult(MarketingExperiments.DesignText(input)),
            MarketingExperiments.SignificanceAction => Task.FromResult(MarketingExperiments.SignificanceText(input)),
            _ => throw new WorkspaceException("This employee has no such action.", 404),
        };

    public Task<ActionChoice[]> ActionChoicesAsync(
        string action,
        string field,
        IToolbox tools,
        CancellationToken token
    ) =>
        (action, field) == (MarketingAdChange.Action, "campaign")
            ? MarketingAdChange.CampaignChoicesAsync(tools, DateOnly.FromDateTime(DateTime.UtcNow), token)
            : throw new WorkspaceException("This field has no choices to load.", 404);

    private static async Task<string> ReadChangeAsync(
        IReadOnlyDictionary<string, string> input,
        IToolbox tools,
        CancellationToken token
    ) => (await MarketingAdChange.ReadAsync(input["campaign"], input["change"], input["amount"], tools, token)).Text;

    // Ad changes are published, never executed (docs/adr/0033-approved-ad-changes.md).
    public async Task<object> ExecuteAsync(
        ExecutionStep step,
        string id,
        CompanyExecutionServices services,
        CancellationToken token
    )
    {
        var store = services.Store;
        if (step is ExecutionStep.Prepare or ExecutionStep.Run)
            throw new WorkspaceException("Marketing publishes only approved ad changes.");
        var taskId = step == ExecutionStep.Propose ? store.Artifact(id).TaskId : store.Publication(id).TaskId;
        if (store.Work(taskId).Skill != MarketingAdChange.Action)
            throw new WorkspaceException("Marketing publishes only approved ad changes.");
        var change = new MarketingAdChangePublication(store, services.Tools, Manifest);
        return step switch
        {
            ExecutionStep.Propose => change.Propose(id),
            ExecutionStep.Publish => await change.PublishAsync(id, token),
            _ => await change.ReconcileAsync(id, token),
        };
    }
}
