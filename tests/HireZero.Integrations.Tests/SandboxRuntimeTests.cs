using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Sandbox;

// Explicit operator qualification only. Routine test runs cannot start an engine, pull an image or run a container.
public sealed class SandboxRuntimeTests
{
    [Fact]
    public async Task ThePinnedRuntimeEnforcesItsSupervisorAndReleasesRealResources()
    {
        var image = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_IMAGE");
        Assert.SkipWhen(
            string.IsNullOrEmpty(image),
            "Set HIREZERO_SANDBOX_TEST_IMAGE only for an authorized disposable runtime qualification."
        );
        var executable = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_DOCKER");
        var endpoint = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_ENDPOINT");
        var installation = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_INSTALLATION");
        var receiptPath = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_RECEIPT");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        Assert.True(Guid.TryParseExact(installation, "N", out _), "Use the deployment's durable installation GUID.");
        Assert.True(
            receiptPath is not null && Path.IsPathFullyQualified(receiptPath),
            "Choose an absolute private qualification receipt path."
        );
        Assert.False(
            File.Exists(receiptPath),
            "Choose a new receipt path; qualification never overwrites earlier evidence."
        );
        var token = TestContext.Current.CancellationToken;
        var commands = new DockerCommands(executable, endpoint);
        var profile = await DockerSandbox.InspectEngineAsync(commands, token);
        Assert.NotNull(profile);
        var executableHash = SandboxQualification.ExecutableHash(executable);
        var resources = new Dictionary<string, (SandboxRequest Request, bool Created)>();
        var failures = new List<string>();
        using var observations = new StreamWriter(
            new FileStream(receiptPath + ".observations.jsonl", FileMode.CreateNew, FileAccess.Write, FileShare.Read)
        )
        {
            AutoFlush = true,
        };
        var cgroup = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_CGROUP_ROOT");
        IDockerCommands observedCommands = string.IsNullOrEmpty(cgroup)
            ? commands
            : new SandboxResourceObserver(commands, cgroup, observations.WriteLine);
        var runtime = new DockerSandbox(observedCommands, image, installation!, profile.Id);
        var extended = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_EXTENDED") == "1";
        async Task<SandboxResult> Run(SandboxRequest request)
        {
            resources.Add(request.JobId, (request, false));
            var observed = await runtime.ExecuteAsync(request, () => true, token);
            resources[request.JobId] = (request, observed.CreationConfirmed);
            observations.WriteLine(
                CompanyJson.Encode(
                    new
                    {
                        request.JobId,
                        request.Tier,
                        request.Limits.Seconds,
                        observed.State,
                        observed.Reason,
                        observed.WallMilliseconds,
                    }
                )
            );
            return observed;
        }
        async Task Check(string name, Func<Task> check)
        {
            observations.WriteLine(CompanyJson.Encode(new { check = name, phase = "started" }));
            try
            {
                await check();
                observations.WriteLine(CompanyJson.Encode(new { check = name, phase = "passed" }));
            }
            catch (Xunit.Sdk.XunitException error)
            {
                failures.Add(name + ": " + error.Message);
                observations.WriteLine(CompanyJson.Encode(new { check = name, phase = "failed" }));
            }
            finally
            {
                // A failed experiment closes this candidate; first tidy up, then report it without starting another.
                foreach (var resource in resources.Values.ToArray())
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    var released = await runtime.ReleaseAsync(
                        resource.Request.CompanyId,
                        resource.Request.JobId,
                        resource.Created,
                        cleanup.Token
                    );
                    Assert.True(released.Confirmed, "Qualification resource release was not confirmed.");
                    resources.Remove(resource.Request.JobId);
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        try
        {
            Assert.True(
                await runtime.AvailableAsync(token),
                "The explicit image and Linux runsc engine must qualify; no fallback is permitted."
            );
            var scope = new SandboxRequest(
                Guid.NewGuid().ToString("N"),
                "co-runtime-fixture",
                """
                import assert from 'node:assert/strict';
                import fs from 'node:fs';
                assert.equal(process.getuid(), 65534);
                assert.equal(process.getgid(), 65534);
                assert.deepEqual(process.execArgv, ['--v8-pool-size=1']);
                assert.equal(process.env.UV_THREADPOOL_SIZE, '1');
                assert.throws(() => process.kill(1, 0), e => e.code === 'EPERM');
                assert.throws(() => fs.writeFileSync('/opt/hirezero/runner.mjs', 'changed'), e => ['EACCES','EROFS'].includes(e.code));
                assert.throws(() => fs.writeFileSync('input.txt', 'changed'), e => e.code === 'EACCES');
                assert.equal(fs.readFileSync('input.txt', 'utf8'), 'Fictional approved input');
                fs.writeFileSync('result.txt', JSON.stringify({uid:process.getuid(),input:'read',supervisor:'protected'}));
                console.log('\0'.repeat(5000));
                """,
                [new("input.txt", "Fictional approved input")],
                ["result.txt"],
                new(5)
            );
            await Check(
                "workload-task-limit",
                async () =>
                {
                    var observed = await Run(
                        scope with
                        {
                            JobId = Guid.NewGuid().ToString("N"),
                            Script = SandboxTaskLimitFixture.Script,
                            Inputs = [],
                            Limits = new(15),
                        }
                    );
                    Assert.True(
                        observed.State == "completed",
                        "Workload task-limit result: " + CompanyJson.Encode(observed)
                    );
                    using var proof = JsonDocument.Parse(Assert.Single(observed.Outputs).Text);
                    Assert.Equal(65534, proof.RootElement.GetProperty("uid").GetInt32());
                    Assert.Equal(32, proof.RootElement.GetProperty("workloadTasks").GetInt32());
                    Assert.Equal("EAGAIN", proof.RootElement.GetProperty("processError").GetString());
                    Assert.True(proof.RootElement.GetProperty("threadRefused").GetBoolean());
                    Assert.InRange(proof.RootElement.GetProperty("childrenReaped").GetInt32(), 1, 39);
                }
            );
            // A separately approved diagnostic stops here and can never issue a qualification receipt.
            if (Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_TASK_DIAGNOSTIC_ONLY") == "1")
                return;
            await Check(
                "protected-output-and-recovery",
                async () =>
                {
                    var observed = await Run(scope);
                    Assert.True(
                        observed.State == "completed",
                        "Fictional script result: " + CompanyJson.Encode(observed)
                    );
                    Assert.False(observed.Release.Confirmed);
                    var recovered = await runtime.RecoverAsync(scope, token);
                    Assert.Equal(observed.Stdout, recovered.Stdout);
                    Assert.Equal(observed.Outputs, recovered.Outputs);
                    var saved = CompanyJson.Encode(recovered);
                    var result = CompanyJson.Decode<SandboxResult>(saved) with
                    {
                        Release = await runtime.ReleaseAsync(
                            scope.CompanyId,
                            scope.JobId,
                            recovered.CreationConfirmed,
                            token
                        ),
                    };
                    Assert.True(result.Release.Confirmed, result.Release.Status);
                    Assert.Equal("completed", result.State);
                    Assert.Equal(
                        65534,
                        JsonDocument.Parse(Assert.Single(result.Outputs).Text).RootElement.GetProperty("uid").GetInt32()
                    );
                }
            );
            await Check(
                "standard-deadline",
                async () =>
                {
                    var timeout = await Run(
                        scope with
                        {
                            JobId = Guid.NewGuid().ToString("N"),
                            Script = "while(true) {}",
                            Outputs = [],
                            Limits = new(2),
                        }
                    );
                    Assert.Equal("timed-out", timeout.State);
                }
            );
            await Check(
                "linked-output-refused",
                async () =>
                {
                    var linked = await Run(
                        scope with
                        {
                            JobId = Guid.NewGuid().ToString("N"),
                            Script = "import fs from 'node:fs'; fs.symlinkSync('/etc/passwd','result.txt');",
                        }
                    );
                    Assert.Equal("failed", linked.State);
                    Assert.Empty(linked.Outputs);
                }
            );
            if (extended)
            {
                // Explicit extended qualification takes over four minutes. Routine checks never enter this room.
                var longRequest = scope with
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    Tier = SandboxRequest.ExtendedTier,
                    Limits = new(140),
                    Script =
                        "import fs from 'node:fs'; await new Promise(r => setTimeout(r, 125000)); fs.writeFileSync('result.txt','Fictional extended result');",
                };
                await Check(
                    "extended-duration-and-recovery",
                    async () =>
                    {
                        var longResult = await Run(longRequest);
                        Assert.Equal("completed", longResult.State);
                        Assert.Equal("Fictional extended result", Assert.Single(longResult.Outputs).Text);
                        var recoveredLong = await runtime.RecoverAsync(longRequest, token);
                        Assert.Equal(longResult.Outputs, recoveredLong.Outputs);
                    }
                );
                await Check(
                    "extended-deadline",
                    async () =>
                    {
                        var deadlineResult = await Run(
                            longRequest with
                            {
                                JobId = Guid.NewGuid().ToString("N"),
                                Limits = new(121),
                                Script = "while(true) {}",
                                Outputs = [],
                            }
                        );
                        Assert.Equal("timed-out", deadlineResult.State);
                    }
                );
            }
        }
        finally
        {
            // A failed assertion is still an obligation to tidy up, not permission to abandon a container.
            var released = new List<bool>();
            foreach (var resource in resources.Values)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                released.Add(
                    (
                        await runtime.ReleaseAsync(
                            resource.Request.CompanyId,
                            resource.Request.JobId,
                            resource.Created,
                            cleanup.Token
                        )
                    ).Confirmed
                );
            }
            Assert.All(
                released,
                confirmed => Assert.True(confirmed, "Qualification resource release was not confirmed.")
            );
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(profile, await DockerSandbox.InspectEngineAsync(commands, token));
        Assert.Equal(executableHash, SandboxQualification.ExecutableHash(executable));
        var now = DateTimeOffset.UtcNow;
        var qualification = new SandboxQualification(
            SandboxQualification.CurrentProtocol,
            image.ToLowerInvariant(),
            installation!.ToLowerInvariant(),
            endpoint,
            executableHash,
            profile,
            now,
            now.AddDays(7),
            SandboxQualification.RequiredChecks,
            extended ? SandboxQualification.RequiredExtendedChecks : null
        );
        using var output = new FileStream(receiptPath!, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = System.Text.Encoding.UTF8.GetBytes(CompanyJson.Encode(qualification));
        await output.WriteAsync(bytes, token);
        output.Flush(flushToDisk: true);
    }
}
