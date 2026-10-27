using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Sandbox;

// Execution/recovery adapter only. Qualification and durable authority must compose it before host registration.
public sealed class DockerBrowser : IBrowserExecutor
{
    public const int MemoryMiB = 512;
    public const int ProcessLimit = 128;
    public const int LogBytes = 4 * 1024 * 1024;
    private const string Label = "io.hirezero.browser.";
    private readonly IDockerDuplexCommands _docker;
    private readonly string _image,
        _installation,
        _engine;
    private readonly Func<BrowserRequest, Func<bool>, IBrowserNetworkSession> _network;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _started = new();
    public string Identity =>
        "docker-browser-runsc-v1:" + _image + ":" + CompanyJson.Digest(new { _installation, _engine });

    public DockerBrowser(
        IDockerDuplexCommands docker,
        string image,
        string installationId,
        string engineId,
        Func<BrowserRequest, Func<bool>, IBrowserNetworkSession> network
    )
    {
        if (
            !image.StartsWith("sha256:", StringComparison.Ordinal)
            || image.Length != 71
            || !image[7..].All(Uri.IsHexDigit)
            || !Guid.TryParseExact(installationId, "N", out _)
            || string.IsNullOrWhiteSpace(engineId)
            || engineId.Length > 200
        )
            throw new ArgumentException("Pin a local browser image and the installation's durable engine identity.");
        _docker = docker;
        _image = image.ToLowerInvariant();
        _installation = ContentGuard.Digest(installationId);
        _engine = engineId;
        _network = network;
    }

    private Task<DockerReply> Command(string[] args, CancellationToken token, int limit = 128 * 1024) =>
        _docker.RunAsync(args, null, TimeSpan.FromSeconds(10), limit, token);

    private string Name(BrowserRequest request) =>
        "hz-browser-"
        + ContentGuard.Digest(Identity)[..12]
        + "-"
        + ContentGuard.Digest(request.CompanyId)[..12]
        + "-"
        + request.JobId;

    private static JsonElement Json(DockerReply reply)
    {
        if (reply.ExitCode != 0)
            throw new IOException("Browser engine command failed.");
        using var document = JsonDocument.Parse(reply.Output);
        return document.RootElement.Clone();
    }

    private static string? Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static bool True(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.True;

    private static bool False(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.False;

    private static bool Empty(JsonElement value, string key) =>
        !value.TryGetProperty(key, out var field)
        || field.ValueKind == JsonValueKind.Null
        || field.ValueKind == JsonValueKind.Array && field.GetArrayLength() == 0
        || field.ValueKind == JsonValueKind.Object && !field.EnumerateObject().Any();

    private static bool Exact(JsonElement value, string key, params string[] expected) =>
        value.TryGetProperty(key, out var field)
        && field.ValueKind == JsonValueKind.Array
        && field
            .EnumerateArray()
            .Select(v => v.GetString())
            .Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Order(StringComparer.Ordinal));

    private async Task<bool> SameEngineAsync(CancellationToken token)
    {
        var info = Json(await Command(["info", "--format", "{{json .}}"], token));
        return Text(info, "OSType") == "linux" && Text(info, "ID") == _engine;
    }

    public async Task<bool> AvailableAsync(CancellationToken token)
    {
        try
        {
            if ((await DockerSandbox.InspectEngineAsync(_docker, token))?.Id != _engine)
                return false;
            var images = Json(await Command(["image", "inspect", _image], token));
            if (images.GetArrayLength() != 1)
                return false;
            var image = images[0];
            return Text(image, "Id") == _image
                && Text(image, "Os") == "linux"
                && Empty(image.GetProperty("Config"), "Volumes")
                && Text(image.GetProperty("Config").GetProperty("Labels"), Label + "worker") == "1";
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return false;
        }
    }

    private string[] CreateArguments(BrowserRequest request) =>
        [
            "container",
            "create",
            "--pull",
            "never",
            "--name",
            Name(request),
            "--label",
            Label + "installation=" + _installation,
            "--label",
            Label + "company=" + ContentGuard.Digest(request.CompanyId),
            "--label",
            Label + "job=" + request.JobId,
            "--label",
            Label + "request=" + CompanyJson.Digest(request),
            "--runtime",
            "runsc",
            "--network",
            "none",
            "--read-only",
            "--interactive",
            "--user",
            "0:0",
            "--cap-drop",
            "ALL",
            "--cap-add",
            "SETUID",
            "--cap-add",
            "SETGID",
            "--cap-add",
            "KILL",
            "--security-opt",
            "no-new-privileges",
            "--pids-limit",
            "128",
            "--cpus",
            "1",
            "--memory",
            "512m",
            "--memory-swap",
            "512m",
            "--tmpfs",
            "/work:rw,nosuid,nodev,noexec,size=128m,mode=1777",
            "--tmpfs",
            "/tmp:rw,nosuid,nodev,noexec,size=32m,mode=1777",
            "--ipc",
            "none",
            "--cgroupns",
            "private",
            "--ulimit",
            "nofile=1024:1024",
            "--ulimit",
            "core=0:0",
            "--restart",
            "no",
            "--log-driver",
            "local",
            "--log-opt",
            "max-size=4m",
            "--log-opt",
            "max-file=1",
            "--log-opt",
            "compress=false",
            "--no-healthcheck",
            "--stop-timeout",
            "1",
            "--workdir",
            "/work",
            "--entrypoint",
            "/usr/local/bin/node",
            _image,
            "/opt/hirezero/browser/runner.mjs",
        ];

    private bool Owned(JsonElement container, BrowserRequest request) =>
        Text(container, "Name") == "/" + Name(request)
        && Text(container, "Image") == _image
        && container.TryGetProperty("Config", out var config)
        && config.TryGetProperty("Labels", out var labels)
        && Text(labels, Label + "installation") == _installation
        && Text(labels, Label + "company") == ContentGuard.Digest(request.CompanyId)
        && Text(labels, Label + "job") == request.JobId
        && Text(labels, Label + "request") == CompanyJson.Digest(request);

    private bool Envelope(JsonElement container, BrowserRequest request)
    {
        if (!Owned(container, request))
            return false;
        var config = container.GetProperty("Config");
        var host = container.GetProperty("HostConfig");
        return Text(config, "User") == "0:0"
            && Text(config, "WorkingDir") == "/work"
            && Exact(config, "Entrypoint", "/usr/local/bin/node")
            && Exact(config, "Cmd", "/opt/hirezero/browser/runner.mjs")
            && True(config, "OpenStdin")
            && False(config, "Tty")
            && Exact(config.GetProperty("Healthcheck"), "Test", "NONE")
            && Text(host, "Runtime") == "runsc"
            && Text(host, "NetworkMode") == "none"
            && True(host, "ReadonlyRootfs")
            && False(host, "Privileged")
            && False(host, "AutoRemove")
            && Text(host, "PidMode") is "" or null
            && Text(host, "IpcMode") == "none"
            && Text(host, "CgroupnsMode") == "private"
            && host.GetProperty("Memory").GetInt64() == MemoryMiB * 1024L * 1024
            && host.GetProperty("MemorySwap").GetInt64() == MemoryMiB * 1024L * 1024
            && host.GetProperty("NanoCpus").GetInt64() == 1_000_000_000
            && host.GetProperty("PidsLimit").GetInt64() == ProcessLimit
            && Exact(host, "CapDrop", "ALL")
            && DockerCapabilities.Exact(host, "CapAdd", "SETUID", "SETGID", "KILL")
            && Exact(host, "SecurityOpt", "no-new-privileges")
            && Empty(host, "Binds")
            && Empty(host, "Mounts")
            && Empty(host, "Devices")
            && Empty(host, "DeviceRequests")
            && Empty(host, "PortBindings")
            && Empty(host, "VolumesFrom")
            && Text(host.GetProperty("RestartPolicy"), "Name") == "no"
            && host.GetProperty("Ulimits").GetArrayLength() == 2
            && host.GetProperty("Ulimits")
                .EnumerateArray()
                .Any(u =>
                    Text(u, "Name") == "nofile"
                    && u.GetProperty("Soft").GetInt64() == 1024
                    && u.GetProperty("Hard").GetInt64() == 1024
                )
            && host.GetProperty("Ulimits")
                .EnumerateArray()
                .Any(u =>
                    Text(u, "Name") == "core"
                    && u.GetProperty("Soft").GetInt64() == 0
                    && u.GetProperty("Hard").GetInt64() == 0
                )
            && Text(host.GetProperty("LogConfig"), "Type") == "local"
            && Text(host.GetProperty("LogConfig").GetProperty("Config"), "max-size") == "4m"
            && Text(host.GetProperty("LogConfig").GetProperty("Config"), "max-file") == "1"
            && Text(host.GetProperty("LogConfig").GetProperty("Config"), "compress") == "false"
            && host.GetProperty("Tmpfs").EnumerateObject().Count() == 2
            && Text(host.GetProperty("Tmpfs"), "/work") == "rw,nosuid,nodev,noexec,size=128m,mode=1777"
            && Text(host.GetProperty("Tmpfs"), "/tmp") == "rw,nosuid,nodev,noexec,size=32m,mode=1777"
            && container
                .GetProperty("Mounts")
                .EnumerateArray()
                .All(m => Text(m, "Type") == "tmpfs" && Text(m, "Destination") is "/work" or "/tmp");
    }

    private async Task<JsonElement?> Inspect(BrowserRequest request, CancellationToken token)
    {
        var inspected = await Command(["container", "inspect", Name(request)], token);
        if (inspected.ExitCode == 0)
        {
            var items = Json(inspected);
            return items.GetArrayLength() == 1
                ? items[0].Clone()
                : throw new IOException("Ambiguous browser resource.");
        }
        var listed = await Command(
            ["container", "ls", "--all", "--quiet", "--no-trunc", "--filter", "name=^/" + Name(request) + "$"],
            token
        );
        if (listed.ExitCode == 0 && string.IsNullOrWhiteSpace(listed.Output))
            return null;
        throw new IOException("Browser resource state is unconfirmed.");
    }

    private bool Exited(JsonElement container, BrowserRequest request)
    {
        var state = container.GetProperty("State");
        return Envelope(container, request)
            && False(state, "Running")
            && Text(state, "Status") == "exited"
            && state.GetProperty("ExitCode").GetInt32() == 0
            && False(state, "OOMKilled");
    }

    private static BrowserRequest Snapshot(BrowserRequest request)
    {
        request.Validate();
        var copy = request with { Origins = request.Origins.ToArray(), Steps = request.Steps.ToArray() };
        copy.Validate();
        return copy;
    }

    public async Task<BrowserExecution> ExecuteAsync(
        BrowserRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request = Snapshot(request);
        var watch = Stopwatch.StartNew();
        BrowserExecution Unsent(string reason, bool certain) =>
            new(
                "not-sent",
                null,
                null,
                watch.ElapsedMilliseconds,
                new(certain, certain ? "not-created" : "not-observed", Name(request)),
                reason
            );
        if (!_started.TryAdd(Name(request), 0))
            return Unsent("already-attempted", false);
        if (!await AvailableAsync(token))
            return Unsent("browser-unavailable", true);
        if (!stillAuthorized() || token.IsCancellationRequested)
            return Unsent("authority-changed", true);
        try
        {
            if (await Inspect(request, token) is not null)
                return new(
                    "unknown",
                    null,
                    null,
                    watch.ElapsedMilliseconds,
                    new(false, "not-observed", Name(request)),
                    "existing-resource"
                );
            if (!await SameEngineAsync(token))
                return Unsent("engine-changed", false);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return Unsent("resource-state-unconfirmed", false);
        }
        var created = false;
        var state = "unknown";
        var reason = "provisioning-unconfirmed";
        IBrowserNetworkSession? network = null;
        BrowserNetworkUsage? usage = null;
        try
        {
            var provision = await Command(CreateArguments(request), token);
            created = provision.ExitCode == 0;
            var container = await Inspect(request, token);
            if (!created || container is null || !Envelope(container.Value, request) || !await SameEngineAsync(token))
                throw new IOException("Browser envelope is unconfirmed.");
            if (
                True(container.Value.GetProperty("State"), "Running")
                || Text(container.Value.GetProperty("State"), "Status") != "created"
            )
                throw new IOException("Browser environment is not fresh.");
            if (!stillAuthorized() || token.IsCancellationRequested)
            {
                state = "not-sent";
                reason = "authority-changed";
            }
            else
            {
                reason = "network-session-unconfirmed";
                network = _network(request, stillAuthorized);
                reason = "execution-unconfirmed";
                var exchange = await _docker.ExchangeAsync(
                    ["container", "start", "--attach", "--interactive", Name(request)],
                    async (output, input, deadline) =>
                    {
                        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, deadline);
                        return await BrowserWorkerProtocol.ExchangeAsync(
                            request,
                            output,
                            input,
                            network,
                            stillAuthorized,
                            cancel.Token
                        );
                    },
                    TimeSpan.FromSeconds(request.Limits.Seconds + 16),
                    CancellationToken.None
                );
                var after = await Inspect(request, CancellationToken.None);
                if (
                    exchange.ExitCode != 0
                    || after is null
                    || !Exited(after.Value, request)
                    || !await SameEngineAsync(CancellationToken.None)
                )
                    throw new IOException("Browser exit is unconfirmed.");
                var result = exchange.Value.Result;
                if (!stillAuthorized() || token.IsCancellationRequested)
                    result = result with
                    {
                        State = "canceled",
                        Captures = [],
                        Blocked = [],
                        Warnings = [],
                        Partial = true,
                        Error = "Browser output was withheld after cancellation or revocation.",
                    };
                return new(
                    result.State,
                    result,
                    exchange.Value.Network,
                    watch.ElapsedMilliseconds,
                    new(false, "awaiting-release", Name(request)),
                    CreationConfirmed: true,
                    BrowserExited: exchange.Value.BrowserExited
                );
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { }
        finally
        {
            usage = network?.Usage;
            network?.Dispose();
        }
        return new(
            state,
            null,
            usage,
            watch.ElapsedMilliseconds,
            new(false, "awaiting-reconciliation", Name(request)),
            reason,
            created
        );
    }

    public async Task<BrowserExecution> RecoverAsync(BrowserRequest request, CancellationToken token)
    {
        request = Snapshot(request);
        var created = false;
        BrowserExecution Unknown(string reason) =>
            new("unknown", null, null, null, new(false, "awaiting-reconciliation", Name(request)), reason, created);
        try
        {
            if (!await SameEngineAsync(token))
                return Unknown("engine-changed");
            var container = await Inspect(request, token);
            if (container is null || !Owned(container.Value, request))
                return Unknown("resource-unconfirmed");
            created = true;
            if (!Exited(container.Value, request))
                return Unknown("execution-unconfirmed");
            var logs = await Command(["container", "logs", Name(request)], token, LogBytes);
            if (logs.ExitCode != 0 || Encoding.UTF8.GetByteCount(logs.Output) > LogBytes)
                return Unknown("result-unconfirmed");
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(logs.Output));
            var receipt = await BrowserWorkerProtocol.ReadRecoveredAsync(request, content, token);
            if (!await SameEngineAsync(token))
                return Unknown("engine-changed");
            return new(
                receipt.Result.State,
                receipt.Result,
                null,
                null,
                new(false, "awaiting-release", Name(request)),
                "network-usage-unconfirmed",
                true,
                receipt.BrowserExited
            );
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return Unknown("recovery-unconfirmed");
        }
    }

    public async Task<SandboxRelease> ReleaseAsync(
        BrowserRequest request,
        string resourceId,
        bool creationConfirmed,
        CancellationToken token
    )
    {
        request = Snapshot(request);
        if (resourceId != Name(request))
            return new(false, "scope-refused", Name(request));
        try
        {
            if (!await SameEngineAsync(token))
                return new(false, "scope-refused", Name(request));
            var container = await Inspect(request, token);
            if (container is null)
                return creationConfirmed && await SameEngineAsync(token)
                    ? new(true, "released", Name(request))
                    : new(false, "provisioning-unconfirmed", Name(request));
            if (!Owned(container.Value, request))
                return new(false, "scope-refused", Name(request));
            var id = Text(container.Value, "Id");
            if (id is null || id.Length != 64 || !id.All(Uri.IsHexDigit))
                return new(false, "scope-refused", Name(request));
            var removed = await Command(["container", "rm", "--force", id], token);
            return removed.ExitCode == 0 && await Inspect(request, token) is null && await SameEngineAsync(token)
                ? new(true, "released", Name(request))
                : new(false, "release-unconfirmed", Name(request));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return new(false, "release-unconfirmed", Name(request));
        }
    }
}
