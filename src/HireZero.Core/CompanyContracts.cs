using System.Text.Json;

namespace HireZero.Core;

// The company workspace's view of an employee module. Built by Core from the module's manifest; roles never author
// it directly (docs/adr/0002-employee-module-contract.md).
public sealed record CompanyRole(
    string Id,
    string Name,
    string Instructions,
    string[] Skills,
    string[] OnboardingFields
)
{
    public Func<string, string, string>? ValidateInput { get; init; }
    public Func<string, string>? InstructionsForSkill { get; init; }
    public bool WorkspaceClaims { get; init; }
    public bool CompanyOverview { get; init; }
    public string[] ExecutionSkills { get; init; } = [];
    public string[] MeasuredSkills { get; init; } = [];
    public string[] PublishingSkills { get; init; } = [];
    public string[] PublishingActions { get; init; } = [];
    public EmployeeManifest? Manifest { get; init; }

    // A skill's or action's name as the module declares it, so the owner never reads an id.
    public string WorkName(string skill) =>
        skill == PlatformSkills.Chat
            ? "Conversation"
            : Manifest?.Skills.FirstOrDefault(s => s.Id == skill)?.Name
                ?? Manifest?.Actions.FirstOrDefault(a => a.Id == skill)?.Name
                ?? skill;

    public static CompanyRole From(IEmployeeModule module)
    {
        var manifest = module.Manifest;
        return new(
            manifest.Id,
            manifest.Name,
            manifest.Instructions,
            manifest.SkillIds(),
            [.. manifest.Onboarding.Fields.Select(f => f.Id)]
        )
        {
            ValidateInput = module.ValidateInput,
            InstructionsForSkill = module.InstructionsFor,
            WorkspaceClaims = manifest.Capabilities.WorkspaceClaims,
            CompanyOverview = manifest.Capabilities.CompanyOverview,
            ExecutionSkills = manifest.SkillIds(SkillMode.Execution),
            MeasuredSkills = manifest.SkillIds(SkillMode.Measured),
            PublishingSkills =
            [
                .. manifest.Skills.Where(s => s.Publishes).Select(s => s.Id),
                .. manifest.Actions.Where(a => a.Publishes).Select(a => a.Id),
            ],
            PublishingActions = [.. manifest.Actions.Where(a => a.Publishes).Select(a => a.Id)],
            Manifest = manifest,
        };
    }
}

public sealed record CompanyDepartment(string Id, string Name, string? Guide, string? Knowledge, int Version = 1);

public sealed record CompanyEmployee(
    string Id,
    string DepartmentId,
    string RoleId,
    string Name,
    string Responsibility,
    string State = "onboarding",
    int Version = 1,
    int Generation = 1
);

public sealed record CompanyKnowledge(
    string Id,
    string? DepartmentId,
    string Body,
    string Digest,
    string State = "draft",
    int Version = 1
);

public sealed record CompanyThread(
    string Id,
    string EmployeeId,
    string Title,
    int Version = 1,
    string Scope = "employee"
);

public sealed record CompanyMessage(
    string Id,
    string ThreadId,
    string EmployeeId,
    string Author,
    string Body,
    long Sequence
);

public sealed record CompanyNotes(string Id, string Body, int Version = 1);

public sealed record CompanyTask(
    string Id,
    string EmployeeId,
    string ThreadId,
    string Skill,
    string Input,
    string State,
    string? Reason,
    string RootId,
    string[] Evidence,
    string[] Dependencies,
    string? PlanId = null,
    string? MeetingId = null,
    string? Resource = null,
    int Version = 1,
    int Generation = 1,
    long Watermark = 0,
    string? KnowledgeId = null,
    string? CompanyKnowledgeId = null,
    string? ModelRoute = null,
    string? Runtime = null,
    string Kind = "work",
    // The owner allowed this work's execution network access when granting its checkout (off by default).
    bool AllowNetwork = false,
    // For a context draft: "company" for the company-wide facts; null for the employee's department context.
    string? ContextScope = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? ScheduleRunId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? RequestedModelRoute = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyTaskRevision? Revision = null
);

public sealed record CompanyTaskRevision(
    string SourceTaskId,
    string ArtifactId,
    int ArtifactVersion,
    string ArtifactDigest,
    string Feedback
);

public sealed record CompanyRevisionContext(
    CompanyTaskRevision Request,
    string OriginalDraft,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyTaskRevision[]? PriorFeedback = null
);

public sealed record CompanyArtifact(
    string Id,
    string TaskId,
    string EmployeeId,
    string Body,
    string Digest,
    string State,
    string Provenance,
    string? ContextId,
    int Version = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanySandboxArtifactOrigin? SandboxOrigin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyBrowserArtifactOrigin? BrowserOrigin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? ReviewFeedback = null
);

public sealed record CompanyHandoff(
    string Id,
    string ArtifactId,
    string TargetEmployeeId,
    string TargetThreadId,
    string Body,
    string Digest,
    string State = "shared",
    int Version = 1,
    string? TargetTaskId = null,
    int? ArtifactVersion = null
);

public sealed record CompanyEvidence(string Id, string Body, string Digest);

public sealed record CompanyColleague(
    string Id,
    string Name,
    string DepartmentId,
    string RoleId,
    string Responsibility,
    string State,
    string? ThreadId,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string[]? Skills = null
);

public sealed record CompanyWorkStatus(string Id, string EmployeeId, string Skill, string State);

public sealed record CompanyContext(
    string Id,
    string EmployeeId,
    string RoleId,
    string ThreadId,
    string Responsibility,
    string CompanyKnowledge,
    string DepartmentKnowledge,
    string Notes,
    CompanyMessage[] History,
    CompanyEvidence[] Evidence,
    string Input,
    string? MeetingId = null,
    CompanyColleague[]? Colleagues = null,
    CompanyWorkStatus[]? CompanyWork = null,
    string Scope = "employee",
    // For a reply: the company's integrations and which are connected (docs/adr/0046-connect-cards.md).
    string? Connections = null,
    // Exact reviewed facts and their evidence, never whole unreviewed source pages.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )] CompanyBusinessProvenance? BusinessBrief = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyDocumentRetrieval? Documents = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyMemoryRetrieval? Memory = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyRevisionContext? Revision = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyScheduledOccurrence? ScheduledOccurrence = null
);

public sealed record CompanyAttempt(
    string Id,
    string TaskId,
    string EmployeeId,
    string Phase,
    int TaskGeneration,
    int EmployeeGeneration,
    string? ContextId,
    string? Error = null,
    int Version = 1,
    string? ProviderReceipt = null,
    long? ReportedUsage = null,
    CompanyInputAdmission? Admission = null,
    ModelUsage? Usage = null,
    CompanyMeasuredJob? Measured = null
);

public sealed record CompanyBudget(string Id, long Limit, long Used = 0, long Reserved = 0, int Version = 1);

public sealed record CompanyReservation(
    string Id,
    string RootId,
    long Amount,
    string State = "reserved",
    long? Actual = null,
    int Version = 1,
    string[]? Scopes = null
);

public sealed record CompanyResourceClaim(
    string Id,
    string TaskId,
    string AttemptId,
    string EmployeeId,
    int Version = 1
);

public sealed record CompanyTaskSpec(
    string EmployeeId,
    string ThreadId,
    string Skill,
    string Input,
    string[] Evidence,
    int[] DependsOn
);

public sealed record CompanyPlanProposal(string Objective, CompanyTaskSpec[] Tasks, long Budget);

public sealed record CompanyPlanSource(string ArtifactId, int Version, string Digest);

public sealed record CompanyPlan(
    string Id,
    string CeoId,
    string Objective,
    CompanyTaskSpec[] Tasks,
    string Digest,
    string State,
    string RootId,
    long Budget,
    string[] TaskIds,
    DateTimeOffset Deadline,
    int Version = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        int? ExecutionHours = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyPlanSource? Source = null
);

public sealed record CompanyMeetingTurn(string EmployeeId, string Body, string TaskId);

public sealed record CompanyMeeting(
    string Id,
    string Title,
    string Agenda,
    string[] Participants,
    string ChairId,
    string[] Evidence,
    string State,
    string RootId,
    long Budget,
    DateTimeOffset Deadline,
    CompanyMeetingTurn[] Turns,
    string? ActiveTask,
    string? Outcome,
    string? OutcomeDigest,
    int Version = 1
);

public sealed record CompanyEvent(
    long Sequence,
    string Type,
    string Id,
    string? EmployeeId,
    string Text,
    DateTimeOffset At
)
{
    // The member who caused it, or "system" (docs/adr/0020).
    public string? Actor { get; init; }
}

// Transport fields are data. Company, owner, actor, approval authority and provider route are never accepted from the client.
public sealed record CompanyCommand(
    string RequestId,
    string Action,
    string? Id = null,
    int ExpectedVersion = 0,
    string? DepartmentId = null,
    string? RoleId = null,
    string? EmployeeId = null,
    string? ThreadId = null,
    string? Name = null,
    string? Responsibility = null,
    string? Body = null,
    string? Digest = null,
    string? Decision = null,
    string? Skill = null,
    string[]? Evidence = null,
    CompanyTaskSpec[]? Tasks = null,
    string[]? Participants = null,
    string? ChairId = null,
    long Budget = 0,
    decimal CreditAllowance = 0,
    string? Scope = null,
    bool AllowNetwork = false,
    string? ModelRoute = null
);

// Runtime selection and task purpose are independent of employee role: a conversation is ordinary text work; only
// execution skills use the coding route. Routes come from the model route catalog (docs/adr/0008-model-routes.md);
// the runtime that dispatches them decides which providers it can serve.
public sealed record CompanyModelRouting(string TextModel, string CodingModel, string Runtime)
{
    public const string AnalysisProfile = "analysis";

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string? AnalysisModel { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string[]? AdditionalTextModels { get; init; }

    public bool AllowsText(string route) =>
        route == TextModel || route == AnalysisModel || AdditionalTextModels?.Contains(route) == true;

    public CompanyModelRouting()
        : this(
            ModelRoutes.Route(ModelRoutes.CompanyText),
            ModelRoutes.Route(ModelRoutes.CompanyCoding),
            ModelRoutes.Runtime(ModelRoutes.CompanyText)
        ) { }

    public CompanyModelRouting(string textModel, string codingModel)
        : this(textModel, codingModel, ModelRoutes.Runtime(ModelRoutes.CompanyText)) { }

    public string For(bool execution, bool analysis = false) =>
        execution ? CodingModel
        : analysis ? AnalysisModel ?? TextModel
        : TextModel;

    public static string Native(string route) => ModelRoutes.Native(route);

    // Routes must be explicit "provider/model" ids on a named runtime; the runtime refuses providers it cannot serve.
    public void Validate()
    {
        if (
            string.IsNullOrWhiteSpace(Runtime)
            || new[] { TextModel, CodingModel }
                .Concat(AnalysisModel is null ? [] : new[] { AnalysisModel })
                .Concat(AdditionalTextModels ?? [])
                .Any(s =>
                    string.IsNullOrWhiteSpace(s)
                    || s.Length is < 8 or > 100
                    || s.IndexOf('/') is <= 0
                    || s.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('/' or '-' or '.'))
                )
        )
            throw new WorkspaceException("Select explicit provider/model routes on a named runtime.");
    }
}

// Why a task is blocked, as stored with the task.
public static class CompanyReasons
{
    public const string ExecutionUnavailable = "execution-unavailable";

    // Tasks saved before the reason was made role-neutral carry the older name.
    public static bool IsExecutionUnavailable(string? reason) =>
        reason is ExecutionUnavailable or "developer-execution-unavailable";
}

public static class CompanyJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Decode<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options) ?? throw new WorkspaceException("Invalid stored company record.");

    public static string Digest<T>(T value) => ContentGuard.Digest(Encode(value));
}
