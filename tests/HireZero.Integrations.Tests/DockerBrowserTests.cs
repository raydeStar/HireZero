using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed partial class DockerBrowserTests
{
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Installation = "11111111111111111111111111111111";
    private const string ContainerId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Network : IBrowserNetworkSession
    {
        public int Calls;
        public bool Disposed;
        public BrowserNetworkUsage Usage => new(Calls, Calls * 4, 0);

        public Task<BrowserResourceReply> FetchAsync(BrowserResourceRequest request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(
                new BrowserResourceReply(
                    200,
                    new() { ["Content-Type"] = "text/plain" },
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("Page")),
                    DateTimeOffset.UtcNow
                )
            );
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Engine(BrowserRequest request) : IDockerDuplexCommands
    {
        private readonly BrowserRequest _packet = request with
        {
            Origins = request.Origins.ToArray(),
            Steps = request.Steps.ToArray(),
        };
        public readonly List<string[]> Calls = [];
        public readonly List<Network> Networks = [];
        public JsonObject? Container;
        public string EngineId = "fictional-browser-engine";
        public string? Logs;
        public string? Input;
        public bool Available = true,
            OrdinaryRuntime,
            LostCreate,
            LostStart,
            FailRemove,
            Oom;
        public int Starts;
        public Action<JsonObject>? Alter;
        public Action? AfterCreate;
        public Action? AfterExchange;

        public DockerBrowser Runtime(string? image = null, string? engineId = null) =>
            new(
                this,
                image ?? Image,
                Installation,
                engineId ?? EngineId,
                (_, _) =>
                {
                    var network = new Network();
                    Networks.Add(network);
                    return network;
                }
            );

        private bool Matches(string name) => Container?["Name"]?.GetValue<string>() == "/" + name;

        public Task<DockerReply> RunAsync(
            string[] args,
            string? input,
            TimeSpan timeout,
            int outputLimit,
            CancellationToken token
        )
        {
            Calls.Add(args);
            DockerReply Json(object value) => new(0, JsonSerializer.Serialize(value));
            if (args[0] == "info")
                return Task.FromResult(
                    Available
                        ? Json(
                            new
                            {
                                ID = EngineId,
                                OSType = "linux",
                                ServerVersion = "fictional-version",
                                KernelVersion = "fictional-kernel",
                                Architecture = "amd64",
                                Runtimes = new Dictionary<string, object>
                                {
                                    [OrdinaryRuntime ? "runc" : "runsc"] = new { },
                                },
                                MemoryLimit = true,
                                SwapLimit = true,
                                PidsLimit = true,
                                CpuCfsQuota = true,
                            }
                        )
                        : new(1, "")
                );
            if (args[0] == "image")
                return Task.FromResult(
                    Json(
                        new[]
                        {
                            new
                            {
                                Id = Image,
                                Os = "linux",
                                Config = new
                                {
                                    Volumes = (object?)null,
                                    Labels = new Dictionary<string, string> { ["io.hirezero.browser.worker"] = "1" },
                                },
                            },
                        }
                    )
                );
            if (args[1] == "create")
            {
                string Flag(string name) => args[Array.IndexOf(args, name) + 1];
                string[] Flags(string name) =>
                    args.Select((value, index) => (value, index))
                        .Where(p => p.value == name)
                        .Select(p => args[p.index + 1])
                        .ToArray();
                Container = JsonSerializer
                    .SerializeToNode(
                        new
                        {
                            Id = ContainerId,
                            Name = "/" + Flag("--name"),
                            Image,
                            Config = new
                            {
                                Labels = Flags("--label")
                                    .Select(s => s.Split('=', 2))
                                    .ToDictionary(p => p[0], p => p[1]),
                                User = Flag("--user"),
                                WorkingDir = Flag("--workdir"),
                                Entrypoint = new[] { Flag("--entrypoint") },
                                Cmd = new[] { args[^1] },
                                OpenStdin = true,
                                Tty = false,
                                Healthcheck = new { Test = new[] { "NONE" } },
                            },
                            HostConfig = new
                            {
                                Runtime = Flag("--runtime"),
                                NetworkMode = Flag("--network"),
                                ReadonlyRootfs = true,
                                Privileged = false,
                                AutoRemove = false,
                                PidMode = "",
                                IpcMode = Flag("--ipc"),
                                CgroupnsMode = Flag("--cgroupns"),
                                Memory = 512L * 1024 * 1024,
                                MemorySwap = 512L * 1024 * 1024,
                                NanoCpus = 1_000_000_000L,
                                PidsLimit = 128,
                                CapDrop = Flags("--cap-drop"),
                                CapAdd = Flags("--cap-add").Select(value => "CAP_" + value).ToArray(),
                                SecurityOpt = Flags("--security-opt"),
                                RestartPolicy = new { Name = Flag("--restart") },
                                Ulimits = new[]
                                {
                                    new
                                    {
                                        Name = "nofile",
                                        Soft = 1024,
                                        Hard = 1024,
                                    },
                                    new
                                    {
                                        Name = "core",
                                        Soft = 0,
                                        Hard = 0,
                                    },
                                },
                                LogConfig = new
                                {
                                    Type = Flag("--log-driver"),
                                    Config = Flags("--log-opt")
                                        .Select(s => s.Split('=', 2))
                                        .ToDictionary(p => p[0], p => p[1]),
                                },
                                Tmpfs = Flags("--tmpfs")
                                    .Select(s => s.Split(':', 2))
                                    .ToDictionary(p => p[0], p => p[1]),
                            },
                            Mounts = Array.Empty<object>(),
                            State = new
                            {
                                Running = false,
                                Status = "created",
                                ExitCode = 0,
                                OOMKilled = false,
                            },
                        }
                    )!
                    .AsObject();
                Alter?.Invoke(Container);
                AfterCreate?.Invoke();
                if (LostCreate)
                    throw new IOException("Fictional lost creation acknowledgement.");
                return Task.FromResult(new DockerReply(0, ContainerId));
            }
            if (args[1] == "inspect")
                return Task.FromResult(
                    Matches(args[^1]) ? new DockerReply(0, "[" + Container!.ToJsonString() + "]") : new(1, "")
                );
            if (args[1] == "ls")
                return Task.FromResult(
                    new DockerReply(
                        0,
                        Container is not null && args[^1] == "name=^" + Container["Name"]!.GetValue<string>() + "$"
                            ? ContainerId
                            : ""
                    )
                );
            if (args[1] == "logs")
                return Task.FromResult(new DockerReply(Logs is null ? 1 : 0, Logs ?? ""));
            if (args[1] == "rm")
            {
                Assert.Equal(ContainerId, args[^1]);
                if (FailRemove)
                    return Task.FromResult(new DockerReply(1, ""));
                Container = null;
                return Task.FromResult(new DockerReply(0, ContainerId));
            }
            throw new InvalidOperationException("Unexpected fictional engine command.");
        }

        public void Finish()
        {
            var request = _packet;
            var result = new BrowserResult(
                request.JobId,
                "completed",
                2,
                false,
                [
                    new(
                        2,
                        request.Origins[0] + "/",
                        DateTimeOffset.UtcNow,
                        "Fictional public page",
                        "Fictional saved browser evidence.",
                        false,
                        []
                    ),
                ],
                [],
                [],
                true,
                new(1, 4),
                50
            );
            Logs =
                CompanyJson.Encode(
                    new
                    {
                        type = "resource",
                        jobId = request.JobId,
                        id = 1,
                        request = new { url = request.Origins[0] + "/", method = "GET" },
                    }
                )
                + "\n"
                + CompanyJson.Encode(
                    new
                    {
                        type = "result",
                        jobId = request.JobId,
                        result,
                        browserExited = true,
                    }
                )
                + "\n";
            Container!["State"]!["Running"] = false;
            Container["State"]!["Status"] = "exited";
            Container["State"]!["OOMKilled"] = Oom;
        }

        public async Task<DockerExchange<T>> ExchangeAsync<T>(
            string[] args,
            Func<Stream, Stream, CancellationToken, Task<T>> exchange,
            TimeSpan timeout,
            CancellationToken token
        )
        {
            Calls.Add(args);
            Starts++;
            Assert.Equal(
                new[]
                {
                    "container",
                    "start",
                    "--attach",
                    "--interactive",
                    Container!["Name"]!.GetValue<string>()[1..],
                },
                args
            );
            if (LostStart)
            {
                Container["State"]!["Running"] = true;
                Container["State"]!["Status"] = "running";
                throw new IOException("Fictional lost attached session.");
            }
            Finish();
            using var output = new MemoryStream(Encoding.UTF8.GetBytes(Logs!));
            using var input = new MemoryStream();
            var value = await exchange(output, input, token);
            Input = Encoding.UTF8.GetString(input.ToArray());
            AfterExchange?.Invoke();
            return new(0, value);
        }
    }

    private static Task<SandboxRelease> Release(
        DockerBrowser runtime,
        BrowserRequest request,
        BrowserExecution result
    ) => runtime.ReleaseAsync(request, result.Release.ResourceId, result.CreationConfirmed, Token);

    [Fact]
    public async Task AScopedBrowserRunsOnceAndRetainsItsResultUntilExplicitRelease()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("completed", result.State);
        Assert.True(result.CreationConfirmed);
        Assert.False(result.Release.Confirmed);
        Assert.NotNull(engine.Container);
        Assert.Single(result.Result!.Captures);
        Assert.True(result.BrowserExited);
        Assert.True(Assert.Single(engine.Networks).Disposed);
        Assert.Equal(1, result.Network!.Requests);
        Assert.DoesNotContain(engine.Calls, c => c.Contains("rm"));
        Assert.DoesNotContain(
            engine.Calls.SelectMany(c => c),
            arg => arg is "--volume" or "--mount" or "--publish" or "--privileged" or "pull"
        );
        Assert.DoesNotContain(
            engine.Calls.SelectMany(c => c),
            arg => arg.Contains("paper.example.invalid", StringComparison.Ordinal)
        );
        Assert.Contains("paper.example.invalid", engine.Input!);
        Assert.Equal("already-attempted", (await runtime.ExecuteAsync(request, () => true, Token)).Reason);
        Assert.Equal(1, engine.Starts);
        Assert.True((await Release(runtime, request, result)).Confirmed);
        Assert.Null(engine.Container);
        Assert.Single(result.Result.Captures);
    }

    [Fact]
    public async Task UnprefixedCapabilityNamesStillDescribeTheSameExactBrowserGrants()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request)
        {
            Alter = container => container["HostConfig"]!["CapAdd"] = new JsonArray("KILL", "SETGID", "SETUID"),
        };
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("completed", result.State);
        Assert.Equal(1, engine.Starts);
        Assert.True((await Release(runtime, request, result)).Confirmed);
    }

    [Fact]
    public async Task AnUnavailableOrOrdinaryEngineCannotProvisionBrowserWork()
    {
        foreach (var ordinary in new[] { true, false })
        {
            var request = BrowserProtocolTests.Scope();
            var engine = new Engine(request) { OrdinaryRuntime = ordinary, Available = ordinary };
            var result = await engine.Runtime().ExecuteAsync(request, () => true, Token);
            Assert.Equal("not-sent", result.State);
            Assert.True(result.Release.Confirmed);
            Assert.DoesNotContain(engine.Calls, c => c.Contains("create"));
            Assert.Empty(engine.Networks);
        }
    }

    [Fact]
    public async Task AuthorityLostAfterCreationNeverStartsOrOpensANetworkSession()
    {
        var request = BrowserProtocolTests.Scope();
        var authorized = true;
        var engine = new Engine(request) { AfterCreate = () => authorized = false };
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => authorized, Token);
        Assert.Equal("not-sent", result.State);
        Assert.True(result.CreationConfirmed);
        Assert.Equal(0, engine.Starts);
        Assert.Empty(engine.Networks);
        Assert.True((await Release(runtime, request, result)).Confirmed);
    }

    [Fact]
    public async Task RuntimeAddedAccessOrRelaxedLimitsAreRefusedBeforeStart()
    {
        foreach (
            var change in new Action<JsonObject>[]
            {
                c => c["HostConfig"]!["Binds"] = new JsonArray("/fictional:/private"),
                c => c["HostConfig"]!["NetworkMode"] = "host",
                c => c["HostConfig"]!["Memory"] = 1024L * 1024 * 1024,
                c => c["HostConfig"]!["AutoRemove"] = true,
                c => c["Config"]!["Tty"] = true,
                c => c["Config"]!["Healthcheck"]!["Test"] = new JsonArray("CMD", "unexpected"),
                c => c["HostConfig"]!["Ulimits"]![0]!["Hard"] = 999999,
                c => c["HostConfig"]!["CapAdd"] = new JsonArray("ALL"),
                c =>
                    c["HostConfig"]!["CapAdd"] = new JsonArray("CAP_SETUID", "CAP_SETGID", "CAP_KILL", "CAP_SYS_ADMIN"),
                c => c["HostConfig"]!["CapAdd"] = new JsonArray("CAP_SETUID", "CAP_SETGID"),
                c => c["HostConfig"]!["CapAdd"] = new JsonArray("CAP_SETUID", "SETUID", "CAP_KILL"),
                c => c["HostConfig"]!["CapAdd"] = new JsonArray("CAP_CAP_SETUID", "CAP_SETGID", "CAP_KILL"),
                c => c["HostConfig"]!["CapAdd"] = new JsonArray("cap_SETUID", "CAP_SETGID", "CAP_KILL"),
                c => c["HostConfig"]!["CapAdd"] = new JsonArray(7, "CAP_SETGID", "CAP_KILL"),
                c => c["HostConfig"]!["LogConfig"]!["Config"]!["compress"] = "true",
                c => c["HostConfig"]!["LogConfig"]!["Config"]!.AsObject().Remove("compress"),
            }
        )
        {
            var request = BrowserProtocolTests.Scope();
            var engine = new Engine(request) { Alter = change };
            var runtime = engine.Runtime();
            var result = await runtime.ExecuteAsync(request, () => true, Token);
            Assert.Equal("unknown", result.State);
            Assert.Equal(0, engine.Starts);
            Assert.Empty(engine.Networks);
            Assert.True((await Release(runtime, request, result)).Confirmed);
        }
    }

    [Fact]
    public async Task LostStartCanBeRecoveredFromTheOriginalLogsWithoutASecondRequest()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request) { LostStart = true };
        var runtime = engine.Runtime();
        var uncertain = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("unknown", uncertain.State);
        Assert.True(uncertain.CreationConfirmed);
        Assert.Equal("unknown", (await runtime.RecoverAsync(request, Token)).State);
        engine.Finish();
        var recovered = await engine.Runtime().RecoverAsync(request, Token);
        Assert.Equal("completed", recovered.State);
        Assert.Null(recovered.Network);
        Assert.Null(recovered.WallMilliseconds);
        Assert.Equal("network-usage-unconfirmed", recovered.Reason);
        Assert.Single(recovered.Result!.Captures);
        Assert.Equal(1, engine.Starts);
        Assert.Single(engine.Networks);
        Assert.Equal(0, engine.Networks[0].Calls);
        Assert.True((await Release(runtime, request, recovered)).Confirmed);
    }

    [Fact]
    public async Task LostCreationAcknowledgementNeverBecomesPermissionToCreateAgain()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request) { LostCreate = true };
        var runtime = engine.Runtime();
        var uncertain = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("unknown", uncertain.State);
        Assert.False(uncertain.CreationConfirmed);
        Assert.Equal(0, engine.Starts);
        Assert.Equal("existing-resource", (await engine.Runtime().ExecuteAsync(request, () => true, Token)).Reason);
        Assert.Single(engine.Calls, c => c.Contains("create"));
        Assert.True((await Release(runtime, request, uncertain)).Confirmed);
        Assert.False((await runtime.ReleaseAsync(request, uncertain.Release.ResourceId, false, Token)).Confirmed);
    }

    [Fact]
    public async Task WrongCompanyChangedRequestAndReplacementRuntimeCannotReleaseTheOriginal()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.False(
            (
                await runtime.ReleaseAsync(
                    request with
                    {
                        CompanyId = "another-fictional-company",
                    },
                    result.Release.ResourceId,
                    true,
                    Token
                )
            ).Confirmed
        );
        Assert.False(
            (await runtime.ReleaseAsync(request with { Width = 800 }, result.Release.ResourceId, true, Token)).Confirmed
        );
        engine.EngineId = "replacement-engine";
        Assert.False((await Release(runtime, request, result)).Confirmed);
        Assert.Equal("engine-changed", (await runtime.RecoverAsync(request, Token)).Reason);
        var replacement = engine.Runtime();
        Assert.NotEqual(runtime.Identity, replacement.Identity);
        Assert.False((await Release(replacement, request, result)).Confirmed);
        Assert.False(
            (
                await Release(
                    engine.Runtime("sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"),
                    request,
                    result
                )
            ).Confirmed
        );
        Assert.DoesNotContain(engine.Calls, c => c.Contains("rm"));
        engine.EngineId = "fictional-browser-engine";
        Assert.True((await Release(runtime, request, result)).Confirmed);
    }

    [Fact]
    public async Task MissingOrCorruptLogsAndOomNeverTurnIntoCompletedWork()
    {
        foreach (var corrupt in new[] { "", "truncated", "foreign", "oom" })
        {
            var request = BrowserProtocolTests.Scope();
            var engine = new Engine(request) { Oom = corrupt == "oom" };
            var runtime = engine.Runtime();
            var result = await runtime.ExecuteAsync(request, () => true, Token);
            if (corrupt == "oom")
                Assert.Equal("unknown", result.State);
            else
                engine.Logs = corrupt switch
                {
                    "" => null,
                    "truncated" => engine.Logs![..^1],
                    _ => engine.Logs!.Replace(request.JobId, Guid.NewGuid().ToString("N"), StringComparison.Ordinal),
                };
            Assert.Equal("unknown", (await runtime.RecoverAsync(request, Token)).State);
            Assert.Equal(1, engine.Starts);
            Assert.True((await Release(runtime, request, result)).Confirmed);
        }
    }

    [Fact]
    public async Task ACallerCannotBroadenThePacketWhileProvisioningRuns()
    {
        var request = BrowserProtocolTests.Scope();
        var saved = request with { Origins = request.Origins.ToArray(), Steps = request.Steps.ToArray() };
        var engine = new Engine(request) { AfterCreate = () => request.Origins[0] = "https://outside.example.invalid" };
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("completed", result.State);
        Assert.DoesNotContain("outside.example.invalid", engine.Input!);
        Assert.True((await Release(runtime, saved, result)).Confirmed);
    }

    [Fact]
    public async Task RevocationAfterTheWorkerFinishesStillWithholdsItsCaptures()
    {
        var request = BrowserProtocolTests.Scope();
        var authorized = true;
        var engine = new Engine(request) { AfterExchange = () => authorized = false };
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => authorized, Token);
        Assert.Equal("canceled", result.State);
        Assert.Empty(result.Result!.Captures);
        Assert.True(result.Result.Partial);
        Assert.True((await Release(runtime, request, result)).Confirmed);
    }

    [Fact]
    public async Task CleanupAndRecoveryRemainAvailableWhenNewExecutionSupportDisappears()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var runtime = engine.Runtime();
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        engine.OrdinaryRuntime = true;
        Assert.False(await runtime.AvailableAsync(Token));
        Assert.Equal("completed", (await runtime.RecoverAsync(request, Token)).State);
        engine.FailRemove = true;
        Assert.False((await Release(runtime, request, result)).Confirmed);
        Assert.NotNull(engine.Container);
        engine.FailRemove = false;
        Assert.True((await Release(runtime, request, result)).Confirmed);
    }
}
