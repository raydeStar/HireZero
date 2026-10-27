using HireZero.Core;

public sealed partial class CompanyModelConnectionTests
{
    private sealed class FictionalSubscription : IModelProvider, IMeasuredModelProvider
    {
        public string RuntimeIdentity => "fictional-subscription-runtime";
        public bool Qualified => false;
        public string Status => "Fictional subscription; no live provider.";
        public readonly List<string> Routes = [];

        public ModelInputAccounting CountInput(ModelCall call) => new(10, 0, "fictional-counter");

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new InvalidOperationException("The butler requires an exact review.");

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            Assert.Equal(RuntimeIdentity, job.RuntimeIdentity);
            Routes.Add(call.ModelRoute!);
            return Task.FromResult(
                new ModelReply("Fictional subscription draft.", 19, "fictional-receipt", new(12, 7))
            );
        }
    }

    private CompanyModelConnection Mixed(FictionalSubscription host, CompanyModelFixture api)
    {
        var connection = Connection(handler: api, platform: host);
        connection.Save(Input() with { Provider = "openrouter", Decision = "use-host-and-company-key" });
        return connection;
    }

    [Fact]
    public void MixedConnectionsKeepHostDefaultAndRequireUnambiguousExplicitSetup()
    {
        using var api = new CompanyModelFixture();
        var host = new FictionalSubscription();
        var connection = Mixed(host, api);
        Assert.Equal(new CompanyModelRouting().TextModel, connection.Routing.TextModel);
        Assert.Equal("credits", connection.AllowanceUnit);
        Assert.Contains(
            connection.Options,
            option => option.Route == Input().Route && option.AllowanceUnit == "usd-cents"
        );
        Assert.Contains(
            connection.Options,
            option => option.Route == connection.Routing.TextModel && option.AllowanceUnit == "credits"
        );
        Assert.False(connection.Qualified);
        Assert.Null(connection.Price(connection.Routing.TextModel));
        Assert.NotNull(connection.Price(Input().Route));
        Assert.Equal(connection.RuntimeIdentity, Connection(handler: api, platform: host).RuntimeIdentity);
        Assert.Throws<WorkspaceException>(() =>
            Connection("fictional-no-host").Save(Input() with { Decision = "use-host-and-company-key" })
        );
        Assert.Empty(api.Requests);
        Assert.Empty(host.Routes);
    }

    [Fact]
    public async Task ExplicitChoiceDispatchesOnceToItsAccountAndKeepsSeparateUsageTerms()
    {
        using var api = new CompanyModelFixture();
        var host = new FictionalSubscription();
        var connection = Mixed(host, api);
        var store = Store(connection);
        var native = Prepare(store, MeasuredQueueChecks.Seed(store));
        using var scheduler = new CompanyScheduler(store, connection);
        await scheduler.RunMeasuredOnceAsync(Run(native), connection, Token);
        Assert.Single(host.Routes);
        Assert.Empty(api.Requests);
        var apiTask = MeasuredQueueChecks.Seed(store);
        var selected = CompanyJson.Decode<CompanyAttempt>(
            store.Apply(
                C("measured.prepare") with
                {
                    Id = apiTask.Id,
                    ExpectedVersion = apiTask.Version,
                    CreditAllowance = 100,
                    ModelRoute = Input().Route,
                }
            )
        );
        Assert.Equal(new CompanyModelTerms("openrouter", "usd-cents"), selected.Measured!.Terms);
        Assert.Equal("credits", native.Measured!.Terms!.AllowanceUnit);
        await scheduler.RunMeasuredOnceAsync(Run(selected), connection, Token);
        Assert.Equal(("openrouter.ai", Input().Key), Assert.Single(api.Requests));
        Assert.Single(host.Routes);
        var receipts = store.Usage().Entries;
        Assert.Equal("host", receipts.Single(entry => entry.Id == native.Id).Funding!.Source);
        Assert.Null(receipts.Single(entry => entry.Id == native.Id).Price);
        Assert.Equal("company-key", receipts.Single(entry => entry.Id == selected.Id).Funding!.Source);
        Assert.Equal("openrouter", receipts.Single(entry => entry.Id == selected.Id).Funding!.Provider);
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(selected)));
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task FailedApiChoiceNeverFallsBackAndForgedProviderTermsAreRefused()
    {
        using var api = new CompanyModelFixture { Refuse = true };
        var host = new FictionalSubscription();
        var connection = Mixed(host, api);
        var (call, job) = Packet(connection);
        call = call with { ModelRoute = Input().Route };
        job = job with { Route = call.ModelRoute, Terms = new("host", "credits") };
        await Assert.ThrowsAsync<ModelTransportException>(() => connection.CompleteMeasuredAsync(call, job, Token));
        Assert.Empty(api.Requests);
        job = job with { Terms = connection.TermsFor(call.ModelRoute) };
        await Assert.ThrowsAsync<ModelTransportException>(() => connection.CompleteMeasuredAsync(call, job, Token));
        Assert.Single(api.Requests);
        Assert.Empty(host.Routes);
    }

    [Fact]
    public void RemovedApiChoiceExpiresReviewAndRequiresExplicitReturnToDefault()
    {
        using var api = new CompanyModelFixture();
        var host = new FictionalSubscription();
        var connection = Mixed(host, api);
        var store = Store(connection);
        var task = MeasuredQueueChecks.Seed(store);
        var old = CompanyJson.Decode<CompanyAttempt>(
            store.Apply(
                C("measured.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = task.Version,
                    CreditAllowance = 100,
                    ModelRoute = Input().Route,
                }
            )
        );
        connection.UseHost(new(1, "remove-key-and-use-host"));
        store.RefreshModelReviews();
        Assert.Equal("expired", store.Attempt(old.Id).Measured!.State);
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(old)));
        Assert.Throws<WorkspaceException>(() => Prepare(store, store.Work(task.Id)));
        var fresh = CompanyJson.Decode<CompanyAttempt>(
            store.Apply(
                C("measured.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = store.Work(task.Id).Version,
                    CreditAllowance = 1,
                    ModelRoute = "",
                }
            )
        );
        Assert.Equal(connection.Routing.TextModel, fresh.Measured!.Route);
        Assert.NotEqual(old.Measured!.Digest, fresh.Measured.Digest);
        Assert.Empty(api.Requests);
        Assert.Empty(host.Routes);
    }
}
