using HireZero.Integrations.Sandbox;

// Read-only operator check: the receipt is inspected, never renewed by wishing or by running another job.
public sealed class SandboxQualificationBindingTests
{
    [Fact]
    public async Task SavedScriptQualificationMatchesTheEngineAndKeepsDisabledExecutionClosed()
    {
        var path = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_CHECK_RECEIPT");
        Assert.SkipWhen(
            string.IsNullOrEmpty(path),
            "Set HIREZERO_SANDBOX_CHECK_RECEIPT for an operator receipt check."
        );
        var executable = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_DOCKER");
        var endpoint = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_ENDPOINT");
        var image = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_IMAGE");
        var installation = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_INSTALLATION");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        Assert.False(string.IsNullOrWhiteSpace(image));
        Assert.False(string.IsNullOrWhiteSpace(installation));
        var receipt = SandboxQualification.Read(path!);
        Assert.NotNull(receipt);
        Assert.True(
            receipt.Matches(
                image!,
                installation!,
                endpoint!,
                SandboxQualification.ExecutableHash(executable!),
                DateTimeOffset.UtcNow
            )
        );
        var extended = Environment.GetEnvironmentVariable("HIREZERO_SANDBOX_TEST_EXTENDED") == "1";
        Assert.Equal(extended, receipt.ExtendedQualified);
        var commands = new DockerCommands(executable!, endpoint!);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(receipt.Engine, await DockerSandbox.InspectEngineAsync(commands, token));
        foreach (var enabled in new[] { false, true })
        {
            var runtime = new QualifiedDockerSandbox(
                commands,
                receipt,
                () => SandboxQualification.Read(path!),
                () => SandboxQualification.ExecutableHash(executable!),
                enabled
            );
            await runtime.RefreshAsync(token);
            Assert.Equal(enabled, runtime.Availability.Available);
            Assert.Equal(enabled ? "ready" : "operator-disabled", runtime.Availability.Status);
            Assert.Equal(enabled && extended, runtime.ExtendedScriptsAvailable);
        }
    }
}
