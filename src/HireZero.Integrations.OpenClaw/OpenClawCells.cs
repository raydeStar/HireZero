using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.OpenClaw;

public sealed class OpenClawCells : IDisposable, IModelGateway
{
    private const string OwnerCell = IModelGateway.OwnerCell;
    public const string Model = "openai/gpt-6-luna";
    public string DefaultModel => Model;
    private const string ReasoningEffort = "high";
    public string Reasoning => ReasoningEffort;
    private readonly string _root;
    private readonly string _node;
    private readonly string _entry;
    private readonly IHttpClientFactory _clients;
    private readonly ConcurrentDictionary<string, Cell> _cells = new(StringComparer.Ordinal);

    public OpenClawCells(string root, IHttpClientFactory clients)
    {
        _root = Path.Combine(root, "OpenClaw");
        Directory.CreateDirectory(_root);
        SecureDirectory(_root);
        var runtime = Path.Combine(root, "Runtime");
        _node = Environment.GetEnvironmentVariable("HIREZERO_OPENCLAW_NODE")
            ?? Path.Combine(runtime, "node", "node.exe");
        _entry = Environment.GetEnvironmentVariable("HIREZERO_OPENCLAW_ENTRY")
            ?? Path.Combine(runtime, "openclaw", "node_modules", "openclaw", "openclaw.mjs");
        _clients = clients;
    }

    public void Prepare(string login) => _cells.GetOrAdd(login, CreateCell);
    private static string Friendly(string model) => model switch
    {
        "openai/gpt-6-luna" => "Luna", "openai/gpt-6.1-sol" => "Sol 6.1", "openai/gpt-6-astra" => "Astra", _ => model
    };

    public void Stop(string login)
    {
        if (!_cells.TryGetValue(login, out var cell) || cell.Process is not { HasExited: false } process) return;
        process.Kill(entireProcessTree: true);
        process.Dispose();
        cell.Process = null;
    }

    public async Task<string> CompleteAsync(string login, string instructions, string prompt, CancellationToken cancellationToken,
        string? model = null, int maxTokens = 900)
    {
        var cell = _cells.GetOrAdd(login, CreateCell);
        await cell.Gate.WaitAsync(cancellationToken);
        var name = Friendly(model ?? Model); var started = DateTimeOffset.UtcNow;
        try
        {
            await EnsureStartedAsync(cell, cancellationToken);
            AgentActivity.Note("Asking " + name + "…");
            using var client = _clients.CreateClient("openclaw");
            using var call = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{cell.Port}/v1/chat/completions");
            call.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cell.Token);
            if (model is not null) call.Headers.Add("x-openclaw-model", model);
            call.Content = JsonContent.Create(new
            {
                model = "openclaw/default",
                messages = new object[]
                {
                    new { role = "system", content = instructions },
                    new { role = "user", content = prompt }
                },
                max_tokens = maxTokens
            });
            using var response = await client.SendAsync(call, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ModelUnavailableException((int)response.StatusCode switch
                {
                    401 or 403 => "OpenClaw was refused by the model provider: the cell needs OpenAI sign-in (tools/OpenClawSignIn.ps1 -Owner).",
                    429 => "The model provider is rate-limiting requests. Try again in a few minutes.",
                    >= 500 => "The model provider had an error (" + (int)response.StatusCode + "). Try again shortly.",
                    _ => "OpenClaw could not answer (" + (int)response.StatusCode + ")."
                });
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            if (document is null || !document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                throw new ModelUnavailableException("OpenClaw returned an unrecognized answer.");
            var answer = content.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
                throw new ModelUnavailableException("OpenClaw returned an empty answer.");
            AgentActivity.Note(name + " answered in " + Math.Max(1, (int)(DateTimeOffset.UtcNow - started).TotalSeconds) + "s.");
            // OpenAI-compatible usage when the gateway passes it through; a call without it still counts as a call.
            UsageMeter.Model(model ?? Model, document.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
                usage.TryGetProperty("total_tokens", out var total) && total.TryGetInt64(out var tokens) ? tokens : 0, (DateTimeOffset.UtcNow - started).TotalSeconds);
            return answer.Length <= 8000 ? answer : answer[..8000];
        }
        catch (HttpRequestException)
        {
            throw new ModelUnavailableException("OpenClaw is unavailable.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ModelUnavailableException(name + " didn't answer within " + Math.Round((DateTimeOffset.UtcNow - started).TotalMinutes) + " minutes (OpenClaw timed out). Try again; large PRs take longer.");
        }
        finally
        {
            cell.Gate.Release();
        }
    }

    // The owner cell's workspace, where OpenClaw keeps MEMORY.md and memory/*.md notes.
    public string Workspace(string login) => Path.Combine(_cells.GetOrAdd(login, CreateCell).Directory, "workspace");

    // Runs an OpenClaw CLI command against a cell (memory, wiki) and returns its standard output.
    public async Task<string> CliAsync(string login, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cell = _cells.GetOrAdd(login, CreateCell);
        if (!File.Exists(_node) || !File.Exists(_entry)) throw new ModelUnavailableException("OpenClaw runtime is not installed.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); limit.CancelAfter(timeout);
        var start = new ProcessStartInfo(_node) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cell.Directory };
        start.ArgumentList.Add(_entry);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["OPENCLAW_STATE_DIR"] = cell.Directory;
        start.Environment["OPENCLAW_CONFIG_PATH"] = cell.ConfigPath;
        start.Environment["OPENCLAW_GATEWAY_TOKEN"] = cell.Token;
        start.Environment["PATH"] = Path.GetDirectoryName(_node) + Path.PathSeparator + start.Environment["PATH"];
        using var process = Process.Start(start) ?? throw new ModelUnavailableException("OpenClaw could not start.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token); var errors = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            var text = await output;
            if (process.ExitCode != 0 && text.Length == 0) throw new ModelUnavailableException("OpenClaw " + arguments[0] + " failed.");
            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ModelUnavailableException("OpenClaw " + arguments[0] + " timed out."); }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }

    // A Gateway RPC through the supported CLI helper. Starts the cell's Gateway when needed.
    public async Task<JsonElement> CallAsync(string login, string method, object parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cell = _cells.GetOrAdd(login, CreateCell);
        await EnsureStartedAsync(cell, cancellationToken);
        var output = await CliAsync(login, ["gateway", "call", method, "--port", cell.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--json",
            "--timeout", ((int)timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture), "--params", JsonSerializer.Serialize(parameters)], timeout + TimeSpan.FromSeconds(15), cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                throw new ModelUnavailableException("OpenClaw " + method + " failed" + (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) ? ": " + message.GetString() : "."));
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new ModelUnavailableException("OpenClaw " + method + " returned an unreadable answer."); }
    }

    private Cell CreateCell(string login)
    {
        var owner = login == OwnerCell;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(login));
        var name = Convert.ToHexString(hash)[..24].ToLowerInvariant();
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        SecureDirectory(directory);
        var workspace = Path.Combine(directory, "workspace");
        Directory.CreateDirectory(workspace);
        SecureDirectory(workspace);
        var port = 30000 + (int)(BitConverter.ToUInt32(hash, 0) % 25000);
        var tokenPath = Path.Combine(directory, "gateway.token");
        if (!File.Exists(tokenPath))
        {
            using var stream = new FileStream(tokenPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var bytes = Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            stream.Write(bytes);
        }
        SecureFile(tokenPath);
        var token = File.ReadAllText(tokenPath).Trim();
        var configuration = new
        {
            gateway = new
            {
                mode = "local", port, bind = "loopback", auth = new { mode = "token" },
                tailscale = new { mode = "off" }, controlUi = new { enabled = false },
                terminal = new { enabled = false },
                http = new { endpoints = new { chatCompletions = new { enabled = true } } }
            },
            // Memory is OpenClaw's: memory-core (notes, keyword search, nightly dreaming into MEMORY.md) and
            // memory-wiki compiled from it. Search stays keyword-only so notes never leave the machine for embeddings.
            // The codex plugin contributes only its read-only session catalog; supervision tools stay off.
            // Only the owner's cell has memory and the session catalog; an invited person's cell is a bare model gateway.
            plugins = owner ? (object)new
            {
                allow = new[] { "openai", "codex", "memory-core", "memory-wiki" },
                entries = new Dictionary<string, object>
                {
                    ["memory-core"] = new { enabled = true, config = new { dreaming = new { enabled = true } } },
                    ["memory-wiki"] = new { enabled = true, config = new { vaultMode = "bridge", vault = new { scope = "global", path = Path.Combine(directory, "wiki") },
                        bridge = new { enabled = true, readMemoryArtifacts = true, indexDailyNotes = true } } },
                    ["codex"] = new { enabled = true, config = new { supervision = new { enabled = false }, sessionCatalog = new { enabled = true } } }
                }
            } : new { allow = new[] { "openai" }, entries = new Dictionary<string, object> { ["memory-core"] = new { enabled = false } } },
            memory = new { search = new { provider = "none" } },
            models = new { providers = new { openai = new { agentRuntime = new { id = "openclaw" },
                baseUrl = "https://chatgpt.com/backend-api/codex", api = "openai-chatgpt-responses", auth = "oauth",
                models = new[] { "gpt-6-luna", "gpt-6.1-sol", "gpt-6-astra" }.Select(id => new { id, name=id,
                    api="openai-chatgpt-responses", reasoning=true, input=new[]{"text"} }).ToArray()
            } } },
            tools = new
            {
                profile = "minimal", deny = new[] { "gateway" }, alsoAllow = owner ? new[] { "memory_search", "memory_get", "wiki_search", "wiki_get" } : Array.Empty<string>(),
                sessions = new { visibility = "self" }, agentToAgent = new { enabled = false },
                message = new { crossContext = new { allowWithinProvider = false, allowAcrossProviders = false } }
            },
            agents = new { defaults = new { model = new { primary = Model }, thinkingDefault = ReasoningEffort, workspace, heartbeat = new { every = "0m" },
                models = new Dictionary<string,object> { [Model]=new{}, ["openai/gpt-6.1-sol"]=new{}, ["openai/gpt-6-astra"]=new{} }
            } },
            commands = new { restart = false }
        };
        ReclaimCaptures(directory);
        var configPath = Path.Combine(directory, "openclaw.json");
        WriteConfiguration(directory, configPath, token, JsonSerializer.Serialize(configuration));
        SecureFile(configPath);
        return new Cell(directory, configPath, port, token, owner);
    }

    private void WriteConfiguration(string directory, string configPath, string token, string configuration)
    {
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, configuration);
            return;
        }
        // A wholesale rewrite loses saved provider metadata and triggers OpenClaw's clobber recovery.
        // Use its supported batch setter to preserve authentication while enforcing this cell's boundaries.
        using var desired = JsonDocument.Parse(configuration);
        var operations = desired.RootElement.EnumerateObject().Where(item => item.Name is not ("agents" or "models"))
            .Select(item => new { path = item.Name, value = item.Value }).ToList();
        operations.AddRange(desired.RootElement.GetProperty("agents").GetProperty("defaults").EnumerateObject()
            .Select(item => new { path = "agents.defaults." + item.Name, value = item.Value }));
        operations.AddRange(desired.RootElement.GetProperty("models").GetProperty("providers").GetProperty("openai").EnumerateObject()
            .Select(item => new { path = "models.providers.openai."+item.Name, value = item.Value }));
        var start = new ProcessStartInfo(_node)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory
        };
        foreach (var argument in new[] { _entry, "config", "set", "--batch-json", JsonSerializer.Serialize(operations) })
            start.ArgumentList.Add(argument);
        start.Environment["OPENCLAW_STATE_DIR"] = directory;
        start.Environment["OPENCLAW_CONFIG_PATH"] = configPath;
        start.Environment["OPENCLAW_GATEWAY_TOKEN"] = token;
        start.Environment["PATH"] = Path.GetDirectoryName(_node) + Path.PathSeparator + start.Environment["PATH"];
        using var process = Process.Start(start) ?? throw new ModelUnavailableException("OpenClaw configuration could not start.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        if (!process.WaitForExit(45000))
        {
            process.Kill(entireProcessTree: true);
            throw new ModelUnavailableException("OpenClaw configuration timed out.");
        }
        if (process.ExitCode != 0) throw new ModelUnavailableException("OpenClaw configuration could not be confirmed.");
    }

    // A stopped gateway leaves its plugin source captures (about 800 MB each) until OpenClaw's hourly cleanup.
    // Same rule as that cleanup: remove an instance only when no process holds its coordinator file.
    public static long ReclaimCaptures(string cellDirectory)
    {
        var root = Path.Combine(cellDirectory, "tmp", "plugin-captures"); long freed = 0;
        if (!Directory.Exists(root)) return 0;
        foreach (var instance in Directory.EnumerateDirectories(root))
        {
            var info = new DirectoryInfo(instance);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var coordinator = Path.Combine(instance, "owner.sqlite");
            if (!File.Exists(coordinator)) continue;
            try { using (new FileStream(coordinator, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            try
            {
                var size = info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
                // Payload first, coordinator last, so a partial removal stays retryable.
                var captures = Path.Combine(instance, "captures");
                if (Directory.Exists(captures)) Directory.Delete(captures, true);
                Directory.Delete(instance, true); freed += size;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        if (freed > 0) AgentActivity.Note("Cleared " + (freed / (1024 * 1024)) + " MB of OpenClaw plugin copies left by stopped gateways.");
        return freed;
    }

    private static void SecureDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity is unavailable.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var rights = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl, rights,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, rights,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void SecureFile(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity is unavailable.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private async Task EnsureStartedAsync(Cell cell, CancellationToken cancellationToken)
    {
        if (await IsReadyAsync(cell, cancellationToken)) return;
        await cell.StartGate.WaitAsync(cancellationToken);
        try { await StartAsync(cell, cancellationToken); } finally { cell.StartGate.Release(); }
    }

    private async Task StartAsync(Cell cell, CancellationToken cancellationToken)
    {
        if (await IsReadyAsync(cell, cancellationToken)) return;
        if (!File.Exists(_node) || !File.Exists(_entry))
            throw new ModelUnavailableException("OpenClaw runtime is not installed.");
        if (cell.Process is { HasExited: false })
            throw new ModelUnavailableException("OpenClaw is starting or is not healthy.");
        var start = new ProcessStartInfo(_node)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = cell.Directory
        };
        start.ArgumentList.Add(_entry);
        start.ArgumentList.Add("gateway");
        start.ArgumentList.Add("run");
        start.Environment["OPENCLAW_STATE_DIR"] = cell.Directory;
        start.Environment["OPENCLAW_CONFIG_PATH"] = cell.ConfigPath;
        start.Environment["OPENCLAW_GATEWAY_TOKEN"] = cell.Token;
        start.Environment["PATH"] = Path.GetDirectoryName(_node) + Path.PathSeparator + start.Environment["PATH"];
        // An isolated state folder hides the user's Codex home from the session catalog unless it is named explicitly.
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        if (cell.Owner && Directory.Exists(codexHome)) start.Environment["CODEX_HOME"] = codexHome;
        cell.Process = Process.Start(start) ?? throw new ModelUnavailableException("OpenClaw could not start.");
        cell.Process.OutputDataReceived += (_, _) => { };
        cell.Process.ErrorDataReceived += (_, _) => { };
        cell.Process.BeginOutputReadLine();
        cell.Process.BeginErrorReadLine();
        for (var attempt = 0; attempt < 180; attempt++)
        {
            if (cell.Process.HasExited)
                throw new ModelUnavailableException("OpenClaw stopped during startup.");
            if (await IsReadyAsync(cell, cancellationToken)) return;
            await Task.Delay(250, cancellationToken);
        }
        throw new ModelUnavailableException("OpenClaw did not become ready.");
    }

    private async Task<bool> IsReadyAsync(Cell cell, CancellationToken cancellationToken)
    {
        using var client = _clients.CreateClient("openclaw-probe");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{cell.Port}/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cell.Token);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ModelUnavailableException("OpenClaw port is occupied by another Gateway.");
            if (!response.IsSuccessStatusCode) return false;
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            return document?.RootElement.TryGetProperty("data", out var models) == true &&
                models.EnumerateArray().Any(model => model.TryGetProperty("id", out var id) &&
                    id.GetString() == "openclaw/default");
        }
        catch (HttpRequestException) { return false; }
        catch (JsonException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public void Dispose()
    {
        foreach (var cell in _cells.Values)
        {
            if (cell.Process is not { HasExited: false } process) continue;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose(); cell.Process = null;
        }
    }

    private sealed record Cell(string Directory, string ConfigPath, int Port, string Token, bool Owner)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public SemaphoreSlim StartGate { get; } = new(1, 1);
        public Process? Process { get; set; }
    }
}

