using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Integrations.AgentApps;

// Collects what a session did inside a time window. Only counts, relative file paths and short cleaned command
// samples are kept; message text is cleaned, bounded and held only as in-memory excerpts for an optional narrative.
internal sealed class ActivityBuilder(string? cwd, DateTimeOffset from, DateTimeOffset to)
{
    const int MaxFiles = 25,
        MaxSamples = 3,
        MaxExcerpts = 6,
        ExcerptLength = 500;
    static readonly Regex TestCommand = new(
        @"\b(dotnet\s+test|dotnet\s+run\s+--project\s+\S*tests?|npm\s+(run\s+)?test|pnpm\s+test|yarn\s+test|node\s+--test|vitest|jest|pytest|go\s+test|cargo\s+test)\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50)
    );
    DateTimeOffset? _first,
        _last;
    int _turns,
        _commands,
        _failed;
    readonly List<string> _files = [],
        _failedSamples = [],
        _tests = [],
        _userExcerpts = [];
    string? _lastAssistant;

    public bool InWindow(string? stamp, out DateTimeOffset at) =>
        DateTimeOffset.TryParse(stamp, out at) && at >= from && at < to;

    public void Seen(DateTimeOffset at)
    {
        _first = _first is { } first && first < at ? first : at;
        _last = _last is { } last && last > at ? last : at;
    }

    public void Turn(string text)
    {
        _turns++;
        if (_userExcerpts.Count < MaxExcerpts / 2)
            _userExcerpts.Add("Owner: " + Clean(text, ExcerptLength));
    }

    public void Assistant(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _lastAssistant = text;
    }

    public void File(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var shown = Relative(path);
        if (_files.Count < MaxFiles && !_files.Contains(shown, StringComparer.OrdinalIgnoreCase))
            _files.Add(shown);
    }

    public void Command(string? command, bool? failed)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;
        _commands++;
        var sample = Clean(command.ReplaceLineEndings(" "), 120);
        if (failed == true)
        {
            _failed++;
            if (_failedSamples.Count < MaxSamples)
                _failedSamples.Add(sample);
        }
        if (TestCommand.IsMatch(command) && _tests.Count < 6)
            _tests.Add(
                (
                    failed switch
                    {
                        true => "failed: ",
                        false => "passed: ",
                        _ => "",
                    }
                ) + sample
            );
    }

    public SessionActivity? Build()
    {
        if (_first is null || _last is null)
            return null;
        var excerpts = new List<string>(_userExcerpts);
        if (_lastAssistant is { } last)
            excerpts.Add("Agent's last message: " + Clean(last, ExcerptLength * 2));
        return new(
            _first.Value.ToString("O"),
            _last.Value.ToString("O"),
            _turns,
            [.. _files],
            _commands,
            _failed,
            [.. _failedSamples],
            [.. _tests],
            EndsWithQuestion(_lastAssistant),
            [.. excerpts]
        );
    }

    internal static bool EndsWithQuestion(string? text) =>
        text is { } value
        && (
            value.TrimEnd().TrimEnd('*', '_', ')', '"').EndsWith('?')
            || Regex.IsMatch(
                value.Length > 400 ? value[^400..] : value,
                @"\b(should I|do you want|would you like|want me to|let me know (if|whether|which))\b",
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(50)
            )
        );

    static string Clean(string text, int max) =>
        ContentGuard.Clean(text.Length > max * 2 ? text[..(max * 2)] : text, max).Text;

    string Relative(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (cwd is { Length: > 0 } root)
        {
            var prefix = root.Replace('/', '\\').TrimEnd('\\') + "\\";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Clean(normalized[prefix.Length..], 200);
        }
        return Clean(Path.GetFileName(normalized), 200);
    }

    internal static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var item)
        && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    // Text parts of a message content array ({type: text|Text|input_text|output_text, text}) or a plain string.
    internal static string Joined(JsonElement content) =>
        content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? "",
            JsonValueKind.Array => string.Join(
                "\n",
                content
                    .EnumerateArray()
                    .Where(part =>
                        Text(part, "type") is { } type && type.EndsWith("text", StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(part => Text(part, "text") ?? "")
            ),
            _ => "",
        };

    internal static IEnumerable<string> Lines(string file)
    {
        using var reader = new StreamReader(
            new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
        );
        while (reader.ReadLine() is { } line)
            if (line.Length is > 0 and < 2_000_000)
                yield return line;
    }

    internal static JsonDocument? Parse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Codex: the thread's rollout file (path from thread/list). Completed items carry commands, file changes and messages.
public sealed class CodexActivityReader(string? sessionsRoot = null) : ISessionActivityReader
{
    public string App => "codex";
    private readonly string _root =
        sessionsRoot
        ?? Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
            "sessions"
        );
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _found = new(
        StringComparer.Ordinal
    );

    // Sources that don't report the rollout path (OpenClaw's catalog) are matched by thread id in the file name.
    public string? Locate(AgentSession session)
    {
        if (session.LogPath is { } known)
            return known;
        if (_found.TryGetValue(session.ExternalId, out var cached) && File.Exists(cached))
            return cached;
        if (
            !Directory.Exists(_root)
            || session.ExternalId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-'))
        )
            return null;
        var match = Directory
            .EnumerateFiles(_root, "rollout-*-" + session.ExternalId + ".jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (match is not null)
            _found[session.ExternalId] = match;
        return match;
    }

    public Task<SessionActivity?> ReadAsync(
        AgentSession session,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken token
    )
    {
        if (
            Locate(session) is not { } file
            || !file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(file)
        )
            return Task.FromResult<SessionActivity?>(null);
        var activity = new ActivityBuilder(session.Cwd, from, to);
        foreach (var line in ActivityBuilder.Lines(file))
        {
            token.ThrowIfCancellationRequested();
            if (!line.Contains("\"item_completed\"", StringComparison.Ordinal))
                continue;
            using var document = ActivityBuilder.Parse(line);
            if (document is null)
                continue;
            var root = document.RootElement;
            if (
                !activity.InWindow(ActivityBuilder.Text(root, "timestamp"), out var at)
                || !root.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("item", out var item)
            )
                continue;
            switch (ActivityBuilder.Text(item, "type"))
            {
                case "UserMessage":
                    var text = item.TryGetProperty("content", out var userContent)
                        ? ActivityBuilder.Joined(userContent)
                        : "";
                    if (text.TrimStart().StartsWith('<'))
                        continue; // injected context, not the owner
                    activity.Seen(at);
                    activity.Turn(text);
                    break;
                case "AgentMessage":
                    activity.Seen(at);
                    if (item.TryGetProperty("content", out var agentContent))
                        activity.Assistant(ActivityBuilder.Joined(agentContent));
                    break;
                case "CommandExecution":
                    activity.Seen(at);
                    var command = item.TryGetProperty("command", out var value)
                        ? value.ValueKind == JsonValueKind.Array
                            ? value.EnumerateArray().LastOrDefault().ToString()
                            : value.ToString()
                        : null;
                    bool? failed =
                        item.TryGetProperty("exit_code", out var code)
                        && code.ValueKind == JsonValueKind.Number
                        && code.TryGetInt32(out var exit)
                            ? exit != 0
                        : ActivityBuilder.Text(item, "status") == "failed" ? true
                        : null;
                    activity.Command(command, failed);
                    break;
                case "FileChange":
                    activity.Seen(at);
                    if (item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
                        foreach (var change in changes.EnumerateObject())
                            activity.File(change.Name);
                    break;
            }
        }
        return Task.FromResult(activity.Build());
    }
}

// Claude Code: the session's project file. Bash tool calls are commands; Edit/Write calls are files; a tool
// result marked is_error is a failure.
public sealed class ClaudeActivityReader : ISessionActivityReader
{
    public string App => "claude";
    static readonly HashSet<string> FileTools = ["Edit", "Write", "MultiEdit", "NotebookEdit"];

    public Task<SessionActivity?> ReadAsync(
        AgentSession session,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken token
    )
    {
        if (
            session.LogPath is not { } file
            || !file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(file)
        )
            return Task.FromResult<SessionActivity?>(null);
        var activity = new ActivityBuilder(session.Cwd, from, to);
        var pending = new List<(string Id, string Command)>();
        var failures = new HashSet<string>(StringComparer.Ordinal);
        var answered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in ActivityBuilder.Lines(file))
        {
            token.ThrowIfCancellationRequested();
            var isUser = line.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            var isAssistant = line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
            if (!isUser && !isAssistant)
                continue;
            using var document = ActivityBuilder.Parse(line);
            if (document is null)
                continue;
            var root = document.RootElement;
            if (
                !activity.InWindow(ActivityBuilder.Text(root, "timestamp"), out var at)
                || !root.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
            )
                continue;
            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
                continue;
            var type = ActivityBuilder.Text(root, "type");
            if (type == "user")
            {
                if (
                    content.ValueKind == JsonValueKind.Array
                    && content.EnumerateArray().Any(part => ActivityBuilder.Text(part, "type") == "tool_result")
                )
                {
                    foreach (
                        var result in content
                            .EnumerateArray()
                            .Where(part => ActivityBuilder.Text(part, "type") == "tool_result")
                    )
                        if (ActivityBuilder.Text(result, "tool_use_id") is { } id)
                        {
                            answered.Add(id);
                            if (
                                result.TryGetProperty("is_error", out var error)
                                && error.ValueKind == JsonValueKind.True
                            )
                                failures.Add(id);
                        }
                    continue;
                }
                var text = ActivityBuilder.Joined(content);
                if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith('<'))
                    continue; // command output and reminders, not the owner
                activity.Seen(at);
                activity.Turn(text);
            }
            else if (type == "assistant" && content.ValueKind == JsonValueKind.Array)
            {
                activity.Seen(at);
                foreach (var part in content.EnumerateArray())
                {
                    var partType = ActivityBuilder.Text(part, "type");
                    if (partType == "text")
                        activity.Assistant(ActivityBuilder.Text(part, "text") ?? "");
                    if (partType != "tool_use" || !part.TryGetProperty("input", out var input))
                        continue;
                    var name = ActivityBuilder.Text(part, "name") ?? "";
                    if (FileTools.Contains(name))
                        activity.File(
                            ActivityBuilder.Text(input, "file_path") ?? ActivityBuilder.Text(input, "notebook_path")
                        );
                    else if (
                        name is "Bash" or "PowerShell"
                        && ActivityBuilder.Text(input, "command") is { } command
                        && ActivityBuilder.Text(part, "id") is { } id
                    )
                        pending.Add((id, command));
                }
            }
        }
        foreach (var (id, command) in pending)
            activity.Command(command, answered.Contains(id) ? failures.Contains(id) : null);
        return Task.FromResult(activity.Build());
    }
}
