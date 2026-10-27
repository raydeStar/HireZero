namespace HireZero.Core;

public sealed record CompanyBrowserInput(
    string Name,
    string[] Origins,
    BrowserStep[] Steps,
    BrowserLimits Limits,
    int Width = 1280,
    int Height = 800,
    bool Screenshots = true
);

public sealed record CompanyBrowserJob(
    string Id,
    string TaskId,
    string EmployeeId,
    int TaskGeneration,
    int EmployeeGeneration,
    string Name,
    string RuntimeIdentity,
    BrowserRequest Request,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    int ReservedSeconds,
    string? ApprovedBy = null,
    DateTimeOffset? StartedAt = null,
    string? Period = null,
    int? PolicyVersion = null,
    bool DispatchTaken = false,
    bool Revoked = false,
    BrowserExecution? Execution = null,
    string? ResultDigest = null,
    SandboxRelease? Release = null,
    int? AllocatedSeconds = null,
    DateTimeOffset? SettledAt = null,
    CompanyBrowserCaptureDeletion? CaptureDeletion = null,
    CompanyPlanAuthority? Plan = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyBrowserScheduleOrigin? Schedule = null
);

public sealed record CompanyBrowserRetainedArtifact(string Id, string Digest, string OriginDigest);

public sealed record CompanyBrowserCaptureDeletion(
    DateTimeOffset DeletedAt,
    string Reason,
    CompanyBrowserRetainedArtifact[] Artifacts
);
