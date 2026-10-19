using HireZero.Roles.Marketing;
using HireZero.Tools.Analytics;

[assembly: EmployeeModule(typeof(MarketingModule))]

namespace HireZero.Roles.Marketing;

public sealed class MarketingModule : IEmployeeModule
{
    public const string Draft = "marketing-draft";

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
            ],
            Actions = [MarketingAnalytics.Definition],
            Kpis =
            [
                new("drafts-approved", "Drafts approved", "count", "Drafts the owner approved without revision."),
                new("review-rounds", "Review rounds", "rounds", "Average owner review rounds per approved draft."),
            ],
            UiSlots = ["skill-input:" + Draft],
            DisplayOrder = 10,
        };

    public string ValidateInput(string skill, string input) =>
        skill == Draft ? new MarketingBriefPolicy().Validate(input) : input;

    public string InstructionsFor(string skill) =>
        skill switch
        {
            Draft => MarketingText.DraftInstructions,
            MarketingAnalytics.Digest => MarketingAnalytics.DigestInstructions,
            _ => MarketingText.Instructions,
        };

    public Task<string> RunActionAsync(
        string action,
        IReadOnlyDictionary<string, string> input,
        IToolbox tools,
        CancellationToken token
    ) =>
        action == MarketingAnalytics.ReadAction
            ? MarketingAnalytics.ReadAsync(input, tools, DateOnly.FromDateTime(DateTime.UtcNow), token)
            : throw new WorkspaceException("This employee has no such action.", 404);
}
