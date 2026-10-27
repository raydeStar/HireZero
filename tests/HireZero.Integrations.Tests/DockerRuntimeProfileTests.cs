using System.Text.Json;
using HireZero.Integrations.Sandbox;

public sealed class DockerRuntimeProfileTests
{
    private sealed class Metadata(object registration) : IDockerCommands
    {
        public Task<DockerReply> RunAsync(
            string[] arguments,
            string? input,
            TimeSpan timeout,
            int outputLimit,
            CancellationToken token
        ) =>
            Task.FromResult(
                new DockerReply(
                    0,
                    JsonSerializer.Serialize(
                        new
                        {
                            ID = "fictional-engine",
                            OSType = "linux",
                            ServerVersion = "fictional-version",
                            KernelVersion = "fictional-kernel",
                            Architecture = "amd64",
                            Runtimes = new Dictionary<string, object> { ["runsc"] = registration },
                            MemoryLimit = true,
                            SwapLimit = true,
                            PidsLimit = true,
                            CpuCfsQuota = true,
                        }
                    )
                )
            );
    }

    private static object Registration(
        string[] capabilities,
        string[]? arguments = null,
        string path = "/fictional/runsc",
        bool seccomp = true
    ) =>
        new
        {
            path,
            runtimeArgs = arguments ?? ["--platform=systrap", "--systemd-cgroup"],
            status = new Dictionary<string, string>
            {
                ["org.opencontainers.runtime-spec.features"] = JsonSerializer.Serialize(
                    new { linux = new { capabilities, seccomp = new { enabled = seccomp } } }
                ),
            },
        };

    [Fact]
    public async Task ReorderedRuntimeCapabilitiesKeepTheSameQualificationBinding()
    {
        var before = await DockerSandbox.InspectEngineAsync(
            new Metadata(Registration(["CAP_SETUID", "CAP_KILL", "CAP_SETGID"])),
            TestContext.Current.CancellationToken
        );
        var after = await DockerSandbox.InspectEngineAsync(
            new Metadata(Registration(["CAP_SETGID", "CAP_SETUID", "CAP_KILL"])),
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ChangedCapabilitiesArgumentsPathsAndFeatureValuesStillInvalidateTheBinding()
    {
        string[] capabilities = ["CAP_SETUID", "CAP_SETGID"];
        var baseline = await DockerSandbox.InspectEngineAsync(
            new Metadata(Registration(capabilities)),
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(baseline);
        foreach (
            var changed in new[]
            {
                Registration(["CAP_SETUID"]),
                Registration(["CAP_SETUID", "CAP_SETGID", "CAP_SYS_ADMIN"]),
                Registration(["CAP_SETUID", "CAP_SETGID", "CAP_SETGID"]),
                Registration(capabilities, ["--systemd-cgroup", "--platform=systrap"]),
                Registration(capabilities, path: "/fictional/replacement-runsc"),
                Registration(capabilities, seccomp: false),
            }
        )
        {
            var profile = await DockerSandbox.InspectEngineAsync(
                new Metadata(changed),
                TestContext.Current.CancellationToken
            );
            Assert.NotNull(profile);
            Assert.NotEqual(baseline, profile);
        }
    }
}
