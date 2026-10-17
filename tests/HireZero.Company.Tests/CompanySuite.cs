// Each synthetic check group runs as one xUnit test in its own disposable temporary root.
public sealed class CompanySuite
{
    static async Task InRoot(Func<string, Task> run)
    {
        await using var root = new TemporaryRoot("company-checks-");
        await run(root.Path);
    }

    [Fact] public Task Inline() => InRoot(CompanyInlineChecks.RunAsync);
    [Fact] public Task Security() => InRoot(root => SecurityChecks.RunAsync(root, Verify.Check));
    [Fact] public Task MarketingQueue() => InRoot(root => MarketingQueueChecks.RunAsync(root, Verify.Check));
    [Fact] public Task IntegrationPath() => InRoot(root => IntegrationPathChecks.RunAsync(root, Verify.Check));
    [Fact] public Task CompanyCodex() => InRoot(root => CompanyCodexChecks.RunAsync(root, Verify.Check));
    [Fact] public Task MeasuredQueue() => InRoot(root => MeasuredQueueChecks.RunAsync(root, Verify.Check));
    [Fact] public Task TaskHandoff() => InRoot(root => TaskHandoffChecks.RunAsync(root, Verify.Check));
    [Fact] public Task Conversation() => InRoot(root => ConversationChecks.RunAsync(root, Verify.Check));
    [Fact] public Task Http() => InRoot(root => CompanyInlineChecks.HttpChecks(root, Verify.Check));
}

// A test-owned directory under the system temp folder, removed afterwards. Git's object files can be read-only on
// Windows, so only this fixture's files are made writable before removal.
public sealed class TemporaryRoot : IAsyncDisposable
{
    private readonly string _prefix;
    public string Path { get; }

    public TemporaryRoot(string prefix)
    {
        _prefix = prefix;
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public ValueTask DisposeAsync()
    {
        var full = System.IO.Path.GetFullPath(Path);
        var parent = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        if (System.IO.Path.GetDirectoryName(full) != parent || !System.IO.Path.GetFileName(full).StartsWith(_prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refused cleanup outside this test's temporary root.");
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, true);
        return ValueTask.CompletedTask;
    }
}
