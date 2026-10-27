namespace HireZero.Core;

public sealed record CompanySandboxSelection(string DocumentId, string Name);

public sealed record CompanySandboxEvidenceSelection(string ArtifactId, string Name);

public sealed record CompanySandboxInput(
    string Name,
    string Script,
    CompanySandboxSelection[] Documents,
    string[] Outputs,
    SandboxLimits Limits,
    CompanySandboxEvidenceSelection[]? Evidence = null,
    string? Tier = null
);

public sealed record CompanySandboxDocument(string DocumentId, int Version, string Digest, string Name, string Text);

public sealed record CompanySandboxEvidence(string ArtifactId, int Version, string Digest, string Name, string Text);

public sealed record CompanySandboxPremium(string PlanDigest, int MaximumSeconds);

public sealed record CompanySandboxEvidenceOption(
    string Id,
    string TaskId,
    string EmployeeName,
    string TaskName,
    int Bytes
);

// Seconds are allocated script/browser runtime, not CPU measurement or a monetary charge. A stopped job consumes its
// approved allocation; a confirmed unsent job consumes none. Unknown resources keep their allocation held.
public sealed record CompanyComputeLimits(int Jobs = 20, int Seconds = 1800, bool Paused = true);

public sealed record CompanyComputePolicy(
    string Id,
    CompanyComputeLimits Limits,
    int Version,
    DateTimeOffset UpdatedAt
);

public sealed record CompanyComputeUsage(
    string Period,
    DateTimeOffset ResetsAt,
    CompanyComputePolicy Policy,
    int Jobs,
    int AllocatedSeconds,
    int HeldJobs,
    int HeldSeconds,
    int CarriedJobs,
    int CarriedSeconds,
    BillingAllowance? SubscriptionAllowance = null,
    bool SubscriptionRequired = false
);

public sealed record CompanyComputeJobSummary(
    string Id,
    string TaskId,
    string Name,
    string Kind,
    string State,
    string? Period,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SettledAt,
    int? AllocatedSeconds,
    int HeldSeconds,
    long? WallMilliseconds,
    SandboxRelease? Release,
    string? Tier = null
);

public sealed record CompanySandboxJob(
    string Id,
    string TaskId,
    string EmployeeId,
    int TaskGeneration,
    int EmployeeGeneration,
    string Name,
    string RuntimeIdentity,
    SandboxRequest Request,
    CompanySandboxDocument[] Documents,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    string? ApprovedBy = null,
    DateTimeOffset? StartedAt = null,
    string? Period = null,
    int? PolicyVersion = null,
    bool DispatchTaken = false,
    bool Revoked = false,
    SandboxResult? Result = null,
    string? ResultDigest = null,
    SandboxRelease? Release = null,
    int? AllocatedSeconds = null,
    DateTimeOffset? SettledAt = null,
    CompanyPlanAuthority? Plan = null,
    CompanySandboxEvidence[]? Evidence = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanySandboxPremium? Premium = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyScriptScheduleOrigin? Schedule = null
);
