using HireZero.Integrations.Sandbox;

// Operator-only, read-only validation of an existing receipt. This never qualifies or starts a job.
public sealed class BrowserQualificationBindingTests
{
    [Fact]
    public async Task SavedQualificationMatchesTheCurrentEngineAndKeepsDisabledExecutionClosed()
    {
        var path = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_CHECK_RECEIPT");
        Assert.SkipWhen(
            string.IsNullOrEmpty(path),
            "Set HIREZERO_BROWSER_CHECK_RECEIPT for an operator receipt check."
        );
        var executable = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_DOCKER");
        var endpoint = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_ENDPOINT");
        var image = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_IMAGE");
        var installation = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_INSTALLATION");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        Assert.False(string.IsNullOrWhiteSpace(image));
        Assert.False(string.IsNullOrWhiteSpace(installation));
        var receipt = BrowserQualification.Read(path!);
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
        var commands = new DockerCommands(executable!, endpoint!);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(receipt.Engine, await DockerSandbox.InspectEngineAsync(commands, token));
        foreach (var enabled in new[] { false, true })
        {
            var runtime = new QualifiedDockerBrowser(
                commands,
                receipt,
                () => BrowserQualification.Read(path!),
                () => SandboxQualification.ExecutableHash(executable!),
                enabled,
                (_, _) =>
                    throw new InvalidOperationException("This inspection has no itinerary; the coach stays parked.")
            );
            await runtime.RefreshAsync(token);
            Assert.Equal(enabled, runtime.Availability.Available);
            Assert.Equal(enabled ? "ready" : "operator-disabled", runtime.Availability.Status);
        }
    }
}
