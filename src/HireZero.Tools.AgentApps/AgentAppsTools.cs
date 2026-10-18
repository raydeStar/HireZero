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

public static class AgentAppsTools
{
    public static readonly ToolContract<AgentAppsStatusRequest, IntegrationStatus> Status = new(
        "agentapps.status",
        ToolRisk.Read,
        "Which coding-agent apps are enabled for session listing, and why not."
    );

    public static readonly ToolContract<SessionListRequest, SessionSummary[]> ListSessions = new(
        "agentapps.sessions.list",
        ToolRisk.Read,
        "List recent coding-agent sessions on this computer: titles, folder names and status, no transcripts."
    );
}
