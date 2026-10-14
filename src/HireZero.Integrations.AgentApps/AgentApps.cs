using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireZero.Integrations.Codex;

namespace HireZero.Integrations.AgentApps;

// Codex threads from `codex app-server` thread/list: names, folders and git info, no transcript content.
public sealed class CodexSessionSource : ISessionSource
{
    public string App => "codex";
    private readonly string _command = CodexRunner.ResolveCommand();

    public async Task<AgentSession[]> ScanAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(_command) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--stdio");
        using var process = Process.Start(start) ?? throw new WorkspaceException("Codex could not start.");
        process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
        try
        {
            await Send(process, new { method = "initialize", id = 1, @params = new { clientInfo = new { name = "hirezero", title = "HireZero", version = "0.1.0" }, capabilities = new { experimentalApi = true } } }, timeout.Token);
            await Read(process, 1, timeout.Token);
            await Send(process, new { method = "initialized", @params = new { } }, timeout.Token);
            await Send(process, new { method = "thread/list", id = 2, @params = new { limit = 50 } }, timeout.Token);
            var response = await Read(process, 2, timeout.Token);
            if (response.TryGetProperty("error", out _) || !response.TryGetProperty("result", out var result) || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new WorkspaceException("Codex didn't list its threads.");
            return [.. data.EnumerateArray().Select(Map).OfType<AgentSession>()];
        }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }

    public static AgentSession? Map(JsonElement thread)
    {
        if (Text(thread, "id") is not { } id || thread.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.ValueKind == JsonValueKind.True) return null;
        var title = ContentGuard.Clean(Text(thread, "name") ?? Text(thread, "preview") ?? "Codex thread", 120);
        var git = thread.TryGetProperty("gitInfo", out var info) && info.ValueKind == JsonValueKind.Object ? info : default;
        var seconds = Number(thread, "recencyAt") ?? Number(thread, "updatedAt") ?? 0;
        return new("codex:" + id, "codex", id, title.Text, Text(thread, "cwd"), Repository(Text(git, "originUrl")) ?? Folder(Text(thread, "cwd")), Text(git, "branch"),
            "idle", Text(thread, "source") ?? Text(thread, "originator"), null, null, DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("O"), title.Flags, LogPath: Text(thread, "path"));
    }

    // owner/name from a git remote (https or ssh); only the name is kept, never credentials.
    public static string? Repository(string? remote)
    {
        if (remote is null) return null;
        var match = Regex.Match(remote, @"[:/]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(\.git)?/?$", RegexOptions.None, TimeSpan.FromMilliseconds(50));
        return match.Success ? match.Groups[1].Value + "/" + match.Groups[2].Value : null;
    }
    internal static string? Folder(string? path) => path is { Length: > 0 } ? Path.GetFileName(path.TrimEnd('\\', '/')) : null;
    static async Task Send(Process process, object message, CancellationToken token)
    { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token); await process.StandardInput.FlushAsync(token); }
    static async Task<JsonElement> Read(Process process, int id, CancellationToken token)
    {
        while (await process.StandardOutput.ReadLineAsync(token) is { } line)
        {
            if (CodexStream.Parse(line) is not { } root) continue;
            if (root.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == id && !root.TryGetProperty("method", out _)) return root;
        }
        throw new WorkspaceException("Codex stopped before answering.");
    }
    static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    static long? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var result) ? result : null;
}

// Claude Code sessions from ~/.claude/projects: only metadata records are read (title, PR link, folder, branch);
// message lines are skipped without parsing. A file is reread only when its size or time changed.
public sealed class ClaudeSessionSource(string? projectsRoot = null) : ISessionSource
{
    public string App => "claude";
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);
    private readonly string _root = projectsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    private readonly ConcurrentDictionary<string, (long Length, DateTime Written, AgentSession? Session)> _cache = new();
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public async Task<AgentSession[]> ScanAsync(CancellationToken token)
    {
        if (!Directory.Exists(_root)) return [];
        var found = new List<AgentSession>();
        foreach (var project in Directory.EnumerateDirectories(_root))
            foreach (var file in Directory.EnumerateFiles(project, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if (Clock() - info.LastWriteTimeUtc > Window) continue;
                if (!_cache.TryGetValue(file, out var cached) || cached.Length != info.Length || cached.Written != info.LastWriteTimeUtc)
                    _cache[file] = cached = (info.Length, info.LastWriteTimeUtc, await ReadAsync(file, info.LastWriteTimeUtc, token));
                if (cached.Session is { } session)
                    found.Add(session with { Status = Clock() - info.LastWriteTimeUtc < TimeSpan.FromMinutes(2) ? "working" : "idle" });
            }
        return [.. found.OrderByDescending(session => session.UpdatedAt).Take(60)];
    }

    public static async Task<AgentSession?> ReadAsync(string file, DateTime written, CancellationToken token)
    {
        string? sessionId = null, cwd = null, branch = null, entry = null, title = null, prUrl = null, prRepository = null; int? prNumber = null;
        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        while (await reader.ReadLineAsync(token) is { } line)
        {
            // Cheap filters first: only metadata lines are parsed.
            var titleLine = line.Contains("\"type\":\"custom-title\"", StringComparison.Ordinal);
            var prLine = line.Contains("\"type\":\"pr-link\"", StringComparison.Ordinal);
            var needContext = cwd is null && line.Contains("\"cwd\":", StringComparison.Ordinal) && line.Length < 200_000;
            if (!titleLine && !prLine && !needContext) continue;
            try
            {
                using var document = JsonDocument.Parse(line); var root = document.RootElement;
                sessionId ??= Text(root, "sessionId");
                if (titleLine) title = Text(root, "customTitle") ?? title;
                if (prLine) { prUrl = Text(root, "prUrl"); prRepository = Text(root, "prRepository"); prNumber = root.TryGetProperty("prNumber", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var number) ? number : prNumber; }
                if (needContext) { cwd = Text(root, "cwd"); branch = Text(root, "gitBranch"); entry = Text(root, "entrypoint"); }
            }
            catch (JsonException) { }
        }
        if (sessionId is null) return null;
        var clean = ContentGuard.Clean(title ?? "Claude session in " + (CodexSessionSource.Folder(cwd) ?? "a project"), 120);
        var url = prUrl is not null && Uri.TryCreate(prUrl, UriKind.Absolute, out var parsed) && parsed.Host == "github.com" ? prUrl : null;
        return new("claude:" + sessionId, "claude", sessionId, clean.Text, cwd, prRepository ?? CodexSessionSource.Folder(cwd), branch, "idle", entry, url, prNumber,
            new DateTimeOffset(written, TimeSpan.Zero).ToString("O"), clean.Flags, LogPath: file);
    }
    static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}

public static class AgentAppServices
{
    public static IServiceCollection AddAgentApps(this IServiceCollection services) =>
        // A host can register a preferred Codex source under the key "codex-preferred" (OpenClaw's catalog); the direct
        // app-server reader stays as its fallback.
        services.AddSingleton<CodexSessionSource>()
            .AddSingleton<ISessionSource>(provider => provider.GetKeyedService<ISessionSource>("codex-preferred") is { } preferred
                ? new FallbackSessionSource(preferred, provider.GetRequiredService<CodexSessionSource>()) : provider.GetRequiredService<CodexSessionSource>())
            .AddSingleton<ISessionSource>(new ClaudeSessionSource())
            .AddSingleton<ISessionActivityReader, CodexActivityReader>().AddSingleton<ISessionActivityReader, ClaudeActivityReader>()
            .AddSingleton<ISessionNarrator>(new ClaudeNarrator()).AddSingleton<ISessionNarrator, CodexNarrator>()
            .AddSingleton<ISessionDriver, CodexSessionDriver>()
            .AddSingleton<ISessionDriver>(provider => new ClaudeSessionDriver(provider.GetRequiredService<CodingRunStore>()));
}
