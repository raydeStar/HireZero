namespace HireZero.Core;

// One owner-approved coding turn inside a single folder (a PR worktree). Roles depend on this contract;
// an integration (Codex today) runs it. Network access is for dependency installs and audits only.
// ResumeThreadId continues an existing thread (steering); Persistent + Name start one that shows up in the app (hand-off).
// OutputSchemaJson null means a free-text answer. AllowWrite false runs read-only.
public sealed record CodingRunRequest(string Root, string Prompt, string? OutputSchemaJson, string? Model, string Effort, bool AllowNetwork,
    string? ResumeThreadId = null, bool Persistent = false, string? Name = null, bool AllowWrite = true,
    IReadOnlyDictionary<string, string>? Environment = null);
// Type is "command-started", "command-completed" (DataJson: {command, exitCode}), "file-changed" (DataJson: {paths}),
// "warning", "progress" or "usage" (DataJson: {tokens}, the turn's total as the runner reported it). Events are what the worker actually did, not what it claims.
public sealed record CodingEvent(string Type, string Message, string DataJson, string At);
// State: queued, running, completed, failed, canceled, interrupted. An interrupted run is never replayed automatically.
public sealed record CodingRun(string Id, string State, string Root, string RequestedModel, string ReasoningEffort, string? ResolvedModel,
    string? ResultJson, string? Error, CodingEvent[] Events, string CreatedAt, string UpdatedAt, string? ThreadId = null);

public interface ICodingRunner
{
    CodingRun Start(CodingRunRequest request);
    CodingRun? Find(string id);
    void Cancel(string id);
}
public interface ICodingRunLifecycle { bool Terminated(string runId); }
// This exception is reserved for validation before a runtime process/request can be started.
public sealed class CodingStartRefusedException(string message):WorkspaceException(message);
