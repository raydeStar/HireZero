// Each synthetic check group runs as one xUnit test; tests in one class run sequentially.
public sealed class IntegrationsSuite
{
    [Fact]
    public Task Inline() => IntegrationsInlineChecks.RunAsync();

    [Fact]
    public Task AzureDevOps() => AzureDevOpsChecks.RunAsync(Verify.Check);

    [Fact]
    public Task Steering() => SteeringChecks.RunAsync(Verify.Check);
}
