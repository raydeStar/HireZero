using System.Diagnostics;
using HireZero.Integrations.Codex;

public sealed class RuntimeReleaseTests : IDisposable
{
    private readonly TemporaryRoot _root = new("runtime-release-");

    private string Scratch() =>
        Directory.CreateDirectory(Path.Combine(_root.Path, "coding-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void FailedTerminationRetainsAllTemporaryFiles()
    {
        var scratch = Scratch();
        File.WriteAllText(Path.Combine(scratch, "retained.txt"), "fictional input");
        var result = CompanyRuntimeRelease.Complete(_root.Path, scratch, () => throw new TimeoutException());
        Assert.False(result.ProcessStopped);
        Assert.False(result.TemporaryFilesRemoved);
        Assert.Equal("termination-unconfirmed", result.Status);
        Assert.Equal(Path.GetFileName(scratch), result.ResourceId);
        Assert.True(File.Exists(Path.Combine(scratch, "retained.txt")));
    }

    [Fact]
    public void ConfirmedStopRemovesOnlyTheOwnedScratchPair()
    {
        var scratch = Scratch();
        var inputs = Directory.CreateDirectory(scratch + "-inputs").FullName;
        var other = Scratch();
        File.WriteAllText(Path.Combine(scratch, "temporary.txt"), "fictional runtime data");
        File.WriteAllText(Path.Combine(inputs, "input.json"), "{}");
        var stopped = false;
        var result = CompanyRuntimeRelease.Complete(
            _root.Path,
            scratch,
            () =>
            {
                Assert.True(Directory.Exists(scratch));
                Assert.True(Directory.Exists(inputs));
                stopped = true;
            }
        );
        Assert.True(stopped);
        Assert.Equal("released", result.Status);
        Assert.True(result.ProcessStopped && result.TemporaryFilesRemoved);
        Assert.False(Directory.Exists(scratch));
        Assert.False(Directory.Exists(inputs));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void ScopeMismatchNeverDeletesAnUnrelatedDirectory()
    {
        var scratch = Scratch();
        var wrong = Directory.CreateDirectory(Path.Combine(_root.Path, "another-root")).FullName;
        Assert.Equal("cleanup-scope-refused", CompanyRuntimeRelease.Complete(wrong, scratch, () => { }).Status);
        Assert.True(Directory.Exists(scratch));
        Assert.Equal("cleanup-scope-refused", CompanyRuntimeRelease.Complete(_root.Path, wrong, () => { }).Status);
        Assert.True(Directory.Exists(wrong));
        var file = Path.Combine(_root.Path, "coding-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "a file cannot masquerade as a removed directory");
        Assert.Equal("cleanup-scope-refused", CompanyRuntimeRelease.Complete(_root.Path, file, () => { }).Status);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task LinkedContentsAreRetainedWithoutTraversingTheTarget()
    {
        var scratch = Scratch();
        var target = Directory.CreateDirectory(Path.Combine(_root.Path, "kept-target")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "fictional independent file");
        var link = Path.Combine(scratch, "linked");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, process.ExitCode);
        }
        else
            Directory.CreateSymbolicLink(link, target);
        try
        {
            Assert.Equal(
                "cleanup-scope-refused",
                CompanyRuntimeRelease.Complete(_root.Path, scratch, () => { }).Status
            );
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
            Assert.True(Directory.Exists(scratch));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void AFileThatCannotBeRemovedProducesAnExplicitCleanupReceipt()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows sharing modes make this failure deterministic.
        var scratch = Scratch();
        var path = Path.Combine(scratch, "locked.txt");
        File.WriteAllText(path, "fictional locked temporary file");
        using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = CompanyRuntimeRelease.Complete(_root.Path, scratch, () => { });
        Assert.True(result.ProcessStopped);
        Assert.False(result.TemporaryFilesRemoved);
        Assert.Equal("cleanup-incomplete", result.Status);
        Assert.True(File.Exists(path));
    }

    public void Dispose() => _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
