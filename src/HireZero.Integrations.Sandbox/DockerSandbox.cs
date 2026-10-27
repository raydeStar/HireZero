using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Sandbox;

// The engine is trusted infrastructure, never a tenant tool. Only its inspected runsc profile may execute code.
public sealed class DockerSandbox : ISandboxExecutor
{
    private readonly IDockerCommands _docker;
    private readonly string _image;
    private readonly string _installation;
    private readonly string _engine;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _started = new();
    private const string Label = "io.hirezero.sandbox.";
    public string Identity => "docker-runsc-v4:" + _image + ":" + CompanyJson.Digest(new { _installation, _engine });

    public DockerSandbox(IDockerCommands docker, string image, string installationId, string engineId)
    {
        if (
            !image.StartsWith("sha256:", StringComparison.Ordinal)
            || image.Length != 71
            || !image[7..].All(Uri.IsHexDigit)
            || !Guid.TryParseExact(installationId, "N", out _)
            || string.IsNullOrWhiteSpace(engineId)
            || engineId.Length > 200
        )
            throw new ArgumentException("Pin a local image ID and the installation's durable identifier.");
        _docker = docker;
        _image = image.ToLowerInvariant();
        _installation = ContentGuard.Digest(installationId);
        _engine = engineId;
    }

    private Task<DockerReply> Command(
        string[] arguments,
        CancellationToken token,
        string? input = null,
        int seconds = 10,
        int limit = 128 * 1024
    ) => _docker.RunAsync(arguments, input, TimeSpan.FromSeconds(seconds), limit, token);

    private string Name(string company, string job) =>
        "hz-sandbox-" + _installation[..8] + "-" + ContentGuard.Digest(company)[..12] + "-" + job;

    private static JsonElement Json(DockerReply reply)
    {
        if (reply.ExitCode != 0)
            throw new IOException("Sandbox engine command failed.");
        using var document = JsonDocument.Parse(reply.Output);
        return document.RootElement.Clone();
    }

    private static string? Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static bool True(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.True;

    private static bool Empty(JsonElement value, string key) =>
        !value.TryGetProperty(key, out var field)
        || field.ValueKind == JsonValueKind.Null
        || field.ValueKind == JsonValueKind.Array && field.GetArrayLength() == 0
        || field.ValueKind == JsonValueKind.Object && !field.EnumerateObject().Any();

    public static async Task<SandboxEngineProfile?> InspectEngineAsync(IDockerCommands docker, CancellationToken token)
    {
        try
        {
            var info = Json(
                await docker.RunAsync(
                    ["info", "--format", "{{json .}}"],
                    null,
                    TimeSpan.FromSeconds(10),
                    128 * 1024,
                    token
                )
            );
            if (
                Text(info, "OSType") != "linux"
                || !info.GetProperty("Runtimes").TryGetProperty("runsc", out var runsc)
                || !True(info, "MemoryLimit")
                || !True(info, "SwapLimit")
                || !True(info, "PidsLimit")
                || !True(info, "CpuCfsQuota")
            )
                return null;
            var fields = new[] { "ID", "ServerVersion", "KernelVersion", "Architecture" }
                .Select(key => Text(info, key))
                .ToArray();
            if (fields.Any(f => string.IsNullOrWhiteSpace(f) || f.Length > 200))
                return null;
            return new(fields[0]!, fields[1]!, fields[2]!, fields[3]!, DockerRuntimeProfile.Digest(runsc));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    private async Task<bool> SameEngineAsync(CancellationToken token)
    {
        // Cleanup still works if runsc or resource support changes. Identity, unlike readiness, cannot be waived.
        var info = Json(await Command(["info", "--format", "{{json .}}"], token));
        return Text(info, "OSType") == "linux" && Text(info, "ID") == _engine;
    }

    public async Task<bool> AvailableAsync(CancellationToken token)
    {
        try
        {
            if ((await InspectEngineAsync(_docker, token))?.Id != _engine)
                return false;
            var images = Json(await Command(["image", "inspect", _image], token));
            if (images.GetArrayLength() != 1)
                return false;
            var image = images[0];
            return Text(image, "Id") == _image
                && Text(image, "Os") == "linux"
                && Empty(image.GetProperty("Config"), "Volumes")
                && Text(image.GetProperty("Config").GetProperty("Labels"), Label + "worker") == "2";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }

    private string[] CreateArguments(SandboxRequest request, string name) =>
        [
            "container",
            "create",
            "--pull",
            "never",
            "--name",
            name,
            "--label",
            Label + "installation=" + _installation,
            "--label",
            Label + "company=" + ContentGuard.Digest(request.CompanyId),
            "--label",
            Label + "job=" + request.JobId,
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
            "--security-opt",
            "no-new-privileges",
            "--pids-limit",
            "128",
            "--cpus",
            "1",
            "--memory",
            request.Limits.MemoryMiB.ToString(CultureInfo.InvariantCulture) + "m",
            "--memory-swap",
            request.Limits.MemoryMiB.ToString(CultureInfo.InvariantCulture) + "m",
            "--tmpfs",
            "/work:rw,nosuid,nodev,noexec,size="
                + request.Limits.TemporaryMiB.ToString(CultureInfo.InvariantCulture)
                + "m,mode=1777",
            "--tmpfs",
            "/tmp:rw,nosuid,nodev,noexec,size=8m,mode=1777",
            "--ipc",
            "none",
            "--cgroupns",
            "private",
            "--ulimit",
            "nproc=32:32",
            "--ulimit",
            "nofile=128:128",
            "--ulimit",
            "core=0:0",
            "--restart",
            "no",
            "--log-driver",
            "local",
            "--log-opt",
            "max-size=1m",
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
            "--v8-pool-size=1",
            "/opt/hirezero/runner.mjs",
        ];

    private bool Owned(JsonElement container, string company, string job) =>
        Text(container, "Name") == "/" + Name(company, job)
        && Text(container, "Image") == _image
        && container.TryGetProperty("Config", out var config)
        && config.TryGetProperty("Labels", out var labels)
        && Text(labels, Label + "installation") == _installation
        && Text(labels, Label + "company") == ContentGuard.Digest(company)
        && Text(labels, Label + "job") == job;

    private static bool ExactStrings(JsonElement value, string key, params string[] expected) =>
        value.TryGetProperty(key, out var field)
        && field.ValueKind == JsonValueKind.Array
        && field
            .EnumerateArray()
            .Select(v => v.GetString())
            .Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Order(StringComparer.Ordinal));

    private bool Envelope(JsonElement container, SandboxRequest request)
    {
        if (!Owned(container, request.CompanyId, request.JobId))
            return false;
        var config = container.GetProperty("Config");
        var host = container.GetProperty("HostConfig");
        return Text(config, "User") == "0:0"
            && Text(config, "WorkingDir") == "/work"
            && ExactStrings(config, "Entrypoint", "/usr/local/bin/node")
            && config
                .GetProperty("Cmd")
                .EnumerateArray()
                .Select(v => v.GetString())
                .SequenceEqual(["--v8-pool-size=1", "/opt/hirezero/runner.mjs"])
            && True(config, "OpenStdin")
            && Text(host, "Runtime") == "runsc"
            && Text(host, "NetworkMode") == "none"
            && True(host, "ReadonlyRootfs")
            && !True(host, "Privileged")
            && Text(host, "PidMode") is "" or null
            && Text(host, "IpcMode") == "none"
            && Text(host, "CgroupnsMode") == "private"
            && host.GetProperty("Memory").GetInt64() == request.Limits.MemoryMiB * 1024L * 1024
            && host.GetProperty("MemorySwap").GetInt64() == request.Limits.MemoryMiB * 1024L * 1024
            && host.GetProperty("NanoCpus").GetInt64() == 1_000_000_000
            && host.GetProperty("PidsLimit").GetInt64() == 128
            && ExactUlimits(host)
            && ExactStrings(host, "CapDrop", "ALL")
            && DockerCapabilities.Exact(host, "CapAdd", "SETGID", "SETUID")
            && ExactStrings(host, "SecurityOpt", "no-new-privileges")
            && Empty(host, "Binds")
            && Empty(host, "Mounts")
            && Empty(host, "Devices")
            && Empty(host, "DeviceRequests")
            && Empty(host, "PortBindings")
            && Empty(host, "VolumesFrom")
            && Text(host.GetProperty("RestartPolicy"), "Name") == "no"
            && Text(host.GetProperty("LogConfig"), "Type") == "local"
            && Text(host.GetProperty("LogConfig").GetProperty("Config"), "max-size") == "1m"
            && Text(host.GetProperty("LogConfig").GetProperty("Config"), "max-file") == "1"
            && Text(host.GetProperty("LogConfig").GetProperty("Config"), "compress") == "false"
            && host.GetProperty("Tmpfs").EnumerateObject().Count() == 2
            && Text(host.GetProperty("Tmpfs"), "/work")
                == "rw,nosuid,nodev,noexec,size="
                    + request.Limits.TemporaryMiB.ToString(CultureInfo.InvariantCulture)
                    + "m,mode=1777"
            && Text(host.GetProperty("Tmpfs"), "/tmp") == "rw,nosuid,nodev,noexec,size=8m,mode=1777"
            && container
                .GetProperty("Mounts")
                .EnumerateArray()
                .All(m => Text(m, "Type") == "tmpfs" && Text(m, "Destination") is "/work" or "/tmp");
    }

    private static bool ExactUlimits(JsonElement host)
    {
        if (
            !host.TryGetProperty("Ulimits", out var limits)
            || limits.ValueKind != JsonValueKind.Array
            || limits.GetArrayLength() != 3
        )
            return false;
        var remaining = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["nproc"] = 32,
            ["nofile"] = 128,
            ["core"] = 0,
        };
        foreach (var limit in limits.EnumerateArray())
        {
            if (
                limit.ValueKind != JsonValueKind.Object
                || Text(limit, "Name") is not { } name
                || !remaining.Remove(name, out var expected)
                || !limit.TryGetProperty("Soft", out var soft)
                || soft.ValueKind != JsonValueKind.Number
                || !soft.TryGetInt64(out var softValue)
                || softValue != expected
                || !limit.TryGetProperty("Hard", out var hard)
                || hard.ValueKind != JsonValueKind.Number
                || !hard.TryGetInt64(out var hardValue)
                || hardValue != expected
            )
                return false;
        }
        return remaining.Count == 0;
    }

    private async Task<JsonElement?> Inspect(string name, CancellationToken token)
    {
        var reply = await Command(["container", "inspect", name], token);
        if (reply.ExitCode == 0)
        {
            var items = Json(reply);
            return items.GetArrayLength() == 1
                ? items[0].Clone()
                : throw new IOException("Ambiguous sandbox resource.");
        }
        var listed = await Command(
            ["container", "ls", "--all", "--quiet", "--no-trunc", "--filter", "name=^/" + name + "$"],
            token
        );
        if (listed.ExitCode == 0 && string.IsNullOrWhiteSpace(listed.Output))
            return null;
        throw new IOException("Sandbox resource state is unconfirmed.");
    }

    public async Task<SandboxRelease> ReleaseAsync(
        string companyId,
        string jobId,
        bool creationConfirmed,
        CancellationToken token
    )
    {
        if (string.IsNullOrWhiteSpace(companyId) || !Guid.TryParseExact(jobId, "N", out _))
            throw new ArgumentException("A scoped sandbox identity is required.");
        var name = Name(companyId, jobId);
        try
        {
            if (!await SameEngineAsync(token))
                return new(false, "scope-refused", name);
            var current = await Inspect(name, token);
            if (current is null)
                return creationConfirmed && await SameEngineAsync(token)
                    ? new(true, "released", name)
                    : new(false, "provisioning-unconfirmed", name);
            if (!Owned(current.Value, companyId, jobId))
                return new(false, "scope-refused", name);
            var id = Text(current.Value, "Id");
            if (id is null || id.Length != 64 || !id.All(Uri.IsHexDigit))
                return new(false, "scope-refused", name);
            var removed = await Command(["container", "rm", "--force", id], token);
            return removed.ExitCode == 0 && await Inspect(name, token) is null && await SameEngineAsync(token)
                ? new(true, "released", name)
                : new(false, "release-unconfirmed", name);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new(false, "release-unconfirmed", name);
        }
    }

    public async Task<SandboxResult> ExecuteAsync(
        SandboxRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request.Validate();
        request = request with { Inputs = request.Inputs.ToArray(), Outputs = request.Outputs.ToArray() };
        request.Validate();
        var name = Name(request.CompanyId, request.JobId);
        var watch = Stopwatch.StartNew();
        SandboxResult Result(string state, string reason, SandboxRelease release) =>
            new(state, null, "", "", [], watch.ElapsedMilliseconds, release, reason);
        if (!_started.TryAdd(name, 0))
            return Result("not-sent", "already-attempted", new(false, "not-observed", name));
        if (!await AvailableAsync(token))
            return Result("not-sent", "sandbox-unavailable", new(true, "not-created", name));
        if (!stillAuthorized() || token.IsCancellationRequested)
            return Result("not-sent", "authority-changed", new(true, "not-created", name));
        try
        {
            if (await Inspect(name, token) is not null)
                return Result("unknown", "existing-resource", new(false, "not-observed", name));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Result("not-sent", "resource-state-unconfirmed", new(false, "not-observed", name));
        }
        // The caller persists dispatch BEFORE entering here. Once create is attempted, uncertainty is never replayed.
        var created = false;
        var state = "unknown";
        var reason = "provisioning-unconfirmed";
        SandboxResult? result = null;
        try
        {
            var provision = await Command(CreateArguments(request, name), token);
            created = provision.ExitCode == 0;
            var container = await Inspect(name, token);
            if (!created || container is null || !Envelope(container.Value, request))
                throw new IOException("Sandbox envelope is unconfirmed.");
            if (
                True(container.Value.GetProperty("State"), "Running")
                || Text(container.Value.GetProperty("State"), "Status") != "created"
            )
                throw new IOException("Sandbox is not a fresh environment.");
            if (!stillAuthorized() || token.IsCancellationRequested)
            {
                state = "not-sent";
                reason = "authority-changed";
            }
            else
            {
                reason = "execution-unconfirmed";
                var reply = await Command(
                    ["container", "start", "--attach", "--interactive", name],
                    token,
                    CompanyJson.Encode(
                        new
                        {
                            script = request.Script,
                            inputs = request.Inputs,
                            outputs = request.Outputs,
                            seconds = request.Limits.Seconds,
                            tier = request.Tier,
                        }
                    ),
                    request.Limits.Seconds + 15,
                    1024 * 1024
                );
                var after = await Inspect(name, CancellationToken.None);
                if (after is null)
                    throw new IOException("Sandbox exit is unconfirmed.");
                result = ReadReceipt(request, reply, after.Value, watch.ElapsedMilliseconds);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        // Persist this receipt before calling ReleaseAsync. Bounded engine logs retain the output across a host crash.
        return result
            ?? Result(state, reason, new(false, "awaiting-release", name)) with
            {
                CreationConfirmed = created,
            };
    }

    private SandboxResult ReadReceipt(
        SandboxRequest request,
        DockerReply reply,
        JsonElement container,
        long? milliseconds
    )
    {
        if (
            reply.ExitCode != 0
            || !Envelope(container, request)
            || True(container.GetProperty("State"), "Running")
            || Text(container.GetProperty("State"), "Status") != "exited"
            || container.GetProperty("State").GetProperty("ExitCode").GetInt32() != 0
            || True(container.GetProperty("State"), "OOMKilled")
        )
            throw new IOException("Sandbox exit is unconfirmed.");
        var payload = CompanyJson.Decode<WorkerReply>(reply.Output);
        if (
            payload.State is not ("completed" or "failed" or "timed-out" or "output-limit")
            || payload.Stdout is null
            || payload.Stderr is null
            || payload.Outputs is null
            || Encoding.UTF8.GetByteCount(payload.Stdout) + Encoding.UTF8.GetByteCount(payload.Stderr)
                > SandboxRequest.OutputBytes
            || payload.Outputs.Length > request.Outputs.Length
            || payload.Outputs.Any(f =>
                f is null || f.Text is null || !request.Outputs.Contains(f.Name, StringComparer.Ordinal)
            )
            || payload.Outputs.Select(f => f.Name).Distinct().Count() != payload.Outputs.Length
            || payload.Outputs.Sum(f => (long)Encoding.UTF8.GetByteCount(f.Text)) > SandboxRequest.OutputBytes
            || payload.State == "completed"
                && (payload.ExitCode != 0 || payload.Outputs.Length != request.Outputs.Length)
        )
            throw new IOException("Sandbox result exceeds its scope.");
        return new(
            payload.State,
            payload.ExitCode,
            payload.Stdout,
            payload.Stderr,
            payload.Outputs,
            milliseconds,
            new(false, "awaiting-release", Name(request.CompanyId, request.JobId)),
            CreationConfirmed: true
        );
    }

    public async Task<SandboxResult> RecoverAsync(SandboxRequest request, CancellationToken token)
    {
        request.Validate();
        var name = Name(request.CompanyId, request.JobId);
        var confirmed = false;
        try
        {
            if (!await SameEngineAsync(token))
                return new(
                    "unknown",
                    null,
                    "",
                    "",
                    [],
                    null,
                    new(false, "awaiting-reconciliation", name),
                    "engine-changed"
                );
            var container = await Inspect(name, token);
            if (container is not null && Owned(container.Value, request.CompanyId, request.JobId))
            {
                confirmed = true;
                if (
                    !True(container.Value.GetProperty("State"), "Running")
                    && Text(container.Value.GetProperty("State"), "Status") == "exited"
                )
                {
                    var logs = await Command(["container", "logs", name], token, limit: 1024 * 1024);
                    return ReadReceipt(request, logs, container.Value, null);
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        return new(
            "unknown",
            null,
            "",
            "",
            [],
            null,
            new(false, "awaiting-reconciliation", name),
            "recovery-unconfirmed",
            confirmed
        );
    }

    private sealed record WorkerReply(string State, int? ExitCode, string Stdout, string Stderr, SandboxFile[] Outputs);
}
