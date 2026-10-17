// Each synthetic check group runs as one xUnit test; tests in one class run sequentially.
public sealed class HostSuite
{
    [Fact]
    public Task Inline() => HostInlineChecks.RunAsync();

    [Fact]
    public Task Discovery() => DiscoveryChecks.RunAsync();

    [Fact]
    public Task Recovery() => RecoveryChecks.RunAsync();
}
