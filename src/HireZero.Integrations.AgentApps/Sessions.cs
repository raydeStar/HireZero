namespace HireZero.Integrations.AgentApps;

// The coding-agent session vocabulary, owned by this integration since Phase 2B.5j took it out of Core
// (docs/adr/0009-legacy-features-are-optional-integrations.md).

// A coding session in another app (Codex, Claude Code). Only metadata and a cleaned title, never transcript content.
public sealed record AgentSession(
    string Id,
    string App,
    string ExternalId,
    string Title,
    string? Cwd,
    string? Repository,
    string? Branch,
    string Status,
    string? Origin,
    string? PrUrl,
    int? PrNumber,
    string UpdatedAt,
    string[] Flags,
    string? LastEvent = null,
    string? LastEventAt = null,
    string? SteerRunId = null,
    string? LogPath = null
);

// An app whose sessions can be listed. Implementations read metadata only and pass titles through ContentGuard.
public interface ISessionSource
{
    string App { get; }
    Task<AgentSession[]> ScanAsync(CancellationToken token);
}

// What a session did within a time window, counted from its own records. Excerpts stay inside this integration.
public sealed record SessionActivity(
    string FirstAt,
    string LastAt,
    int Turns,
    string[] Files,
    int Commands,
    int FailedCommands,
    string[] FailedSamples,
    string[] Tests,
    bool EndedWithQuestion,
    string[] Excerpts
);

public interface ISessionActivityReader
{
    string App { get; }
    Task<SessionActivity?> ReadAsync(
        AgentSession session,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken token
    );
}

// What the owner sends into a session, and what that turn may do. Edits stay inside the session's folder.
public sealed record SteerRequest(string Message, bool AllowEdits, bool AllowNetwork);

// An app that can take a follow-up in an existing session, or start a new one that shows up in that app.
public interface ISessionDriver
{
    string App { get; }
    CodingRun Send(AgentSession session, SteerRequest request);
    CodingRun Start(string folder, string title, SteerRequest request);
    CodingRun? Find(string runId);
    void Cancel(string runId);
}
