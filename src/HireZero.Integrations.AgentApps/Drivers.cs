using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HireZero.Integrations.Codex;

namespace HireZero.Integrations.AgentApps;

// Codex: a follow-up resumes the thread; a hand-off starts a persistent, named thread that appears in the Codex app.
// Edits stay inside the session's folder; network only with edits and only when ticked.
public sealed class CodexSessionDriver(ICodingRunner runner) : ISessionDriver
{
    public string App => "codex";
    public CodingRun Send(AgentSession session, SteerRequest request) =>
        runner.Start(new CodingRunRequest(session.Cwd!, request.Message, null, null, "", request.AllowNetwork, ResumeThreadId: session.ExternalId, AllowWrite: request.AllowEdits));
    public CodingRun Start(string folder, string title, SteerRequest request) =>
        runner.Start(new CodingRunRequest(folder, request.Message, null, null, "", request.AllowNetwork, Persistent: true, Name: title, AllowWrite: request.AllowEdits));
    public CodingRun? Find(string runId) => runner.Find(runId);
    public void Cancel(string runId) => runner.Cancel(runId);
}

// Claude Code: `claude -p --resume <session>` continues a session headlessly; a hand-off starts one in the folder.
// Without "allow edits" it runs in default mode, where anything needing approval is refused; with it, acceptEdits.
// bypassPermissions, dontAsk and auto are never used.
public sealed class ClaudeSessionDriver(CodingRunStore store, string? command = null) : ISessionDriver
{
    public string App => "claude";
    private readonly string _command = command ?? Environment.GetEnvironmentVariable("HIREZERO_CLAUDE_COMMAND") ?? ResolveCommand();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    public static readonly TimeSpan MaximumRun = TimeSpan.FromMinutes(30);

    public static string ResolveCommand()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        return File.Exists(local) ? local : "claude";
    }
    public static string[] Arguments(string? resume, SteerRequest request)
    {
        var arguments = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--permission-mode", request.AllowEdits ? "acceptEdits" : "default" };
        if (resume is not null) { arguments.Add("--resume"); arguments.Add(resume); }
        arguments.Add(request.Message);
        return [.. arguments];
    }

    public CodingRun Send(AgentSession session, SteerRequest request) => Run(session.Cwd!, session.ExternalId, request);
    public CodingRun Start(string folder, string title, SteerRequest request) => Run(folder, null, request);
    public CodingRun? Find(string runId) => store.Find(runId);
    public void Cancel(string runId) { if (_active.TryGetValue(runId, out var cancel)) cancel.Cancel(); }

    private CodingRun Run(string folder, string? resume, SteerRequest request)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var run = store.Save(new CodingRun(Guid.NewGuid().ToString("N"), "queued", folder, "claude session default", request.AllowEdits ? "acceptEdits" : "default", null, null, null, [], now, now, resume));
        var cancel = new CancellationTokenSource(MaximumRun); _active[run.Id] = cancel;
        _ = Task.Run(async () => { try { await ExecuteAsync(run, folder, resume, request, cancel.Token); } finally { _active.TryRemove(run.Id, out _); cancel.Dispose(); } });
        return run;
    }

    private async Task ExecuteAsync(CodingRun run, string folder, string? resume, SteerRequest request, CancellationToken token)
    {
        var events = new List<CodingEvent>();
        void Record(string type, string message, object? data = null)
        {
            events.Add(new(type, message, JsonSerializer.Serialize(data ?? new { }), DateTimeOffset.UtcNow.ToString("O")));
            if (events.Count > 300) events.RemoveAt(0);
            run = store.Save(run with { Events = [.. events], UpdatedAt = DateTimeOffset.UtcNow.ToString("O") });
            AgentActivity.Note(message, data is not null && JsonSerializer.SerializeToElement(data) is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("command", out var raw) ? "Claude ran: " + raw.GetString() : null);
        }
        var start = new ProcessStartInfo(_command) { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in Arguments(resume, request)) start.ArgumentList.Add(argument);
        Process? process = null;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Claude Code could not start.");
            process.StandardInput.Close();
            var errors = new StringBuilder(); process.ErrorDataReceived += (_, line) => { if (line.Data is not null && errors.Length < 4000) errors.AppendLine(line.Data); }; process.BeginErrorReadLine();
            run = store.Save(run with { State = "running", UpdatedAt = DateTimeOffset.UtcNow.ToString("O") });
            Record("progress", resume is null ? "Starting Claude in " + Path.GetFileName(folder) + "." : "Continuing the Claude session.");
            var stream = new ClaudeStream();
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                foreach (var item in stream.Read(line)) Record(item.Type, item.Message, item.Data);
                if (stream.SessionId is { } id && run.ThreadId != id) run = store.Save(run with { ThreadId = id });
                if (stream.Completed) break;
            }
            await process.WaitForExitAsync(token);
            run = store.Save(run with { State = stream.Completed && stream.Failure is null ? "completed" : "failed", ResolvedModel = stream.Model ?? run.ResolvedModel,
                ResultJson = JsonSerializer.Serialize(new { summary = stream.Summary }), Error = stream.Failure ?? (stream.Completed ? null : CodexStream.Safe("Claude Code stopped before finishing. " + errors, 400)),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O") });
        }
        catch (OperationCanceledException) { store.Save(run with { State = "canceled", Error = "Stopped by the owner.", UpdatedAt = DateTimeOffset.UtcNow.ToString("O") }); }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        { store.Save(run with { State = "failed", Error = CodexStream.Safe(error.Message, 400), UpdatedAt = DateTimeOffset.UtcNow.ToString("O") }); }
        finally { try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } process?.Dispose(); }
    }
}

// Reads `claude -p --output-format stream-json`: tool calls become steps; the result line ends the turn.
public sealed class ClaudeStream
{
    public string? SessionId { get; private set; }
    public string? Model { get; private set; }
    public bool Completed { get; private set; }
    public string? Failure { get; private set; }
    public string Summary { get; private set; } = "";

    public IReadOnlyList<(string Type, string Message, object? Data)> Read(string line)
    {
        if (CodexStream.Parse(line) is not { } root) return [];
        var type = CodexStream.Text(root, "type");
        SessionId ??= CodexStream.Text(root, "session_id");
        if (type == "system") { Model ??= CodexStream.Text(root, "model"); return [("progress", "Claude session ready.", null)]; }
        if (type == "assistant" && root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var steps = new List<(string, string, object?)>();
            foreach (var block in content.EnumerateArray().Where(block => CodexStream.Text(block, "type") == "tool_use"))
            {
                var tool = CodexStream.Text(block, "name") ?? "tool";
                var detail = block.TryGetProperty("input", out var input) ? CodexStream.Text(input, "command") ?? CodexStream.Text(input, "file_path") ?? CodexStream.Text(input, "pattern") : null;
                steps.Add(tool is "Bash" or "PowerShell" ? ("command-started", CommandText.Describe(detail) + "…", new { command = CodexStream.Safe(detail, 500) })
                    : ("progress", tool + (detail is null ? "" : ": " + CodexStream.Safe(ContentGuard.Clean(detail, 200).Text, 100)), null));
            }
            return steps;
        }
        if (type != "result") return [];
        Completed = true;
        var subtype = CodexStream.Text(root, "subtype") ?? "";
        var error = root.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True || subtype.StartsWith("error", StringComparison.OrdinalIgnoreCase);
        Summary = ContentGuard.Clean(CodexStream.Text(root, "result") ?? "", 8000).Text;
        if (error) Failure = CodexStream.Safe(Summary.Length > 0 ? Summary : "Claude Code reported " + subtype + ".", 400);
        return [("progress", error ? "Claude stopped: " + Failure : "Claude finished.", null)];
    }
}
