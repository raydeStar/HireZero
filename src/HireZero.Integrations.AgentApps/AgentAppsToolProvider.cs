using System.Text.Json;
using HireZero.Tools.AgentApps;

namespace HireZero.Integrations.AgentApps;

// The agentapps.* tools for the company workspace (docs/adr/0009-legacy-features-are-optional-integrations.md): off
// until the owner lists apps in <data root>/agent-apps.json, then an on-demand scan of those apps' sessions. No
// watcher, hooks or session store; nothing is sent to a session.
public sealed class AgentAppsToolProvider(string dataRoot, IEnumerable<ISessionSource> sources)
    : IToolProvider,
        IIntegrationStatusSource
{
    public static readonly string[] Known = ["claude", "codex"];
    private readonly ISessionSource[] _sources = [.. sources];

    private sealed record Settings(string[]? Apps);

    public string[] Enabled()
    {
        var file = Path.Combine(dataRoot, "agent-apps.json");
        try
        {
            var settings = File.Exists(file)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), CompanyJson.Options)
                : null;
            return [.. (settings?.Apps ?? []).Where(Known.Contains).Distinct()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public IntegrationStatus Status()
    {
        var apps = Enabled();
        return new(
            "agent-apps",
            "Coding-agent sessions",
            apps.Length > 0,
            apps.Length > 0
                ? "Listing " + string.Join(" and ", apps) + " sessions on request."
                : "List the apps to read (claude, codex) in agent-apps.json in the data root."
        );
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(AgentAppsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AgentAppsTools.ListSessions, ListAsync);
    }

    private async Task<SessionSummary[]> ListAsync(SessionListRequest request, CancellationToken token)
    {
        if (request.Max is < 1 or > 25)
            throw new ToolFailureException("refused", "List between 1 and 25 sessions.", 422);
        var enabled = Enabled();
        if (request.App is { } app && !enabled.Contains(app) || enabled.Length == 0)
            throw new ToolFailureException("refused", "That app is not enabled: " + Status().Reason, 409);
        var found = new List<AgentSession>();
        foreach (
            var source in _sources.Where(s => enabled.Contains(s.App) && (request.App is null || s.App == request.App))
        )
        {
            try
            {
                found.AddRange(await source.ScanAsync(token));
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                throw new ToolFailureException("unavailable", source.App + " sessions could not be listed.");
            }
        }
        return
        [
            .. found
                .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)
                .Take(request.Max)
                .Select(s => new SessionSummary(
                    s.Id,
                    s.App,
                    s.Title,
                    s.Cwd is null ? null : Path.GetFileName(s.Cwd.TrimEnd(Path.DirectorySeparatorChar, '/')),
                    s.Repository,
                    s.Branch,
                    s.Status,
                    s.UpdatedAt,
                    s.Flags
                )),
        ];
    }

    // The company workspace's provider: the installed apps' own session sources, scanned only on request.
    public static AgentAppsToolProvider Company(string dataRoot) =>
        new(dataRoot, [new ClaudeSessionSource(), new LazySource("codex", () => new CodexSessionSource())]);

    // Defers resolving the Codex command until a listing actually needs it.
    private sealed class LazySource(string app, Func<ISessionSource> create) : ISessionSource
    {
        private readonly Lazy<ISessionSource> _source = new(create);

        public string App => app;

        public Task<AgentSession[]> ScanAsync(CancellationToken token) => _source.Value.ScanAsync(token);
    }
}
