using HireZero.Core;
using HireZero.Integrations.Sandbox;
using Microsoft.Extensions.Logging.Abstractions;

public sealed partial class CompanyBrowserHostingTests
{
    [Fact]
    public async Task PrivateBrowserBindingRetainsRecoveryButCannotEnableWorkFromAScriptReceipt()
    {
        var cli = Path.Combine(_root.Path, "fictional-cli");
        var path = Path.Combine(_root.Path, "qualification.json");
        File.WriteAllText(cli, "Fictional test file. Never executed.");
        var image = "sha256:" + new string('a', 64);
        var installation = new string('1', 32);
        var values = new Dictionary<string, string>
        {
            ["HIREZERO_BROWSER_DOCKER"] = cli,
            ["HIREZERO_BROWSER_ENDPOINT"] = "unix:///var/run/docker.sock",
            ["HIREZERO_BROWSER_IMAGE"] = image,
            ["HIREZERO_BROWSER_INSTALLATION"] = installation,
            ["HIREZERO_BROWSER_QUALIFICATION"] = path,
        };
        var settings = CompanySettings.Load(name => values.GetValueOrDefault(name));
        Assert.Throws<InvalidOperationException>(() => CompanyBrowserRuntime.Create(settings));
        var now = DateTimeOffset.UtcNow;
        var receipt = new BrowserQualification(
            BrowserQualification.CurrentProtocol,
            image,
            installation,
            values["HIREZERO_BROWSER_ENDPOINT"],
            SandboxQualification.ExecutableHash(cli),
            new("fictional-engine", "fictional-version", "fictional-kernel", "amd64", new string('b', 64)),
            now,
            now.AddDays(7),
            BrowserQualification.RequiredChecks
        );
        File.WriteAllText(path, CompanyJson.Encode(receipt));
        var runtime = CompanyBrowserRuntime.Create(settings)!;
        await runtime.RefreshAsync(Token);
        Assert.Equal("operator-disabled", runtime.Availability.Status);
        Assert.False(runtime.Availability.Available);
        Assert.NotEmpty(runtime.Identity);
        var publicStatus = CompanyJson.Encode(CompanyBrowserRuntime.Status(runtime));
        Assert.DoesNotContain(cli, publicStatus);
        Assert.DoesNotContain(receipt.Engine.Id, publicStatus);
        File.WriteAllText(path, CompanyJson.Encode(receipt with { Protocol = SandboxQualification.CurrentProtocol }));
        Assert.Throws<InvalidOperationException>(() => CompanyBrowserRuntime.Create(settings));
        File.WriteAllText(path, new string('x', 20_000));
        Assert.Null(BrowserQualification.Read(path));
        File.WriteAllText(path, CompanyJson.Encode(receipt with { Image = "sha256:" + new string('d', 64) }));
        Assert.Throws<InvalidOperationException>(() => CompanyBrowserRuntime.Create(settings));
    }

    private sealed class MonitoringBrowser : IBrowserExecutor, IBrowserRuntimeAvailability
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _available = true;
        public int Executions;
        public string Identity => Runtime;
        public BrowserAvailability Availability => new(_available, _available ? "ready" : "qualification-required");

        public async Task RefreshAsync(CancellationToken token)
        {
            await _started.Task.WaitAsync(token);
            _available = false;
        }

        public async Task<BrowserExecution> ExecuteAsync(
            BrowserRequest request,
            Func<bool> allowed,
            CancellationToken token
        )
        {
            Interlocked.Increment(ref Executions);
            _started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return new(
                "canceled",
                new(request.JobId, "canceled", 0, true, [], [], [], true, new(0, 0), 1),
                new(0, 0, 0),
                1,
                new(false, "awaiting-release", "fictional-" + request.JobId),
                CreationConfirmed: true,
                BrowserExited: true
            );
        }

        public Task<BrowserExecution> RecoverAsync(BrowserRequest request, CancellationToken token) =>
            throw new InvalidOperationException("This fictional job must not be replayed.");

        public Task<SandboxRelease> ReleaseAsync(
            BrowserRequest request,
            string resource,
            bool created,
            CancellationToken token
        ) => Task.FromResult(new SandboxRelease(true, "released", resource));
    }

    [Fact]
    public async Task BrowserQualificationMonitoringRunsIndependentlyAndWithdrawsInFlightAuthority()
    {
        var executor = new MonitoringBrowser();
        var store = Store(browserAvailable: () => executor.Availability.Available);
        Enable(store);
        var job = Apply<CompanyBrowserJob>(store, Prepare(MeasuredQueueChecks.Seed(store)));
        using var service = new CompanyBrowserService(
            () => [store],
            executor,
            NullLogger<CompanyBrowserService>.Instance
        );
        store.Apply(Run(job));
        await service.StartAsync(Token);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (store.BrowserJob(job.Id).SettledAt is null && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(30, Token);
            var result = store.BrowserJob(job.Id);
            Assert.Equal(1, executor.Executions);
            Assert.False(executor.Availability.Available);
            Assert.Equal("canceled", result.State);
            Assert.NotNull(result.SettledAt);
            Assert.True(result.Release!.Confirmed);
            Assert.True(result.Execution!.Result is null || result.Execution.Result.Captures.Length == 0);
        }
        finally
        {
            await service.StopAsync(Token);
        }
    }
}
