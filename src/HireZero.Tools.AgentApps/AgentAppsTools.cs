namespace HireZero.Tools.AgentApps;

// App is null for every enabled app; Max is 1 to 25.
public sealed record SessionListRequest(string? App, int Max);

// One coding-agent session's metadata: its folder name, never a full path, log or transcript.
public sealed record SessionSummary(
    string Id,
    string App,
    string Title,
    string? Folder,
    string? Repository,
    string? Branch,
    string Status,
    string UpdatedAt,
    string[] Flags
);

public sealed record AgentAppsStatusRequest;

// One local day (yyyy-MM-dd) on this computer's clock; Max is 1 to 25 sessions.
public sealed record SessionActivityRequest(string Day, int Max);

// What one session did that day, counted from its own records: no transcript and no message text. Failed samples
// are the failing commands, clipped.
public sealed record SessionDay(
    string Id,
    string App,
    string Title,
    string? Folder,
    string? Repository,
    string? Branch,
    string Status,
    string FirstAt,
    string LastAt,
    int Turns,
    string[] Files,
    int Commands,
    int FailedCommands,
    string[] FailedSamples,
    string[] Tests,
    bool EndedWithQuestion
);

// One read-only message for an existing session: the turn it starts may not edit files or use the network, and
// nothing in this contract can ask otherwise.
public sealed record SteerMessage(string SessionId, string Message);

public sealed record SteerCheck(bool Ready, string Reason);

public sealed record SteerReceipt(string RunId, string State);

public static class AgentAppsTools
{
    public static readonly ToolContract<AgentAppsStatusRequest, IntegrationStatus> Status = new(
        "agentapps.status",
        ToolRisk.Read,
        "Which coding-agent apps are enabled for session listing, and why not."
    );

    public static readonly ToolContract<SteerMessage, SteerCheck> CheckSteer = new(
        "agentapps.session.check",
        ToolRisk.Read,
        "Check whether a session can take a read-only message now (enabled, idle, quiet, no turn running)."
    );

    public static readonly ToolContract<SteerMessage, SteerReceipt> SendReadOnly = new(
        "agentapps.session.send",
        ToolRisk.External,
        "Send one read-only message (no edits, no network) into an idle coding-agent session."
    );

    public static readonly ToolContract<SessionActivityRequest, SessionDay[]> ListActivity = new(
        "agentapps.sessions.activity",
        ToolRisk.Read,
        "What each coding-agent session did on one day: turns, files changed, commands, failures and tests; no transcript."
    );

    public static readonly ToolContract<SessionListRequest, SessionSummary[]> ListSessions = new(
        "agentapps.sessions.list",
        ToolRisk.Read,
        "List recent coding-agent sessions on this computer: titles, folder names and status, no transcripts."
    );
}
