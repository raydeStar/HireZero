// Each synthetic check group runs as one xUnit test; tests in one class run sequentially.
public sealed class PolicySuite
{
    [Fact]
    public void Inline() => PolicyInlineChecks.Run();
}
