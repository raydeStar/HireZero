using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Codex;

// One owned Codex app-server process over stdio, with a cleared environment and strict, process-local configuration.
// Shared by measured text replies (CompanyCodexConnection) and live company coding (CompanyCodexCodingRunner).
internal sealed class CodexAppServer : IAsyncDisposable
{
    private readonly Process _process;
    private int _id;
    private readonly Queue<JsonElement> _events = new();
    private readonly int _lineLimit;
    public string WorkingDirectory { get; }

    public CodexAppServer(
        string executable,
        string? home,
        string? syntheticMode,
        string work,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        int lineLimit = 131072
    )
    {
        _lineLimit = lineLimit;
        // app-server stdio is UTF-8, independent of the Windows console code page.
        WorkingDirectory = work;
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = work,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        var inherited = info
            .Environment.Where(x =>
                new[]
                {
                    "PATH",
                    "SystemRoot",
                    "WINDIR",
                    "USERPROFILE",
                    "HOMEDRIVE",
                    "HOMEPATH",
                    "APPDATA",
                    "LOCALAPPDATA",
                    "TEMP",
                    "TMP",
                    "CODEX_HOME",
                }.Contains(x.Key, StringComparer.OrdinalIgnoreCase)
            )
            .ToArray();
        info.Environment.Clear();
        foreach (var (key, value) in inherited)
            info.Environment[key] = value;
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            info.Environment[key] = value;
        if (home is not null)
            info.Environment["CODEX_HOME"] = Path.GetFullPath(home);
        if (syntheticMode is not null)
            info.Environment["HIREZERO_FIXTURE_MODE"] = syntheticMode;
        foreach (var a in arguments)
            info.ArgumentList.Add(a);
        _process = Process.Start(info) ?? throw new WorkspaceException("Codex did not start.");
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
    }

    // Features a text-only turn never needs. Coding keeps the shell tool and drops nothing else.
    public static readonly string[] TextOnlyFeatures =
    [
        "shell_tool",
        "shell_snapshot",
        "apps",
        "plugins",
        "remote_plugin",
        "memories",
        "multi_agent",
        "hooks",
        "computer_use",
        "browser_use",
        "image_generation",
        "view_image",
        "request_permissions_tool",
        "skill_search",
        "goals",
        "code_mode_host",
        "workspace_dependencies",
        "sleep_tool",
        "unbounded_connection_retries",
    ];

    public static readonly string[] CodingFeatures = [.. TextOnlyFeatures.Where(f => f != "shell_tool")];

    // Process-local, strict configuration: the MXC sandbox on Windows, one named permissions profile granting exactly
    // the given filesystem access with no network, and no MCP, plugins, web search, memories or project docs.
    public static IEnumerable<string> Arguments(
        string profile,
        IReadOnlyDictionary<string, string> filesystem,
        IEnumerable<string> disabledFeatures
    )
    {
        yield return "app-server";
        yield return "--stdio";
        yield return "--strict-config";
        if (OperatingSystem.IsWindows())
        {
            yield return "-c";
            yield return "windows.sandbox=\"mxc\"";
        }
        // The pinned native deserializer rejects legacy readOnly.access, even though the
        // generated schema permits unknown properties. Configure restricted access by name.
        yield return "-c";
        yield return "permissions={"
            + profile
            + "={filesystem={"
            + string.Join(",", filesystem.Select(f => JsonSerializer.Serialize(f.Key) + "=\"" + f.Value + "\""))
            + "},network={enabled=false}}}";
        yield return "-c";
        yield return "default_permissions=\"" + profile + "\"";
        foreach (
            var value in new[]
            {
                "model_provider=\"openai\"",
                "forced_login_method=\"chatgpt\"",
                "mcp_servers={}",
                "plugins={}",
                "project_doc_max_bytes=0",
                "developer_instructions=\"\"",
                "include_environment_context=false",
                "web_search=\"disabled\"",
                "skills.bundled.enabled=false",
                "skills.include_instructions=false",
                "memories.generate_memories=false",
                "memories.use_memories=false",
                "tools.update_plan.enabled=false",
                "tools.experimental_request_user_input.enabled=false",
            }
        )
        {
            yield return "-c";
            yield return value;
        }
        foreach (var feature in disabledFeatures)
        {
            yield return "--disable";
            yield return feature;
        }
    }

    public async Task Initialize(CancellationToken token)
    {
        await Request(
            "initialize",
            new
            {
                clientInfo = new { name = "hirezero_company", version = "0.1.0" },
                capabilities = new { experimentalApi = true },
            },
            token
        );
        await Write(new { method = "initialized", @params = new { } }, token);
    }

    public async Task VerifyPermissions(
        string expectedProfile,
        IReadOnlyDictionary<string, string> filesystem,
        CancellationToken token
    )
    {
        var config = (
            await Request("config/read", new { cwd = WorkingDirectory, includeLayers = false }, token)
        ).GetProperty("config");
        // An allowed named profile does not enable the Windows execution backend. With
        // no backend, command/exec can read outside the declared restricted roots.
        // This necessary configuration gate is not a substitute for behavioral qualification.
        if (
            OperatingSystem.IsWindows()
            && (!config.TryGetProperty("windows", out var windows) || Text(windows, "sandbox") != "mxc")
        )
            throw new WorkspaceException(
                "Native Windows sandbox enforcement is not configured as explicit MXC for restricted reads. No model turn was sent."
            );
        if (
            Text(config, "default_permissions") != expectedProfile
            || !config.TryGetProperty("permissions", out var profiles)
            || !profiles.TryGetProperty(expectedProfile, out var profile)
            || profile.ValueKind != JsonValueKind.Object
            || profile
                .EnumerateObject()
                .Any(p => p.Name is not ("filesystem" or "network") && p.Value.ValueKind != JsonValueKind.Null)
            || !profile.TryGetProperty("filesystem", out var fs)
            || fs.ValueKind != JsonValueKind.Object
            || fs.EnumerateObject().Count(p => p.Value.ValueKind != JsonValueKind.Null) != filesystem.Count
            || filesystem.Any(f => Text(fs, f.Key) != f.Value)
            || !profile.TryGetProperty("network", out var network)
            || Flag(network, "enabled") != false
            || network.EnumerateObject().Any(p => p.Name != "enabled" && p.Value.ValueKind != JsonValueKind.Null)
        )
            throw new WorkspaceException(
                "Codex restricted permissions are unconfirmed: only the approved roots may be accessible, with no network or inherited profile."
            );
        var available = await Request("permissionProfile/list", new { cwd = WorkingDirectory, limit = 100 }, token);
        if (
            available.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null
            || !available
                .GetProperty("data")
                .EnumerateArray()
                .Any(p => Text(p, "id") == expectedProfile && Flag(p, "allowed") == true)
        )
            throw new WorkspaceException(
                "Codex did not allow the selected restricted permissions profile. No model turn was sent."
            );
    }

    private async Task Write(object value, CancellationToken token) =>
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token);

    public async Task<JsonElement> Request(string method, object parameters, CancellationToken token)
    {
        var id = ++_id;
        await Write(
            new
            {
                id,
                method,
                @params = parameters,
            },
            token
        );
        while (true)
        {
            var j = await ReadWire(token);
            if (j.TryGetProperty("id", out var n) && n.TryGetInt32(out var value) && value == id)
            {
                if (j.TryGetProperty("error", out var error))
                {
                    var code =
                        error.TryGetProperty("code", out var rawCode) && rawCode.TryGetInt32(out var errorCode)
                            ? errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : "unknown";
                    throw new WorkspaceException(
                        "Codex refused the supported protocol request: "
                            + method
                            + " (code "
                            + code
                            + "). "
                            + SafeDiagnostic(error)
                            + " No automatic replay."
                    );
                }
                return j.GetProperty("result").Clone();
            }
            if (j.TryGetProperty("id", out var serverId))
            {
                await Deny(serverId, token);
                throw new WorkspaceException("Codex requested an unapproved action during preflight.");
            }
            if (_events.Count >= 256)
                throw new WorkspaceException("Too many runtime events.");
            _events.Enqueue(j);
        }
    }

    private static string SafeDiagnostic(JsonElement error)
    {
        // Emit only exact public protocol literals, never arbitrary server text or error.data.
        var message = Text(error, "message");
        foreach (
            var known in new[]
            {
                "readOnly.access is no longer supported; use permissionProfile for restricted reads",
                "workspaceWrite.readOnlyAccess is no longer supported; use permissionProfile for restricted reads",
                "`permissions` cannot be combined with `sandboxPolicy`",
                "`permissions` cannot be combined with `sandbox`",
            }
        )
            if (message == known || message == "Invalid request: " + known)
                return known + ".";
        return "Server message/details withheld.";
    }

    public Task Deny(JsonElement id, CancellationToken token) =>
        Write(
            new
            {
                id = id.Clone(),
                error = new
                {
                    code = -32601,
                    message = "HireZero declines all approvals, permission changes and authentication changes.",
                },
            },
            token
        );

    public Task<JsonElement> Read(CancellationToken token) =>
        _events.Count > 0 ? Task.FromResult(_events.Dequeue()) : ReadWire(token);

    private async Task<JsonElement> ReadWire(CancellationToken token)
    {
        var line =
            await _process.StandardOutput.ReadLineAsync(token)
            ?? throw new ModelTransportException("Codex exited without a confirmed outcome.");
        if (line.Length > _lineLimit)
            throw new ModelTransportException("Runtime event size exceeded its local allowance.");
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _process.Dispose();
            if (Directory.Exists(WorkingDirectory) && !Directory.EnumerateFileSystemEntries(WorkingDirectory).Any())
                Directory.Delete(WorkingDirectory);
        }
    }

    internal static string? Text(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object
        && j.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool? Flag(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object && j.TryGetProperty(key, out var value)
            ? value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False
                    ? false
                    : null
            : null;
}
