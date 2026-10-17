// Bridges the synthetic check groups, which report through Action<bool, string>, onto xUnit assertions.
public static class Verify
{
    public static void Check(bool condition, string message) => Xunit.Assert.True(condition, message);
}
