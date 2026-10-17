using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.OpenClaw;

// Fresh gateway state, existing auth in place, no tools or historical startup jobs.
public sealed class TextOnlyOpenClaw(string dataRoot) : IModelGateway, IDisposable
{
    private const int Port = 55064;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private string? _token;
    public string DefaultModel => OpenClawCells.Model;
    public string Reasoning => "high";

    public void Prepare(string cell) { }

    public void Stop(string cell) { }

    public static object Configuration(string workspace, string existingAuthDirectory) =>
        new
        {
            gateway = new
            {
                mode = "local",
                port = Port,
                bind = "loopback",
                auth = new { mode = "token" },
                tailscale = new { mode = "off" },
                controlUi = new { enabled = false },
                terminal = new { enabled = false },
                http = new { endpoints = new { chatCompletions = new { enabled = true } } },
            },
            plugins = new
            {
                allow = new[] { "openai" },
                entries = new Dictionary<string, object>
                {
                    ["memory-core"] = new { enabled = false },
                    ["memory-wiki"] = new { enabled = false },
                    ["codex"] = new { enabled = false },
                },
            },
            tools = new
            {
                profile = "minimal",
                deny = new[] { "*" },
                agentToAgent = new { enabled = false },
            },
            agents = new
            {
                ownership = "explicit",
                defaults = new
                {
                    model = new { primary = OpenClawCells.Model, fallbacks = Array.Empty<string>() },
                    thinkingDefault = "high",
                    workspace,
                    skills = Array.Empty<string>(),
                    skipBootstrap = true,
                    contextInjection = "never",
                    startupContext = new { enabled = false },
                    heartbeat = new { every = "0m" },
                    compaction = new { enabled = false, memoryFlush = new { enabled = false } },
                },
                entries = new Dictionary<string, object>
                {
                    ["main"] = new
                    {
                        agentDir = existingAuthDirectory,
                        skills = Array.Empty<string>(),
                        contextInjection = "never",
                    },
                },
            },
            models = new
            {
                providers = new
                {
                    openai = new
                    {
                        agentRuntime = new { id = "openclaw" },
                        baseUrl = "https://chatgpt.com/backend-api/codex",
                        api = "openai-chatgpt-responses",
                        auth = "oauth",
                        models = new[]
                        {
                            new
                            {
                                id = "gpt-6-luna",
                                name = "gpt-6-luna",
                                api = "openai-chatgpt-responses",
                                reasoning = true,
                                input = new[] { "text" },
                            },
                        },
                    },
                },
            },
            memory = new { search = new { provider = "none" } },
            skills = new { allowBundled = Array.Empty<string>() },
            hooks = new { enabled = false, @internal = new { enabled = false } },
            channels = new Dictionary<string, object>(),
            cron = new
            {
                enabled = false,
                triggers = new { enabled = false },
                sessionRetention = false,
            },
            commands = new { restart = false },
        };

    public async Task<string> CompleteAsync(
        string cell,
        string instructions,
        string prompt,
        CancellationToken token,
        string? model = null,
        int maxTokens = 900
    )
    {
        if (cell != IModelGateway.OwnerCell || model is not null && model != DefaultModel)
            throw new ModelUnavailableException("Only the configured owner's text model is available.");
        await _gate.WaitAsync(token);
        try
        {
            await StartAsync(token);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "http://127.0.0.1:" + Port + "/v1/chat/completions"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Content = JsonContent.Create(
                new
                {
                    model = "openclaw/default",
                    messages = new[]
                    {
                        new { role = "system", content = instructions },
                        new { role = "user", content = prompt },
                    },
                    max_tokens = Math.Min(500, maxTokens),
                }
            );
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new ModelUnavailableException(FailureMessage(response.StatusCode));
            using var content = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            if (
                content?.RootElement.TryGetProperty("choices", out var choices) != true
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var text)
                || string.IsNullOrWhiteSpace(text.GetString())
            )
                throw new ModelUnavailableException("The model returned no text response.");
            return text.GetString()!;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or SocketException)
        {
            throw new ModelUnavailableException(
                "The existing text-only model runtime is unavailable. No execution was started."
            );
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            throw new ModelUnavailableException(
                "The existing model did not answer within three minutes. Retry explicitly; nothing will be replayed automatically."
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string FailureMessage(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "The local text-only gateway refused this chat request (HTTP "
                + (int)status
                + "). Existing model sign-in has not been verified. No credentials or permissions were changed."
            : "The existing owner model could not answer (HTTP "
                + (int)status
                + "). No task or publication was started.";

    private async Task StartAsync(CancellationToken token)
    {
        if (_process is { HasExited: false })
        {
            await WaitReadyAsync(token);
            return;
        }
        using (var probe = new TcpClient())
        {
            try
            {
                await probe.ConnectAsync(IPAddress.Loopback, Port, token);
                throw new ModelUnavailableException(
                    "The text-only gateway port is occupied; its process was not reused or stopped."
                );
            }
            catch (SocketException) { }
        }
        var name = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(IModelGateway.OwnerCell)))[..24]
            .ToLowerInvariant();
        var ownerCell = Path.Combine(dataRoot, "OpenClaw", name);
        var authDirectory = Path.Combine(ownerCell, "agents", "main", "agent");
        var tokenPath = Path.Combine(ownerCell, "gateway.token");
        if (!File.Exists(tokenPath) || !Directory.Exists(authDirectory))
            throw new ModelUnavailableException(
                "Existing owner auth state is unavailable. Sign the existing owner cell in using tools/OpenClawSignIn.ps1 -Owner and the existing data root."
            );
        var node = Path.Combine(dataRoot, "Runtime", "node", "node.exe");
        var entry = Path.Combine(dataRoot, "Runtime", "openclaw", "node_modules", "openclaw", "openclaw.mjs");
        if (!File.Exists(node) || !File.Exists(entry))
            throw new ModelUnavailableException("The existing OpenClaw runtime is missing.");
        OpenClawRuntime.EnsurePinned(entry);
        var state = Path.Combine(dataRoot, "RecoveryChat", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(state, "workspace");
        Directory.CreateDirectory(workspace);
        var config = Path.Combine(state, "openclaw.json");
        File.WriteAllText(config, JsonSerializer.Serialize(Configuration(workspace, authDirectory)));
        _token = File.ReadAllText(tokenPath).Trim();
        var start = new ProcessStartInfo(node)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = state,
        };
        start.ArgumentList.Add(entry);
        start.ArgumentList.Add("gateway");
        start.ArgumentList.Add("run");
        start.Environment["OPENCLAW_STATE_DIR"] = state;
        start.Environment["OPENCLAW_CONFIG_PATH"] = config;
        start.Environment["OPENCLAW_GATEWAY_TOKEN"] = _token;
        start.Environment.Remove("CODEX_HOME");
        start.Environment["PATH"] = Path.GetDirectoryName(node) + Path.PathSeparator + start.Environment["PATH"];
        _process =
            Process.Start(start) ?? throw new ModelUnavailableException("The text-only runtime could not start.");
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await WaitReadyAsync(token);
    }

    private async Task WaitReadyAsync(CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (_process is null || _process.HasExited)
                throw new ModelUnavailableException(
                    "The text-only runtime rejected its isolated configuration. No credentials or jobs were copied."
                );
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + Port + "/v1/models");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var response = await client.SendAsync(request, token);
                if (response.IsSuccessStatusCode)
                {
                    using var content = await response.Content.ReadFromJsonAsync<JsonDocument>(
                        cancellationToken: token
                    );
                    if (
                        content?.RootElement.TryGetProperty("data", out var models) == true
                        && models
                            .EnumerateArray()
                            .Any(item => item.TryGetProperty("id", out var id) && id.GetString() == "openclaw/default")
                    )
                        return;
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException) { }
            await Task.Delay(250, token);
        }
        throw new ModelUnavailableException("The text-only runtime did not become ready within its startup limit.");
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
        _process = null;
        _token = null;
    }
}
