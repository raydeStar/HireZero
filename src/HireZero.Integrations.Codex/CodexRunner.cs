using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HireZero.Integrations.Codex;

public sealed record CodexRunnerScope(string Home,Func<bool> MayStart,string? FixtureMode=null);

// Runs one coding turn through `codex app-server` (JSON-RPC over stdio): a sandbox that can write only the
// approved folder, network only when the owner's approval included it, every interactive approval declined.
// One run at a time; runs and their events are persisted, and a restart marks an unfinished run interrupted.
public sealed class CodexRunner : ICodingRunner,ICodingRunLifecycle, IDisposable
{
    private readonly CodingRunStore _store;
    private readonly string _command;
    private readonly CodexRunnerScope? _scope;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    public static readonly TimeSpan MaximumRun = TimeSpan.FromMinutes(60);

    public CodexRunner(CodingRunStore store, string? command = null,CodexRunnerScope? scope=null,bool recoverInterrupted=true)
    {
        _store = store; _command = command ?? ResolveCommand();_scope=scope;
        if(_scope is null)ContentGuard.RegisterEnvironmentSecrets();
        foreach (var run in recoverInterrupted?store.Unfinished():[])
            store.Save(run with { State = "interrupted", Error = "The workspace restarted during this run, so it was not replayed. Ask for a fresh strategy.", UpdatedAt = Now() });
    }

    // HIREZERO_CODEX_COMMAND, else the newest Codex app CLI, else codex on PATH.
    public static string ResolveCommand()
    {
        if (Environment.GetEnvironmentVariable("HIREZERO_CODEX_COMMAND") is { Length: > 0 } configured) return configured;
        var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try
        {
            if (Directory.Exists(bin) && Directory.GetFiles(bin, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } newest) return newest;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return "codex";
    }

    public CodingRun Start(CodingRunRequest request)
    {
        if (!Directory.Exists(request.Root)) throw new WorkspaceException("The coding folder does not exist: " + request.Root);
        // Secrets this run could print (its environment, the worker's sign-in tokens) are masked wherever its output is stored.
        if(_scope is not null&&(request.ResumeThreadId is not null||request.Persistent||request.AllowNetwork||request.Environment?.Count>0||request.Model is null||Directory.Exists(Path.Combine(request.Root,".codex"))))throw new CodingStartRefusedException("Scoped coding requires a fresh ephemeral thread, exact route, no inherited environment or network, and no project runtime config.");
        ContentGuard.RegisterSecrets(request.Environment ?? new Dictionary<string, string>()); if(_scope is null)RegisterSignInSecrets(Home());
        var run = _store.Save(new CodingRun(Guid.NewGuid().ToString("N"), "queued", request.Root, request.Model ?? "thread default", request.Effort, null, null, null, [], Now(), Now(), request.ResumeThreadId));
        var cancel = new CancellationTokenSource(MaximumRun); _active[run.Id] = cancel;
        _ = Task.Run(async () =>
        {
            try { await _one.WaitAsync(cancel.Token); }
            catch (OperationCanceledException) { _store.Save(run with { State = "canceled", UpdatedAt = Now() }); _active.TryRemove(run.Id, out _); cancel.Dispose(); return; }
            try { using var execution = AgentActivity.Begin("coding:" + run.Id); await ExecuteAsync(run, request, cancel.Token); }
            finally { _one.Release(); _active.TryRemove(run.Id, out _); cancel.Dispose(); }
        });
        return run;
    }
    public CodingRun? Find(string id) => _store.Find(id);
    public bool Terminated(string runId)=>_store.Find(runId) is not null&&!_active.ContainsKey(runId);
    static string? CommandOf(object? data) =>
        data is not null && JsonSerializer.SerializeToElement(data) is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("command", out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString() : null;
    public void Cancel(string id) { if (_active.TryGetValue(id, out var cancel)) cancel.Cancel(); }

    // Roles use provider-qualified model names; the Codex protocol accepts its native model ID.
    static string? ProviderModel(string? model) => model?.StartsWith("openai/", StringComparison.OrdinalIgnoreCase) == true ? model[7..] : model;

    private async Task ExecuteAsync(CodingRun run, CodingRunRequest request, CancellationToken token)
    {
        var events = new List<CodingEvent>();
        void Record(string type, string message, object? data = null)
        {
            events.Add(new(type, message, JsonSerializer.Serialize(data ?? new { }), Now()));
            if (events.Count > 300) events.RemoveAt(0);
            run = _store.Save(run with { Events = [.. events], UpdatedAt = Now() });
            AgentActivity.Note(message, CommandOf(data) is { } raw ? "Codex ran: " + raw : null);
        }
        Process? process = null;
        string? thread = null, turnId = null;
        try
        {
            process = StartProcess(request.Root, request.Environment);
            run = _store.Save(run with { State = "running", UpdatedAt = Now() });
            Record("progress", request.ResumeThreadId is null ? "Starting Codex in " + Path.GetFileName(request.Root.TrimEnd('\\', '/')) + "." : "Reopening the Codex thread.");
            await WriteAsync(process, new { method = "initialize", id = 1, @params = new { clientInfo = new { name = "hirezero", title = "HireZero", version = "0.1.0" }, capabilities = new { experimentalApi = true } } }, token);
            Check(await ReadResponseAsync(process, 1, token));
            await WriteAsync(process, new { method = "initialized", @params = new { } }, token);
            if (request.ResumeThreadId is { } resume)
                await WriteAsync(process, new { method = "thread/resume", id = 2, @params = new { threadId = resume, cwd = request.Root, approvalPolicy = "never" } }, token);
            else
                await WriteAsync(process, new { method = "thread/start", id = 2, @params = new { model = ProviderModel(request.Model), cwd = request.Root, approvalPolicy = "never", ephemeral = !request.Persistent, serviceName = "hirezero" } }, token);
            thread = Check(await ReadResponseAsync(process, 2, token)).GetProperty("result").GetProperty("thread").GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Codex did not return a thread.");
            run = _store.Save(run with { ThreadId = thread, UpdatedAt = Now() });
            if (request.Name is { Length: > 0 } name && request.ResumeThreadId is null)
            {
                await WriteAsync(process, new { method = "thread/name/set", id = 4, @params = new { threadId = thread, name } }, token);
                Check(await ReadResponseAsync(process, 4, token));
            }
            var turnParameters = new Dictionary<string, object?>
            {
                ["threadId"] = thread, ["input"] = new[] { new { type = "text", text = request.Prompt } }, ["cwd"] = request.Root,
                ["approvalPolicy"] = "never", ["summary"] = "concise",
                ["sandboxPolicy"] = request.AllowWrite
                    ? new Dictionary<string, object?> { ["type"] = "workspaceWrite", ["writableRoots"] = new[] { request.Root }, ["networkAccess"] = request.AllowNetwork }
                    : new Dictionary<string, object?> { ["type"] = "readOnly" }
            };
            if (request.Model is not null) turnParameters["model"] = ProviderModel(request.Model);
            if (request.Effort is { Length: > 0 }) turnParameters["effort"] = request.Effort;
            if (request.OutputSchemaJson is not null) turnParameters["outputSchema"] = JsonSerializer.Deserialize<JsonElement>(request.OutputSchemaJson);
            await WriteAsync(process, new { method = "turn/start", id = 3, @params = turnParameters }, token);
            var turn = Check(await ReadResponseAsync(process, 3, token)).GetProperty("result").GetProperty("turn");
            turnId = CodexStream.Text(turn, "id") ?? throw new InvalidOperationException("Codex did not return a turn id.");
            run = _store.Save(run with { ResolvedModel = CodexStream.Text(turn, "model") ?? ProviderModel(request.Model), UpdatedAt = Now() });
            Record("progress", request.ResumeThreadId is null && request.OutputSchemaJson is not null ? "Sol is working on the fix." : "Codex is working on your message.");
            var stream = new CodexStream(turnId, thread);
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(token);
                if (line is null) break;
                var step = stream.Read(line);
                if (step.Reply is { } reply) await WriteAsync(process, reply, token);
                foreach (var item in step.Events) Record(item.Type, item.Message, item.Data);
                if (stream.Completed)
                {
                    run = _store.Save(run with { State = stream.Interrupted ? "canceled" : stream.Failure is null ? "completed" : "failed",
                        ResultJson = stream.Result(structured: request.OutputSchemaJson is not null), Error = stream.Failure, UpdatedAt = Now() });
                    return;
                }
            }
            throw new InvalidOperationException("Codex exited before finishing. " + Captured(process));
        }
        catch (OperationCanceledException)
        {
            // Ask Codex to stop the turn cleanly, so the thread is left consistent for the app, before ending the process.
            if (process is not null && thread is not null && turnId is not null && !process.HasExited)
                try
                {
                    using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await WriteAsync(process, new { method = "turn/interrupt", id = 9, @params = new { threadId = thread, turnId } }, grace.Token);
                    while (await process.StandardOutput.ReadLineAsync(grace.Token) is { } line && !line.Contains("\"turn/completed\"", StringComparison.Ordinal)) { }
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException) { }
            _store.Save(run with { State = "canceled", Error = run.CreatedAt is { } && DateTimeOffset.UtcNow - DateTimeOffset.Parse(run.CreatedAt) >= MaximumRun
                ? "The coding run exceeded " + MaximumRun.TotalMinutes + " minutes." : "Canceled by the owner.", UpdatedAt = Now() });
        }
        catch (Exception error) when (error is WorkspaceException or InvalidOperationException or IOException or JsonException or TimeoutException or KeyNotFoundException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            _store.Save(run with { State = "failed", Error = CodexStream.Safe(error.Message, 400) + (_scope is not null||SignedIn() ? "" : " " + SignInHint), UpdatedAt = Now() });
        }
        finally
        {
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
    }

    private Process StartProcess(string root, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(_command) { WorkingDirectory = root, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false) };
        string home;
        if(_scope is null)home=PrepareHome();
        else
        {
            if(!_scope.MayStart())throw new WorkspaceException("Scoped execution authority changed before process start.");
            home=Path.GetFullPath(_scope.Home);Directory.CreateDirectory(home);
            if(File.Exists(Path.Combine(home,"auth.json"))||Directory.Exists(Path.Combine(home,"sessions")))throw new WorkspaceException("Scoped fixture home must contain no authentication or session history.");
            var config=Path.Combine(home,"config.toml");if(File.Exists(config)&&File.ReadAllText(config)!=GeneratedConfig)throw new WorkspaceException("Unreviewed scoped runtime configuration is refused.");
            File.WriteAllText(config,GeneratedConfig);
            var essential=new[]{"SystemRoot","WINDIR","ComSpec","PATHEXT","PATH"}.ToDictionary(key=>key,key=>Environment.GetEnvironmentVariable(key));
            start.Environment.Clear();foreach(var pair in essential)if(pair.Value is not null)start.Environment[pair.Key]=pair.Value;
            foreach(var key in new[]{"HOME","USERPROFILE","APPDATA","LOCALAPPDATA","TEMP","TMP"}){var directory=Path.Combine(home,key.ToLowerInvariant());Directory.CreateDirectory(directory);start.Environment[key]=directory;}
            start.Environment["GIT_CONFIG_NOSYSTEM"]="1";start.Environment["GIT_CONFIG_GLOBAL"]=Path.Combine(home,"empty-git-config");
            if(_scope.FixtureMode is not null)start.Environment["HIREZERO_FIXTURE_MODE"]=_scope.FixtureMode;
        }
        foreach (var argument in Arguments(root, home)) start.ArgumentList.Add(argument);
        start.Environment["CODEX_HOME"] = home;
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) start.Environment[key] = value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Codex could not start: " + _command);
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) lock (errors) if (errors.Length < 8000) errors.AppendLine(line.Data); };
        process.BeginErrorReadLine();
        _errors[process] = errors;
        return process;
    }
    // Coding runs use their own Codex home, so the desktop app's config, plugins, MCP servers and computer-use runtime
    // never load into the worker. Sign this home in once (see SignInHint). HIREZERO_CODEX_HOME selects another
    // folder; it must not be under a temp folder.
    public static string Home() => Environment.GetEnvironmentVariable("HIREZERO_CODEX_HOME") is { Length: > 0 } configured ? configured
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hirezero", "codex-home");
    public static bool SignedIn() => File.Exists(Path.Combine(Home(), "auth.json"));
    // The token values in the worker home's auth.json (secret-named fields only), held in memory for masking; never logged.
    public static void RegisterSignInSecrets(string home)
    {
        try
        {
            var path = Path.Combine(home, "auth.json"); if (!File.Exists(path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            void Walk(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                    foreach (var property in element.EnumerateObject())
                        if (property.Value.ValueKind == JsonValueKind.String) { if (ContentGuard.SecretNamed(property.Name)) ContentGuard.RegisterSecret(property.Value.GetString()); }
                        else Walk(property.Value);
            }
            Walk(document.RootElement);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public static string SignInHint => "HireZero's Codex home is not signed in. Run once in PowerShell: $env:CODEX_HOME='" + Home() + "'; codex login";
    // The worker uses Codex's unelevated Windows sandbox (a restricted token; no admin setup). The elevated mode the
    // desktop app may use needs its own setup helper and, started from this host, fails every command ("setup refresh
    // had errors"), which showed up as exit -1 on each command Sol ran. A config this runner generated earlier is
    // upgraded in place; a file the owner wrote (no HireZero marker, or the current one) is left alone.
    public const string ConfigMarker = "# HireZero's own Codex home (v2): no plugins, MCP servers or desktop runtimes.";
    public const string GeneratedConfig = ConfigMarker + "\n[windows]\nsandbox = \"unelevated\"\n";
    public static string PrepareHome(string? home = null)
    {
        home ??= Home(); Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml");
        if (!File.Exists(config) || (File.ReadLines(config).FirstOrDefault() is { } first && first.StartsWith("# HireZero's own Codex home", StringComparison.Ordinal) && first != ConfigMarker))
            File.WriteAllText(config, GeneratedConfig);
        return home;
    }
    private readonly ConditionalWeakTableLike _errors = new();
    private string Captured(Process process) => _errors.TryGet(process) is { } buffer ? CodexStream.Safe(buffer.ToString(), 400) : "";

    // MCP servers and plugins that the worker's own home or the checkout defines are switched off (section names
    // only; values are never read). The owner's ~/.codex config is not consulted: it never loads under CODEX_HOME,
    // and an `enabled=false` override for a server the home does not define makes Codex reject its config and exit.
    public static string[] Arguments(string root, string home)
    {
        var arguments = new List<string> { "app-server", "--stdio" };
        var pattern = new Regex(@"^\[(mcp_servers|plugins)\.(?:""([^""]+)""|([A-Za-z0-9_-]+))\]$", RegexOptions.CultureInvariant);
        var sections = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { Path.Combine(home, "config.toml"), Path.Combine(root, ".codex", "config.toml") }.Where(File.Exists))
            foreach (var line in File.ReadLines(path))
            {
                var match = pattern.Match(line.Trim()); if (!match.Success) continue;
                var name = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                sections.Add(match.Groups[1].Value + "." + (Regex.IsMatch(name, "^[A-Za-z0-9_-]+$") ? name : "\"" + name.Replace("\"", "\\\"") + "\"") + ".enabled=false");
            }
        foreach (var section in sections) { arguments.Add("-c"); arguments.Add(section); }
        return [.. arguments];
    }

    // A write to a Codex that already exited (a config it rejected, a missing CLI feature) raises a bare pipe error;
    // report Codex's own stderr instead.
    private async Task WriteAsync(Process process, object message, CancellationToken token)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
        }
        catch (IOException) when (process.HasExited) { throw new InvalidOperationException(Exited(process)); }
    }
    private string Exited(Process process)
    {
        var captured = Captured(process);
        return "Codex exited (code " + process.ExitCode + ") before the run could start." + (captured.Length > 0 ? " Codex said: " + captured : "");
    }
    private async Task<JsonElement> ReadResponseAsync(Process process, int id, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            string? line;
            try { line = await process.StandardOutput.ReadLineAsync(timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Codex did not answer request " + id + " within 60 seconds."); }
            if (line is null) break;
            if (CodexStream.Parse(line) is not { } root) continue;
            if (root.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == id && !root.TryGetProperty("method", out _))
                return root;
        }
        process.WaitForExit(2000);
        throw new InvalidOperationException(process.HasExited ? Exited(process) : "Codex stopped before answering request " + id + ".");
    }
    private static JsonElement Check(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? throw new InvalidOperationException("Codex refused: " + CodexStream.Safe(error.GetRawText(), 300)) : response;
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    public void Dispose() { foreach (var cancel in _active.Values) cancel.Cancel(); }

    // Minimal process-to-buffer map without keeping exited processes alive.
    private sealed class ConditionalWeakTableLike
    {
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Process, StringBuilder> _table = new();
        public StringBuilder this[Process process] { set => _table.AddOrUpdate(process, value); }
        public StringBuilder? TryGet(Process process) => _table.TryGetValue(process, out var value) ? value : null;
    }
}

// Reads Codex app-server notifications. Null-safe by construction: an in-progress command has exitCode null.
public sealed class CodexStream(string? expectedTurnId = null, string? expectedThreadId = null)
{
    public sealed record Step(IReadOnlyList<(string Type, string Message, object? Data)> Events, object? Reply);
    private readonly StringBuilder _final = new();
    public bool Completed { get; private set; }
    public long? Tokens { get; private set; }
    public bool Interrupted { get; private set; }
    public string? Failure { get; private set; }

    public static JsonElement? Parse(string line)
    {
        try { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
    public static string? Text(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    public static int? Number(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var result) ? result : null;
    public static string Safe(string? value, int maximum)
    {
        var text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maximum ? text : text[..maximum];
    }

    public Step Read(string line)
    {
        if (Parse(line) is not { } root || Text(root, "method") is not { } method) return new([], null);
        var parameters = root.TryGetProperty("params", out var value) ? value : default;
        // A request (has an id) wants a decision: the worker never gets more than its approval.
        if (root.TryGetProperty("id", out var id))
        {
            object result = method switch
            {
                "item/permissions/requestApproval" => new { permissions = Array.Empty<object>(), scope = "turn" },
                "item/tool/requestUserInput" => new { answers = (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array
                    ? questions.EnumerateArray() : default(JsonElement.ArrayEnumerator)).Select(question => Text(question, "id")).OfType<string>()
                    .ToDictionary(key => key, _ => (object)new { answers = new[] { "Do not pause. Put the question in the structured questions output and stop before any change that needs it." } }) },
                _ => new { decision = "decline" }
            };
            return new([("warning", method == "item/tool/requestUserInput" ? "Sol raised a question; it will be returned with the result." : "Declined a request for more access than you approved.", new { method })],
                new Dictionary<string, object?> { ["id"] = id.Clone(), ["result"] = result });
        }
        // A resumed thread may still emit earlier-turn notifications. Neither its output nor another
        // thread's commands, errors or usage are evidence for this run. Decisions above still decline access.
        var notificationThread = Text(parameters, "threadId");
        var notificationTurn = Text(parameters, "turnId");
        if ((expectedThreadId is not null && notificationThread is not null && notificationThread != expectedThreadId) ||
            (expectedTurnId is not null && notificationTurn is not null && notificationTurn != expectedTurnId)) return new([], null);
        if ((method is "item/agentMessage/delta" or "item/started" or "item/completed" or "error" or "thread/tokenUsage/updated") &&
            ((expectedThreadId is not null && notificationThread is null) || (expectedTurnId is not null && notificationTurn is null))) return new([], null);
        switch (method)
        {
            case "item/agentMessage/delta":
                if (Text(parameters, "delta") is { Length: > 0 } delta) _final.Append(delta);
                return new([], null);
            case "item/started" or "item/completed":
                var item = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("item", out var inner) ? inner : default;
                var type = Text(item, "type") ?? "";
                if (type.Equals("commandExecution", StringComparison.OrdinalIgnoreCase))
                {
                    var command = Safe(Text(item, "command") ?? "local command", 500);
                    return method == "item/started"
                        ? new([("command-started", CommandText.Describe(command) + "…", new { command })], null)
                        : new([("command-completed", CommandText.Finished(command, (int?)Number(item, "exitCode")), new { command, exitCode = Number(item, "exitCode") })], null);
                }
                if (type.Contains("file", StringComparison.OrdinalIgnoreCase) && method == "item/completed")
                    return new([("file-changed", "Changed files in the worktree.", new { paths = Paths(item) })], null);
                if (type.Equals("agentMessage", StringComparison.OrdinalIgnoreCase) && method == "item/completed" && _final.Length == 0 && Text(item, "text") is { } text)
                    _final.Append(text);
                return new([], null);
            case "thread/tokenUsage/updated":
                // The thread's running total, for the budget ledger (a resumed thread includes its earlier turns).
                if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("tokenUsage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
                    usage.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Object && total.TryGetProperty("totalTokens", out var tokens) && tokens.TryGetInt64(out var count))
                    Tokens = count;
                return new([], null);
            case "error":
                Failure = Safe(Text(parameters, "message") ?? (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("error", out var error) ? Text(error, "message") ?? error.GetRawText() : "Codex reported an error."), 400);
                return new([("warning", "Codex reported: " + Failure, null)], null);
            case "turn/completed":
                var turn = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("turn", out var completed) ? completed : default;
                var reportedId = Text(turn, "id");
                if (expectedTurnId is not null && reportedId is not null && reportedId != expectedTurnId) return new([], null);
                var status = Text(turn, "status");
                Completed = true; Interrupted = string.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase);
                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                    Failure ??= Safe(turn.ValueKind == JsonValueKind.Object && turn.TryGetProperty("error", out var turnError) ? Text(turnError, "message") ?? turnError.GetRawText() : "The coding turn failed.", 400);
                else if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) || Interrupted) Failure = null;
                else Failure = "Codex returned an unsupported terminal status.";
                if (expectedTurnId is not null && reportedId is null) Failure = "Codex completion did not identify the expected turn.";
                if (expectedThreadId is not null && notificationThread is null) Failure = "Codex completion did not identify the expected thread.";
                (string, string, object?) done = ("progress", Interrupted ? "Codex stopped the turn." : Failure is null ? "Sol finished; checking the result." : "The coding turn failed: " + Failure, null);
                return Tokens is { } used ? new([done, (BudgetLedger.CodingUsage, "Used " + used.ToString("N0") + " tokens.", new { tokens = used })], null) : new([done], null);
            default:
                return new([], null);
        }
    }

    // The structured answer, or the plain text wrapped as a summary when it isn't JSON. Free-text turns (steering)
    // are cleaned and wrapped as a plain summary.
    public string Result(bool structured = true)
    {
        var text = _final.ToString().Trim();
        if (!structured) return JsonSerializer.Serialize(new { summary = ContentGuard.Clean(text, 8000).Text });
        if (text.StartsWith("```")) text = text.Trim('`').Trim(); if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        if (Parse(text) is { ValueKind: JsonValueKind.Object }) return text;
        if (Trailing(text) is { } answer) return answer;
        return JsonSerializer.Serialize(new { summary = Safe(text, 4000), changedFiles = Array.Empty<string>(), tests = Array.Empty<string>(), risks = new[] { "The worker did not return a structured result." }, questions = Array.Empty<string>() });
    }

    // The first position from which the rest of the text is one JSON object with a summary: commentary before it is dropped.
    public static string? Trailing(string text)
    {
        var body = text.TrimEnd().TrimEnd('`').TrimEnd();
        for (var index = body.IndexOf('{'); index >= 0; index = body.IndexOf('{', index + 1))
            if (Parse(body[index..]) is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("summary", out _)) return body[index..];
        return null;
    }

    private static string[] Paths(JsonElement item)
    {
        var paths = new List<string>();
        if (item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
            foreach (var change in changes.EnumerateArray()) if (Text(change, "path") is { } path) paths.Add(Safe(path, 300));
        if (Text(item, "path") is { } single) paths.Add(Safe(single, 300));
        return [.. paths.Distinct().Take(50)];
    }
}

public sealed class CodingRunStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public CodingRunStore(string path)
    {
        _path = path;
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS coding_runs(id TEXT PRIMARY KEY, state TEXT NOT NULL, payload TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    // Every stored run is masked first (events, result, error), whichever runner wrote it.
    public static CodingRun Masked(CodingRun run) => run with
    {
        ResultJson = run.ResultJson is null ? null : ContentGuard.MaskJson(run.ResultJson), Error = run.Error is null ? null : ContentGuard.Mask(run.Error),
        Events = [.. run.Events.Select(item => item with { Message = ContentGuard.Mask(item.Message), DataJson = ContentGuard.MaskJson(item.DataJson) })]
    };
    public CodingRun Save(CodingRun run)
    {
        run = Masked(run);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO coding_runs VALUES($id,$state,$payload) ON CONFLICT(id) DO UPDATE SET state=excluded.state,payload=excluded.payload";
        command.Parameters.AddWithValue("$id", run.Id); command.Parameters.AddWithValue("$state", run.State);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(run, Json));
        command.ExecuteNonQuery(); return run;
    }
    public CodingRun? Find(string id) => Query("WHERE id=$id", id).SingleOrDefault();
    public CodingRun[] Unfinished() => Query("WHERE state IN ('queued','running')", null);
    private CodingRun[] Query(string where, string? id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM coding_runs " + where;
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader(); var results = new List<CodingRun>();
        while (reader.Read()) results.Add(JsonSerializer.Deserialize<CodingRun>(reader.GetString(0), Json)!);
        return [.. results];
    }
}

public static class CodexServices
{
    public static IServiceCollection AddCodex(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new CodingRunStore(databasePath));
        return services.AddSingleton<ICodingRunner>(provider => new CodexRunner(provider.GetRequiredService<CodingRunStore>()));
    }
}
