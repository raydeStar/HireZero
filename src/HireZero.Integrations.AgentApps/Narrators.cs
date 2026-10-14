using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HireZero.Integrations.Codex;

namespace HireZero.Integrations.AgentApps;

// Narratives stay with the provider that produced the session. Each runs in an empty scratch folder with no
// tools, and leaves no session behind that HireZero would list as your work.
internal static class Scratch
{
    public static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hirezero-brief");
        Directory.CreateDirectory(folder); return folder;
    }
}

// Claude: `claude -p` with every tool disabled, no session persistence, no MCP servers and no user settings
// (so the session hooks don't fire for it).
public sealed class ClaudeNarrator(string? command = null) : ISessionNarrator
{
    public string App => "claude";
    private readonly string _command = command ?? Environment.GetEnvironmentVariable("HIREZERO_CLAUDE_COMMAND") ?? ClaudeSessionDriver.ResolveCommand();
    public static string[] Arguments() =>
        ["-p", "--output-format", "text", "--tools", "", "--no-session-persistence", "--strict-mcp-config", "--setting-sources", "project", "--permission-mode", "default"];

    public async Task<string> NarrateAsync(string prompt, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var start = new ProcessStartInfo(_command) { WorkingDirectory = Scratch.Folder(), RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in Arguments()) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Claude Code could not start.");
        try
        {
            await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token); process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = (await output).Trim();
            if (process.ExitCode != 0 || text.Length == 0) throw new InvalidOperationException("Claude didn't write a summary" + ((await errors).Contains("login", StringComparison.OrdinalIgnoreCase) ? " (sign in to Claude Code)." : "."));
            return text;
        }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }
}

// Codex: an ephemeral, read-only turn in the scratch folder with no network. Ephemeral threads aren't listed as sessions.
public sealed class CodexNarrator(ICodingRunner runner) : ISessionNarrator
{
    public string App => "codex";
    public async Task<string> NarrateAsync(string prompt, CancellationToken token)
    {
        var run = runner.Start(new CodingRunRequest(Scratch.Folder(), prompt, null, null, "low", false, AllowWrite: false));
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        while (run.State is "queued" or "running")
        {
            if (DateTimeOffset.UtcNow > deadline) { runner.Cancel(run.Id); throw new InvalidOperationException("Codex took too long to summarize."); }
            await Task.Delay(1000, token);
            run = runner.Find(run.Id) ?? throw new InvalidOperationException("The Codex summary run disappeared.");
        }
        if (run.State != "completed" || run.ResultJson is null) throw new InvalidOperationException(run.Error ?? "Codex didn't write a summary.");
        using var result = JsonDocument.Parse(run.ResultJson);
        return result.RootElement.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String ? summary.GetString()!.Trim() : throw new InvalidOperationException("Codex didn't write a summary.");
    }
}
