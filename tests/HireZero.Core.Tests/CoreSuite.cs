// Core's shared building blocks, run as inline checks.
public sealed class CoreSuite
{
    [Fact]
    public Task ContentGuard() => ContentGuardChecks.RunAsync(Verify.Check);

    [Fact]
    public Task CommandText() => CommandTextChecks.RunAsync(Verify.Check);
}
