using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Integrations.OpenClaw;

// Codex threads from OpenClaw's session catalog (sessions.catalog.list, provided by the @openclaw/codex plugin).
// The catalog is passive: listing never resumes a thread or answers an approval. Only metadata comes back.
public sealed class OpenClawCodexSessions(OpenClawCells cells) : ISessionSource
{
    public string App => "codex";

    public async Task<AgentSession[]> ScanAsync(CancellationToken token)
    {
        var result = await cells.CallAsync(IModelGateway.OwnerCell, "sessions.catalog.list", new { limitPerHost = 50 }, TimeSpan.FromSeconds(60), token);
        return Map(result);
    }

    public static AgentSession[] Map(JsonElement result)
    {
        if (!result.TryGetProperty("catalogs", out var catalogs) || catalogs.ValueKind != JsonValueKind.Array) throw new WorkspaceException("OpenClaw didn't return a session catalog.");
        var codex = catalogs.EnumerateArray().FirstOrDefault(catalog => Text(catalog, "id") == "codex");
        if (codex.ValueKind != JsonValueKind.Object) throw new WorkspaceException("OpenClaw's Codex catalog isn't available (is @openclaw/codex installed?).");
        var found = new List<AgentSession>();
        foreach (var host in codex.TryGetProperty("hosts", out var hosts) ? hosts.EnumerateArray() : default)
        {
            if (host.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.False) continue;
            foreach (var row in host.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Array ? sessions.EnumerateArray() : default)
            {
                if (Text(row, "threadId") is not { Length: > 0 } id || row.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True) continue;
                var title = ContentGuard.Clean(Text(row, "name") ?? "Codex thread", 120); var cwd = Text(row, "cwd");
                var seconds = Number(row, "recencyAt") ?? Number(row, "updatedAt") ?? 0;
                found.Add(new("codex:" + id, "codex", id, title.Text, cwd, GitRemote.Repository(cwd) ?? Folder(cwd), Text(row, "gitBranch"),
                    Text(row, "status") == "active" ? "working" : "idle", Text(row, "source"), null, null, DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("O"), title.Flags));
            }
        }
        return [.. found];
    }

    static string? Folder(string? path) => path is { Length: > 0 } ? Path.GetFileName(path.TrimEnd('\\', '/')) : null;
    static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    static long? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var result) ? result : null;
}
