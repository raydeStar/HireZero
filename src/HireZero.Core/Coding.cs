namespace HireZero.Core;

// One owner-approved coding turn inside a single folder. Roles depend on this contract; a runtime integration
// runs it. Network access is for dependency installs and audits only.
// ResumeThreadId continues an existing thread (steering); Persistent + Name start one that shows up in the app (hand-off).
// OutputSchemaJson null means a free-text answer. AllowWrite false runs read-only.
// ReadRoots are extra folders the run may read but not change. InputFiles maps a variable name to a small file's text: the
// runner writes each file where the run can read but not change it and passes its path in that variable.
public sealed record CodingRunRequest(
    string Root,
    string Prompt,
    string? OutputSchemaJson,
    string? Model,
    string Effort,
    bool AllowNetwork,
    string? ResumeThreadId = null,
    bool Persistent = false,
    string? Name = null,
    bool AllowWrite = true,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyList<string>? ReadRoots = null,
    IReadOnlyDictionary<string, string>? InputFiles = null
);

// Type is "command-started", "command-completed" (DataJson: {command, exitCode}), "file-changed" (DataJson: {paths}),
// "warning", "progress" or "usage" (DataJson: {tokens}, the turn's total as the runner reported it). Events are what the worker actually did, not what it claims.
public sealed record CodingEvent(string Type, string Message, string DataJson, string At);

public static class CodingEvents
{
    // The turn's total token usage, as the runner reported it.
    public const string Usage = "usage";
}

// State: queued, running, completed, failed, canceled, interrupted. An interrupted run is never replayed automatically.
public sealed record CodingRun(
    string Id,
    string State,
    string Root,
    string RequestedModel,
    string ReasoningEffort,
    string? ResolvedModel,
    string? ResultJson,
    string? Error,
    CodingEvent[] Events,
    string CreatedAt,
    string UpdatedAt,
    string? ThreadId = null
);

public interface ICodingRunner
{
    CodingRun Start(CodingRunRequest request);
    CodingRun? Find(string id);
    void Cancel(string id);
}

public interface ICodingRunLifecycle
{
    bool Terminated(string runId);
}

// Process exit and removal of temporary data are separate facts. Neither may be inferred from a model reply.
public sealed record CodingReleaseReceipt(
    bool ProcessStopped,
    bool TemporaryFilesRemoved,
    string Status,
    DateTimeOffset RecordedAt,
    string? ResourceId = null
);

public interface ICodingRunCleanup
{
    CodingReleaseReceipt? ReleaseReceipt { get; }
}

// This exception is reserved for validation before a runtime process/request can be started.
public sealed class CodingStartRefusedException(string message) : WorkspaceException(message);
