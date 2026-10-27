using System.Text.Json;
using System.Text.Json.Nodes;
using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed class SandboxTests
{
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Installation = "11111111111111111111111111111111";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SandboxRequest Request() =>
        new(
            Guid.NewGuid().ToString("N"),
            "co-fictional",
            "console.log('Fictional result')",
            [new("input.txt", "Fictional private input")],
            ["result.txt"],
            new(5)
        );

    internal sealed class Engine : IDockerCommands
    {
        public readonly List<string[]> Calls = [];
        public JsonObject? Container;
        public bool Available = true,
            OrdinaryRuntimeOnly,
            LoseCreate,
            FailStart,
            FailRemove,
            UnexpectedOutput;
        public Action<JsonObject>? Alter;
        public Action? AfterCreate;
        public string? Input;
        public string? Receipt;
        public int Starts;
        public string EngineId = "fictional-engine";
        public string WorkerVersion = "2";
        private const string Id = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

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
                                ServerVersion = "fictional-version",
                                KernelVersion = "fictional-kernel",
                                Architecture = "amd64",
                                OSType = "linux",
                                Runtimes = new Dictionary<string, object>
                                {
                                    [OrdinaryRuntimeOnly ? "runc" : "runsc"] = new { },
                                },
                                MemoryLimit = true,
                                SwapLimit = true,
                                PidsLimit = true,
                                CpuCfsQuota = true,
                            }
                        )
                        : new DockerReply(1, "")
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
                                    Labels = new Dictionary<string, string>
                                    {
                                        ["io.hirezero.sandbox.worker"] = WorkerVersion,
                                    },
                                },
                            },
                        }
                    )
                );
            if (args[1] == "create")
            {
                string Flag(string flag) => args[Array.IndexOf(args, flag) + 1];
                string[] Flags(string flag) =>
                    args.Select((a, i) => (a, i)).Where(p => p.a == flag).Select(p => args[p.i + 1]).ToArray();
                var labels = Flags("--label").Select(s => s.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
                var tmpfs = Flags("--tmpfs").Select(s => s.Split(':', 2)).ToDictionary(p => p[0], p => p[1]);
                Container = JsonSerializer
                    .SerializeToNode(
                        new
                        {
                            Id,
                            Name = "/" + Flag("--name"),
                            Image,
                            Config = new
                            {
                                Labels = labels,
                                User = Flag("--user"),
                                WorkingDir = Flag("--workdir"),
                                Entrypoint = new[] { Flag("--entrypoint") },
                                Cmd = args[(Array.IndexOf(args, Image) + 1)..],
                                OpenStdin = true,
                            },
                            HostConfig = new
                            {
                                Runtime = Flag("--runtime"),
                                NetworkMode = Flag("--network"),
                                ReadonlyRootfs = true,
                                Privileged = false,
                                PidMode = "",
                                IpcMode = Flag("--ipc"),
                                CgroupnsMode = Flag("--cgroupns"),
                                Memory = 256L * 1024 * 1024,
                                MemorySwap = 256L * 1024 * 1024,
                                NanoCpus = 1_000_000_000L,
                                PidsLimit = int.Parse(
                                    Flag("--pids-limit"),
                                    System.Globalization.CultureInfo.InvariantCulture
                                ),
                                Ulimits = Flags("--ulimit")
                                    .Select(value =>
                                    {
                                        var parts = value.Split(['=', ':']);
                                        return new
                                        {
                                            Name = parts[0],
                                            Soft = long.Parse(
                                                parts[1],
                                                System.Globalization.CultureInfo.InvariantCulture
                                            ),
                                            Hard = long.Parse(
                                                parts[2],
                                                System.Globalization.CultureInfo.InvariantCulture
                                            ),
                                        };
                                    })
                                    .ToArray(),
                                CapDrop = Flags("--cap-drop"),
                                CapAdd = Flags("--cap-add").Select(value => "CAP_" + value).ToArray(),
                                SecurityOpt = Flags("--security-opt"),
                                RestartPolicy = new { Name = Flag("--restart") },
                                LogConfig = new
                                {
                                    Type = Flag("--log-driver"),
                                    Config = Flags("--log-opt")
                                        .Select(s => s.Split('=', 2))
                                        .ToDictionary(p => p[0], p => p[1]),
                                },
                                Tmpfs = tmpfs,
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
                if (LoseCreate)
                {
                    Container = null;
                    throw new IOException("Fictional lost create response");
                }
                return Task.FromResult(new DockerReply(0, Id));
            }
            if (args[1] == "inspect")
                return Task.FromResult(
                    Container is null
                        ? new DockerReply(1, "")
                        : new DockerReply(0, "[" + Container.ToJsonString() + "]")
                );
            if (args[1] == "ls")
                return Task.FromResult(new DockerReply(0, Container is null ? "" : Id));
            if (args[1] == "start")
            {
                Starts++;
                Input = input;
                if (FailStart)
                    throw new OperationCanceledException("Fictional lost execution response");
                Container!["State"]!["Status"] = "exited";
                Receipt = CompanyJson.Encode(
                    new
                    {
                        state = "completed",
                        exitCode = 0,
                        stdout = "Fictional result",
                        stderr = "",
                        outputs = new[]
                        {
                            new SandboxFile(
                                UnexpectedOutput ? "unapproved.txt" : "result.txt",
                                "Fictional saved output"
                            ),
                        },
                    }
                );
                return Task.FromResult(new DockerReply(0, Receipt));
            }
            if (args[1] == "logs")
                return Task.FromResult(new DockerReply(Receipt is null ? 1 : 0, Receipt ?? ""));
            if (args[1] == "rm")
            {
                Assert.Equal(Id, args[^1]);
                if (FailRemove)
                    return Task.FromResult(new DockerReply(1, ""));
                Container = null;
                return Task.FromResult(new DockerReply(0, Id));
            }
            throw new InvalidOperationException("Unexpected fictional engine command.");
        }
    }

    [Fact]
    public async Task ReplacementEngineCannotClaimAnOldResourceWasReleased()
    {
        var engine = new Engine();
        var runtime = new DockerSandbox(engine, Image, Installation, engine.EngineId);
        var request = Request();
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.True(result.CreationConfirmed);
        engine.EngineId = "replacement-engine";
        engine.Container = null;
        Assert.NotEqual(runtime.Identity, new DockerSandbox(engine, Image, Installation, engine.EngineId).Identity);
        Assert.NotEqual(
            runtime.Identity,
            new DockerSandbox(engine, Image, "22222222222222222222222222222222", "fictional-engine").Identity
        );
        Assert.False((await runtime.ReleaseAsync(request.CompanyId, request.JobId, true, Token)).Confirmed);
        Assert.Equal("engine-changed", (await runtime.RecoverAsync(request, Token)).Reason);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("rm"));
    }

    [Fact]
    public async Task CleanupStillWorksWhenTheOriginalEngineLosesExecutionSupport()
    {
        var engine = new Engine();
        var runtime = new DockerSandbox(engine, Image, Installation, engine.EngineId);
        var request = Request();
        await runtime.ExecuteAsync(request, () => true, Token);
        engine.OrdinaryRuntimeOnly = true;
        Assert.False(await runtime.AvailableAsync(Token));
        Assert.Equal("completed", (await runtime.RecoverAsync(request, Token)).State);
        Assert.True((await runtime.ReleaseAsync(request.CompanyId, request.JobId, true, Token)).Confirmed);
    }

    [Fact]
    public async Task AScopedPacketRunsOnceAndItsOutputsSurviveConfirmedRelease()
    {
        var engine = new Engine();
        var executor = new DockerSandbox(engine, Image, Installation, "fictional-engine");
        var request = Request();
        var result = await executor.ExecuteAndReleaseAsync(request, () => true, Token);
        Assert.Equal("completed", result.State);
        Assert.Equal("result.txt", Assert.Single(result.Outputs).Name);
        Assert.True(result.Release.Confirmed);
        Assert.Null(engine.Container);
        Assert.Equal(1, engine.Starts);
        Assert.Contains("Fictional private input", engine.Input!);
        Assert.DoesNotContain(
            engine.Calls.SelectMany(a => a),
            a => a.Contains("Fictional private input", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            engine.Calls.SelectMany(a => a),
            a => a is "--volume" or "--mount" or "--privileged" or "--publish" or "pull"
        );
        var again = await executor.ExecuteAndReleaseAsync(request, () => true, Token);
        Assert.Equal("already-attempted", again.Reason);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task ScriptWorkerRequiresBoundedPoolsAndRejectsAnOldImageOrReorderedNodeArguments()
    {
        var old = new Engine { WorkerVersion = "1" };
        Assert.False(await new DockerSandbox(old, Image, Installation, old.EngineId).AvailableAsync(Token));
        foreach (
            var command in new[]
            {
                new[] { "/opt/hirezero/runner.mjs" },
                new[] { "/opt/hirezero/runner.mjs", "--v8-pool-size=1" },
                new[] { "--v8-pool-size=4", "/opt/hirezero/runner.mjs" },
            }
        )
        {
            var engine = new Engine
            {
                Alter = container => container["Config"]!["Cmd"] = JsonSerializer.SerializeToNode(command),
            };
            var result = await new DockerSandbox(engine, Image, Installation, engine.EngineId).ExecuteAndReleaseAsync(
                Request(),
                () => true,
                Token
            );
            Assert.Equal("unknown", result.State);
            Assert.Equal(0, engine.Starts);
            Assert.True(result.Release.Confirmed);
        }
    }

    [Fact]
    public async Task CapabilitySpellingDoesNotChangeTheExactScriptGrants()
    {
        foreach (var names in new[] { new[] { "SETUID", "SETGID" }, new[] { "CAP_SETGID", "CAP_SETUID" } })
        {
            var engine = new Engine
            {
                Alter = container => container["HostConfig"]!["CapAdd"] = new JsonArray(names[0], names[1]),
            };
            var runtime = new DockerSandbox(engine, Image, Installation, engine.EngineId);
            var result = await runtime.ExecuteAndReleaseAsync(Request(), () => true, Token);
            Assert.Equal("completed", result.State);
            Assert.Equal(1, engine.Starts);
            Assert.True(result.Release.Confirmed);
        }
        foreach (
            var names in new[]
            {
                new[] { "CAP_SETUID", "CAP_SETGID", "CAP_SYS_ADMIN" },
                new[] { "CAP_SETUID" },
                new[] { "CAP_SETUID", "SETUID" },
                new[] { "CAP_CAP_SETUID", "CAP_SETGID" },
            }
        )
        {
            var engine = new Engine
            {
                Alter = container => container["HostConfig"]!["CapAdd"] = JsonSerializer.SerializeToNode(names),
            };
            var result = await new DockerSandbox(engine, Image, Installation, engine.EngineId).ExecuteAndReleaseAsync(
                Request(),
                () => true,
                Token
            );
            Assert.NotEqual("completed", result.State);
            Assert.Equal(0, engine.Starts);
        }
    }

    [Fact]
    public async Task InvalidSingleFileLogCompressionIsRefusedBeforeScriptStart()
    {
        foreach (var compression in new string?[] { "true", null })
        {
            var engine = new Engine
            {
                Alter = container =>
                {
                    var log = container["HostConfig"]!["LogConfig"]!["Config"]!.AsObject();
                    if (compression is null)
                        log.Remove("compress");
                    else
                        log["compress"] = compression;
                },
            };
            var result = await new DockerSandbox(engine, Image, Installation, engine.EngineId).ExecuteAndReleaseAsync(
                Request(),
                () => true,
                Token
            );
            Assert.NotEqual("completed", result.State);
            Assert.Equal(0, engine.Starts);
        }
    }

    [Fact]
    public async Task HostAndWorkloadLimitsMustMatchExactlyBeforeAnyScriptStarts()
    {
        var good = new Engine();
        var accepted = await new DockerSandbox(good, Image, Installation, good.EngineId).ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal("completed", accepted.State);
        var create = Assert.Single(good.Calls, a => a.Contains("create"));
        Assert.Equal("128", create[Array.IndexOf(create, "--pids-limit") + 1]);
        Assert.Contains("nproc=32:32", create);
        var changes = new List<Action<JsonObject>>
        {
            c => c["HostConfig"]!["PidsLimit"] = 32,
            c => c["HostConfig"]!["PidsLimit"] = 129,
            c => c["HostConfig"]!.AsObject().Remove("Ulimits"),
            c => c["HostConfig"]!["Ulimits"] = null,
            c => c["HostConfig"]!["Ulimits"] = new JsonObject(),
            c => c["HostConfig"]!["Ulimits"]!.AsArray().RemoveAt(0),
            c =>
                c["HostConfig"]!["Ulimits"]!
                    .AsArray()
                    .Add(
                        new JsonObject
                        {
                            ["Name"] = "stack",
                            ["Soft"] = 128,
                            ["Hard"] = 128,
                        }
                    ),
            c => c["HostConfig"]!["Ulimits"]![1] = c["HostConfig"]!["Ulimits"]![0]!.DeepClone(),
            c => c["HostConfig"]!["Ulimits"]![0]!["Name"] = "NPROC",
            c => c["HostConfig"]!["Ulimits"]![0]!["Soft"] = "32",
            c => c["HostConfig"]!["Ulimits"]![0]!.AsObject().Remove("Hard"),
            c => c["HostConfig"]!["Ulimits"]![0] = null,
        };
        for (var index = 0; index < 3; index++)
        {
            var slot = index;
            foreach (var bound in new[] { "Soft", "Hard" })
                changes.Add(c => c["HostConfig"]!["Ulimits"]![slot]![bound] = -1);
        }
        foreach (var change in changes)
        {
            var engine = new Engine { Alter = change };
            var result = await new DockerSandbox(engine, Image, Installation, engine.EngineId).ExecuteAndReleaseAsync(
                Request(),
                () => true,
                Token
            );
            Assert.NotEqual("completed", result.State);
            Assert.Equal(0, engine.Starts);
            Assert.True(result.Release.Confirmed);
            Assert.Null(engine.Container);
        }
    }

    [Fact]
    public async Task UnavailableEngineAndInvalidScopeNeverCreateAContainer()
    {
        var engine = new Engine { Available = false };
        var executor = new DockerSandbox(engine, Image, Installation, "fictional-engine");
        Assert.Equal("not-sent", (await executor.ExecuteAndReleaseAsync(Request(), () => true, Token)).State);
        foreach (var invalid in new[] { "../secret", "/absolute", "job.mjs", "a/b", ".hidden" })
            await Assert.ThrowsAsync<WorkspaceException>(() =>
                executor.ExecuteAndReleaseAsync(Request() with { Outputs = [invalid] }, () => true, Token)
            );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            executor.ExecuteAndReleaseAsync(
                Request() with
                {
                    Inputs = [new("same.txt", "a"), new("SAME.txt", "b")],
                },
                () => true,
                Token
            )
        );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            executor.ExecuteAndReleaseAsync(
                Request() with
                {
                    Inputs = [new("input.txt", new string('é', SandboxRequest.InputBytes))],
                },
                () => true,
                Token
            )
        );
        Assert.DoesNotContain(engine.Calls, a => a.Contains("create"));
    }

    [Fact]
    public async Task RevocationAfterProvisioningReleasesWithoutSendingInputs()
    {
        var authorized = true;
        var engine = new Engine { AfterCreate = () => authorized = false };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => authorized,
            Token
        );
        Assert.Equal("not-sent", result.State);
        Assert.True(result.Release.Confirmed);
        Assert.Null(engine.Input);
        Assert.Equal(0, engine.Starts);
    }

    [Fact]
    public async Task AnEngineThatAddsHostAccessIsRefusedBeforeStart()
    {
        var engine = new Engine { Alter = c => c["HostConfig"]!["Binds"] = new JsonArray("/fictional:/private") };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal("unknown", result.State);
        Assert.Equal(0, engine.Starts);
        Assert.True(result.Release.Confirmed);
    }

    [Fact]
    public async Task OwnershipMismatchCannotStartOrDeleteSomeoneElsesResource()
    {
        var engine = new Engine
        {
            Alter = c => c["Config"]!["Labels"]!["io.hirezero.sandbox.company"] = "another-company",
        };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal(0, engine.Starts);
        Assert.False(result.Release.Confirmed);
        Assert.Equal("scope-refused", result.Release.Status);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("rm"));
    }

    [Fact]
    public async Task LostCreateCannotBeDeclaredReleasedFromAnEmptyListing()
    {
        var engine = new Engine { LoseCreate = true };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal("unknown", result.State);
        Assert.False(result.Release.Confirmed);
        Assert.Equal("provisioning-unconfirmed", result.Release.Status);
        Assert.Equal(0, engine.Starts);
    }

    [Fact]
    public async Task InterruptedExecutionIsStoppedWithoutReplayingOrInventingOutputs()
    {
        var engine = new Engine { FailStart = true };
        var executor = new DockerSandbox(engine, Image, Installation, "fictional-engine");
        var request = Request();
        var result = await executor.ExecuteAndReleaseAsync(request, () => true, Token);
        Assert.Equal("unknown", result.State);
        Assert.Empty(result.Outputs);
        Assert.True(result.Release.Confirmed);
        await executor.ExecuteAndReleaseAsync(request, () => true, Token);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task CleanupFailureKeepsTheResultAndAnExplicitUnconfirmedRelease()
    {
        var engine = new Engine { FailRemove = true };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal("completed", result.State);
        Assert.Single(result.Outputs);
        Assert.False(result.Release.Confirmed);
        Assert.NotNull(engine.Container);
    }

    [Fact]
    public async Task OrdinaryDockerIsNotASilentFallbackForTheRequiredRuntime()
    {
        var engine = new Engine { OrdinaryRuntimeOnly = true };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal("sandbox-unavailable", result.Reason);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("create"));
    }

    [Fact]
    public async Task ExistingResourcesAreReconciledWithoutRestartingOrDeletingThemAsANewRun()
    {
        var engine = new Engine { FailRemove = true };
        var request = Request();
        await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            request,
            () => true,
            Token
        );
        var removes = engine.Calls.Count(a => a.Contains("rm"));
        var reopened = new DockerSandbox(engine, Image, Installation, "fictional-engine");
        var result = await reopened.ExecuteAndReleaseAsync(request, () => true, Token);
        Assert.Equal("existing-resource", result.Reason);
        Assert.Equal(1, engine.Starts);
        Assert.Equal(removes, engine.Calls.Count(a => a.Contains("rm")));
        engine.FailRemove = false;
        Assert.True((await reopened.ReleaseAsync(request.CompanyId, request.JobId, true, Token)).Confirmed);
        Assert.Null(engine.Container);
    }

    [Fact]
    public async Task RecoveryCannotConfirmAnInterruptedCreateMerelyBecauseItIsNotListedYet()
    {
        var engine = new Engine();
        var executor = new DockerSandbox(engine, Image, Installation, "fictional-engine");
        var request = Request();
        Assert.False((await executor.ReleaseAsync(request.CompanyId, request.JobId, false, Token)).Confirmed);
        Assert.True((await executor.ReleaseAsync(request.CompanyId, request.JobId, true, Token)).Confirmed);
        Assert.Equal(0, engine.Starts);
    }

    [Fact]
    public async Task ResultsRemainRecoverableUntilTheCallerPersistsThenReleasesThem()
    {
        var engine = new Engine();
        var request = Request();
        var executor = new DockerSandbox(engine, Image, Installation, "fictional-engine");
        var first = await executor.ExecuteAsync(request, () => true, Token);
        Assert.Equal("completed", first.State);
        Assert.False(first.Release.Confirmed);
        Assert.NotNull(engine.Container);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("rm"));
        var recovered = await new DockerSandbox(engine, Image, Installation, "fictional-engine").RecoverAsync(
            request,
            Token
        );
        Assert.Equal(first.Outputs, recovered.Outputs);
        Assert.True(recovered.CreationConfirmed);
        Assert.Null(recovered.WallMilliseconds);
        Assert.Equal(1, engine.Starts);
        var saved = CompanyJson.Encode(recovered);
        Assert.True(
            (
                await executor.ReleaseAsync(request.CompanyId, request.JobId, recovered.CreationConfirmed, Token)
            ).Confirmed
        );
        Assert.Equal("Fictional saved output", Assert.Single(CompanyJson.Decode<SandboxResult>(saved).Outputs).Text);
        Assert.Null(engine.Container);
    }

    [Fact]
    public async Task UnapprovedOutputNamesCannotBecomeArtifacts()
    {
        var engine = new Engine { UnexpectedOutput = true };
        var result = await new DockerSandbox(engine, Image, Installation, "fictional-engine").ExecuteAndReleaseAsync(
            Request(),
            () => true,
            Token
        );
        Assert.Equal("unknown", result.State);
        Assert.Empty(result.Outputs);
        Assert.True(result.Release.Confirmed);
    }
}

internal static class SandboxTestFlow
{
    // The platform must store the result before cleanup. This adapter fixture represents that caller boundary;
    // actual database durability is tested with the product's separate composition, not invented here.
    internal static async Task<SandboxResult> ExecuteAndReleaseAsync(
        this ISandboxExecutor executor,
        SandboxRequest request,
        Func<bool> authorized,
        CancellationToken token
    )
    {
        var result = await executor.ExecuteAsync(request, authorized, token);
        var saved = CompanyJson.Decode<SandboxResult>(CompanyJson.Encode(result));
        if (
            saved.Release.Confirmed
            || saved.Reason is "already-attempted" or "existing-resource" or "resource-state-unconfirmed"
        )
            return saved;
        return saved with
        {
            Release = await executor.ReleaseAsync(
                request.CompanyId,
                request.JobId,
                saved.CreationConfirmed,
                CancellationToken.None
            ),
        };
    }
}
