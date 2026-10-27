using HireZero.Core;
using HireZero.Integrations.Sandbox;
using Microsoft.Data.Sqlite;

public sealed class CompanyPremiumScriptTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("premium-scripts-");
    private readonly BillingFixtureProvider _provider = new() { SelectedPlan = BillingFixtureProvider.ExtendedPlan };
    private readonly SandboxFixtureExecutor _executor = new() { ExtendedEnabled = true };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    private CompanyStore Store(
        string company = CompanyStore.DefaultCompanyId,
        bool billing = true,
        BillingPlan? plan = null
    ) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            companyId: company
        )
        {
            SandboxRuntimeIdentity = _executor.Identity,
            SandboxExecutionAvailable = () => _executor.ExecutionEnabled,
            ExtendedSandboxAvailable = () => _executor.ExtendedScriptsAvailable,
            BillingConfiguration = billing ? new(_provider.Identity, [plan ?? _provider.SelectedPlan]) : null,
        };

    private async Task<CompanyBillingCheckout> Pay(CompanyStore store)
    {
        var checkout = A<CompanyBillingCheckout>(
            store,
            C("billing.prepare") with
            {
                Scope = _provider.SelectedPlan.Id,
            }
        );
        store.Apply(
            C("billing.checkout") with
            {
                Id = checkout.Id,
                ExpectedVersion = checkout.Version,
                Digest = checkout.Digest,
                Decision = "approve-subscription",
            }
        );
        var billing = new CompanyBillingWorkflow(store, _provider);
        Assert.True(await billing.DispatchAsync(checkout.Id, Token));
        _provider.Change(checkout.Id, "paid");
        Assert.True(await billing.RefreshAsync(checkout.Id, Token));
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = store.ComputeUsage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyComputeLimits(3, 1800, false)),
            }
        );
        return checkout;
    }

    private static CompanySandboxJob Prepare(
        CompanyStore store,
        CompanyTask task,
        int seconds = 180,
        string? tier = SandboxRequest.ExtendedTier
    ) =>
        A<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = store.Work(task.Id).Version,
                Body = CompanyJson.Encode(
                    new CompanySandboxInput(
                        "Fictional extended report",
                        "console.log('Fictional')",
                        [],
                        ["report.txt"],
                        new(seconds),
                        Tier: tier
                    )
                ),
            }
        );

    private static CompanyCommand Run(CompanySandboxJob job) =>
        C("sandbox.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };

    [Fact]
    public void OldRequestsAndPlanDigestsKeepTheirJsonAndStandardDurationLimit()
    {
        var request = new SandboxRequest(
            Guid.NewGuid().ToString("N"),
            "fictional",
            "console.log('Fictional')",
            [],
            [],
            new(120)
        );
        Assert.DoesNotContain("tier", CompanyJson.Encode(request));
        Assert.Equal(
            "{\"jobs\":1,\"tokens\":1000,\"computeJobs\":2,\"computeSeconds\":240}",
            CompanyJson.Encode(new BillingAllowance(1, 1000, 2, 240))
        );
        request.Validate();
        Assert.Throws<WorkspaceException>(() => (request with { Limits = new(121) }).Validate());
        (request with { Tier = SandboxRequest.ExtendedTier, Limits = new(1800) }).Validate();
        Assert.Throws<WorkspaceException>(() =>
            (request with { Tier = SandboxRequest.ExtendedTier, Limits = new(1801) }).Validate()
        );
        Assert.Throws<WorkspaceException>(() => (request with { Tier = "unbounded" }).Validate());
    }

    [Fact]
    public void PlanCannotAdvertiseAnUnboundedOrUnfundedExtendedEntitlement()
    {
        foreach (
            var allowance in new[]
            {
                new BillingAllowance(1, 100, 1, 1800, 1801),
                new(1, 100, 1, 100, 600),
                new(1, 100, 0, 1800, 600),
                new(1, 100, 1, 1800, 120),
            }
        )
            Assert.Throws<ArgumentException>(() =>
                new CompanyBillingConfiguration(
                    _provider.Identity,
                    [_provider.SelectedPlan with { Allowance = allowance }]
                )
            );
    }

    [Fact]
    public async Task PaidEntitlementAndQualifiedRuntimeAreBothRequiredAndCompanyScoped()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        Assert.Throws<WorkspaceException>(() => Prepare(store, task));
        await Pay(store);
        _executor.ExtendedEnabled = false;
        Assert.Throws<WorkspaceException>(() => Prepare(store, task));
        _executor.ExtendedEnabled = true;
        Assert.Throws<WorkspaceException>(() => Prepare(store, task, 601));
        var job = Prepare(store, task);
        Assert.Equal(600, job.Premium!.MaximumSeconds);
        Assert.Equal(0, store.ComputeUsage().Jobs);
        var second = Store("fictional-second", billing: false);
        Assert.Throws<WorkspaceException>(() => Prepare(second, MeasuredQueueChecks.Seed(second)));
        Assert.Null(second.ScriptTiers().ExtendedMaximumSeconds);
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public async Task ExtendedDurationUsesOneSharedReservationAndCannotBeReplayedAfterReload()
    {
        var store = Store();
        await Pay(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store));
        var approval = Run(job);
        store.Apply(approval);
        Assert.Equal(180, store.ComputeUsage().HeldSeconds);
        _executor.ReleaseFailures = 1;
        var runner = new CompanySandboxRunner(store, _executor);
        var result = await runner.RunAsync(job.Id, Token);
        Assert.False(result.Release!.Confirmed);
        Assert.Equal(180, Store().ComputeUsage().HeldSeconds);
        var reopened = Store();
        reopened.Apply(approval);
        await new CompanySandboxRunner(reopened, _executor).RunAsync(job.Id, Token);
        Assert.Equal(1, _executor.Executions);
        var recovered = await new CompanySandboxRunner(reopened, _executor).RecoverAsync(job.Id, Token);
        Assert.True(recovered.Release!.Confirmed);
        Assert.Equal(180, reopened.ComputeUsage().AllocatedSeconds);
        Assert.Equal(0, reopened.ComputeUsage().HeldSeconds);
        Assert.Equal(SandboxRequest.ExtendedTier, Assert.Single(reopened.ComputeSummaries()).Tier);
    }

    [Fact]
    public async Task UncertainExtendedWorkCannotClearItsHoldByChangingSubscriptionStatus()
    {
        var store = Store();
        var checkout = await Pay(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store), 600);
        store.Apply(Run(job));
        _executor.Mode = "unknown";
        _executor.ReleaseFailures = 5;
        await new CompanySandboxRunner(store, _executor).RunAsync(job.Id, Token);
        _provider.Change(checkout.Id, "past_due");
        await new CompanyBillingWorkflow(store, _provider).RefreshAsync(checkout.Id, Token);
        Assert.Equal(600, Store().ComputeUsage().HeldSeconds);
        Assert.Null(store.ScriptTiers().ExtendedMaximumSeconds);
        Assert.Throws<WorkspaceException>(() => Prepare(store, MeasuredQueueChecks.Seed(store)));
        Assert.Equal(1, _executor.Executions);
    }

    [Fact]
    public async Task ChangedPlanAndLostPaymentInvalidateUnsentApprovalWithoutReservingCompute()
    {
        var store = Store();
        var checkout = await Pay(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store));
        var changed = Store(plan: _provider.SelectedPlan with { Allowance = new(3, 42000, 3, 1800, 300) });
        Assert.Throws<WorkspaceException>(() => changed.Apply(Run(job)));
        _provider.Change(checkout.Id, "past_due");
        await new CompanyBillingWorkflow(store, _provider).RefreshAsync(checkout.Id, Token);
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(job)));
        Assert.Equal(0, store.ComputeUsage().Jobs);
        Assert.Equal(0, _executor.Executions);
    }

    [Fact]
    public async Task RuntimeWithdrawalDuringExecutionCancelsAndWithholdsItsOutput()
    {
        var store = Store();
        await Pay(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store));
        store.Apply(Run(job));
        _executor.Mode = "wait";
        var running = new CompanySandboxRunner(store, _executor).RunAsync(job.Id, Token);
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (_executor.Executions == 0 && DateTimeOffset.UtcNow < timeout)
            await Task.Delay(20, Token);
        Assert.Equal(1, _executor.Executions);
        _executor.ExtendedEnabled = false;
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal(180, store.ComputeUsage().AllocatedSeconds);
    }

    [Fact]
    public async Task LosingPaidEntitlementDuringExecutionWithholdsResultsWithoutLosingAccounting()
    {
        var store = Store();
        var checkout = await Pay(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store));
        store.Apply(Run(job));
        _executor.Mode = "wait";
        var running = new CompanySandboxRunner(store, _executor).RunAsync(job.Id, Token);
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (_executor.Executions == 0 && DateTimeOffset.UtcNow < timeout)
            await Task.Delay(20, Token);
        Assert.Equal(1, _executor.Executions);
        _provider.Change(checkout.Id, "past_due");
        await new CompanyBillingWorkflow(store, _provider).RefreshAsync(checkout.Id, Token);
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Result!.Outputs);
        Assert.True(result.Release!.Confirmed);
        Assert.Equal(180, Store().ComputeUsage().AllocatedSeconds);
    }

    [Fact]
    public async Task HostRechecksQualificationWhileAnExtendedJobIsStillRunning()
    {
        var store = Store();
        await Pay(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store));
        _executor.Mode = "wait";
        _executor.WaitTime = TimeSpan.FromMinutes(2);
        var probe = new WithdrawalProbe(_executor);
        using var service = new CompanySandboxService(
            () => [store],
            probe,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CompanySandboxService>.Instance
        );
        await service.StartAsync(Token);
        try
        {
            var initial = DateTimeOffset.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref probe.Refreshes) == 0 && DateTimeOffset.UtcNow < initial)
                await Task.Delay(20, Token);
            Assert.True(Volatile.Read(ref probe.Refreshes) > 0);
            store.Apply(Run(job));
            var dispatch = DateTimeOffset.UtcNow.AddSeconds(5);
            while (_executor.Executions == 0 && DateTimeOffset.UtcNow < dispatch)
                await Task.Delay(20, Token);
            Assert.Equal(1, _executor.Executions);
            probe.WithdrawOnRefresh = true;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
            while (store.SandboxJob(job.Id).SettledAt is null && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(50, Token);
            var result = store.SandboxJob(job.Id);
            Assert.True(Volatile.Read(ref probe.Refreshes) >= 2);
            Assert.Equal("withheld", result.State);
            Assert.Empty(result.Result!.Outputs);
            Assert.True(result.Release!.Confirmed);
            Assert.Equal(180, store.ComputeUsage().AllocatedSeconds);
            Assert.Equal(0, store.ComputeUsage().HeldSeconds);
            Assert.Equal(1, _executor.Executions);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StopAsync(stop.Token);
        }
    }

    private sealed class WithdrawalProbe(SandboxFixtureExecutor inner) : ISandboxExecutor, ISandboxRuntimeAvailability
    {
        public int Refreshes;
        public volatile bool WithdrawOnRefresh;
        public string Identity => inner.Identity;
        public SandboxAvailability Availability => inner.Availability;

        public Task RefreshAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Refreshes);
            if (WithdrawOnRefresh)
                inner.ExtendedEnabled = false;
            return Task.CompletedTask;
        }

        public Task<SandboxResult> ExecuteAsync(
            SandboxRequest request,
            Func<bool> stillAuthorized,
            CancellationToken token
        ) => inner.ExecuteAsync(request, stillAuthorized, token);

        public Task<SandboxResult> RecoverAsync(SandboxRequest request, CancellationToken token) =>
            inner.RecoverAsync(request, token);

        public Task<SandboxRelease> ReleaseAsync(
            string companyId,
            string jobId,
            bool created,
            CancellationToken token
        ) => inner.ReleaseAsync(companyId, jobId, created, token);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
