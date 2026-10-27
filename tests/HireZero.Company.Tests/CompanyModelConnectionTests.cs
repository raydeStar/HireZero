using System.Text;
using HireZero.Core;
using HireZero.Integrations.ModelApis;
using Microsoft.AspNetCore.DataProtection;

public sealed partial class CompanyModelConnectionTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-model-");
    private readonly EphemeralDataProtectionProvider _protection = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static CompanyModelConnectionInput Input(int version = 0, string key = "fictional-company-key") =>
        new(version, "openai", "openai/fictional-luna", key, "use-company-key");

    private CompanyModelConnection Connection(
        string company = CompanyStore.DefaultCompanyId,
        CompanyModelFixture? handler = null,
        IModelProvider? platform = null
    )
    {
        var connection = new CompanyModelConnection(
            company,
            platform ?? new UnavailableModelProvider(),
            new(),
            CompanyModelFixture.Models,
            handler
        );
        connection.Initialize(_root.Path, _protection);
        return connection;
    }

    private CompanyStore Store(CompanyModelConnection connection) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: connection.MeasuredIdentity
        )
        {
            CurrentModelRouting = () => connection.Routing,
            CurrentMeasuredRuntimeIdentity = () => connection.MeasuredIdentity,
            UsageFunding = connection.FundingFor,
            UsageRouteFunding = (kind, route) => connection.FundingFor(kind, route),
            MeasuredTerms = connection.TermsFor,
            UsagePrice = connection.Price,
        };

    private static CompanyAttempt Prepare(CompanyStore store, CompanyTask task) =>
        CompanyJson.Decode<CompanyAttempt>(
            store.Apply(
                C("measured.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = store.Work(task.Id).Version,
                    CreditAllowance = 100,
                }
            )
        );

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    private static (ModelCall Call, CompanyMeasuredJob Job) Packet(CompanyModelConnection connection)
    {
        var context = new CompanyContext(
            "ctx",
            "emp",
            "marketing",
            "thread",
            "Fictional work",
            "Fictional facts",
            "",
            "",
            [],
            [],
            "Draft a fictional note."
        );
        var call = new ModelCall(
            "attempt",
            context,
            "Fictional instructions",
            4000,
            1000,
            connection.Routing.TextModel
        );
        return (
            call,
            new(
                "dispatched",
                "fictional-digest",
                CompanyJson.Digest(context),
                ContentGuard.Digest(call.Instructions),
                call.ModelRoute!,
                connection.RuntimeIdentity,
                100,
                DateTimeOffset.UtcNow.AddMinutes(10),
                Encoding.UTF8.GetByteCount(CompanyJson.Encode(context)) + Encoding.UTF8.GetByteCount(call.Instructions),
                1,
                1,
                CompanyMeasuredPolicy.Current
            )
        );
    }

    [Fact]
    public void SavedKeyIsEncryptedAbsentFromStatusAndReloadsWithStableAuthority()
    {
        var connection = Connection();
        var status = connection.Save(Input());
        Assert.True(status.KeyStored);
        Assert.Contains("unverified", status.Status);
        Assert.DoesNotContain(Input().Key, CompanyJson.Encode(status));
        var ciphertext = File.ReadAllText(
            Assert.Single(Directory.GetFiles(_root.Path, "model-connection.secret", SearchOption.AllDirectories))
        );
        Assert.DoesNotContain(Input().Key, ciphertext);
        Assert.Equal(connection.RuntimeIdentity, Connection().RuntimeIdentity);
        Assert.Equal("company-key", Connection().Funding.Source);
    }

    [Fact]
    public void CopiedCiphertextCannotBeOpenedByAnotherCompanyAndNeverFallsBack()
    {
        Connection().Save(Input());
        var source = Assert.Single(
            Directory.GetFiles(_root.Path, "model-connection.secret", SearchOption.AllDirectories)
        );
        var second = Connection("fictional-second");
        second.Save(Input(key: "fictional-second-key"));
        var destination = Directory
            .GetFiles(_root.Path, "model-connection.secret", SearchOption.AllDirectories)
            .Single(p => p != source);
        File.Copy(source, destination, true);
        var unreadable = Connection("fictional-second");
        Assert.False(unreadable.HasMeasured);
        Assert.False(unreadable.Qualified);
        Assert.Contains("cannot be opened", unreadable.Status);
        unreadable.UseHost(new(0, "remove-key-and-use-host"));
        Assert.Equal("host", unreadable.Describe().Mode);
    }

    [Fact]
    public void ConsentVersionAndCompatibleReviewedPricesAreRequiredBeforeSaving()
    {
        var connection = Connection();
        foreach (
            var input in new[]
            {
                Input() with
                {
                    Decision = "",
                },
                Input() with
                {
                    Provider = "anthropic",
                },
                Input() with
                {
                    Route = "openai/unpriced",
                },
                Input() with
                {
                    Key = "white space secret",
                },
                Input() with
                {
                    Key = "short",
                },
            }
        )
            Assert.Throws<WorkspaceException>(() => connection.Save(input));
        connection.Save(Input());
        Assert.Throws<WorkspaceException>(() => connection.Save(Input()));
        Assert.Throws<WorkspaceException>(() => connection.UseHost(new(1, "")));
        Assert.Equal(1, connection.Describe().Version);
    }

    [Fact]
    public async Task OnlyTheSelectedCompanyKeyIsSentAndAnotherCompanyPacketIsRefused()
    {
        using var fake = new CompanyModelFixture();
        using var platform = new ApiModelProvider(_ => "fictional-host-key", fake, _ => new(3, 15));
        var first = Connection(handler: fake, platform: platform);
        first.Save(Input());
        var second = Connection("fictional-second", fake, platform);
        second.Save(Input(key: "fictional-second-key"));
        var (call, job) = Packet(first);
        await Assert.ThrowsAsync<ModelTransportException>(() => second.CompleteMeasuredAsync(call, job, Token));
        await Assert.ThrowsAsync<ModelUnavailableException>(() => first.CompleteAsync(call, Token));
        Assert.Empty(fake.Requests);
        await first.CompleteMeasuredAsync(call, job, Token);
        Assert.Equal(("api.openai.com", Input().Key), Assert.Single(fake.Requests));
        var (nextCall, nextJob) = Packet(second);
        await second.CompleteMeasuredAsync(nextCall, nextJob, Token);
        Assert.Equal("fictional-second-key", fake.Requests[1].Key);
    }

    [Fact]
    public async Task BadCompanyKeyDoesNotRetryWithHostCredentials()
    {
        using var fake = new CompanyModelFixture { Refuse = true };
        using var platform = new ApiModelProvider(_ => "fictional-host-key", fake, _ => new(3, 15));
        var connection = Connection(handler: fake, platform: platform);
        connection.Save(Input());
        var (call, job) = Packet(connection);
        await Assert.ThrowsAsync<ModelTransportException>(() => connection.CompleteMeasuredAsync(call, job, Token));
        Assert.Equal(Input().Key, Assert.Single(fake.Requests).Key);
    }

    [Fact]
    public async Task ReplacementAndReturnToHostNeverResurrectAnEarlierApproval()
    {
        using var fake = new CompanyModelFixture();
        using var platform = new ApiModelProvider(_ => "fictional-host-key", fake, _ => new(3, 15));
        var connection = Connection(handler: fake, platform: platform);
        var host = Packet(connection);
        connection.Save(Input());
        var company = Packet(connection);
        connection.Save(Input(1, "fictional-replacement"));
        await Assert.ThrowsAsync<ModelTransportException>(() =>
            connection.CompleteMeasuredAsync(company.Call, company.Job, Token)
        );
        connection.UseHost(new(2, "remove-key-and-use-host"));
        await Assert.ThrowsAsync<ModelTransportException>(() =>
            connection.CompleteMeasuredAsync(host.Call, host.Job, Token)
        );
        var fresh = Packet(connection);
        await connection.CompleteMeasuredAsync(fresh.Call, fresh.Job, Token);
        Assert.Equal("fictional-host-key", Assert.Single(fake.Requests).Key);
        Assert.False(Connection().Describe().KeyStored);
    }

    [Fact]
    public void ConnectionChangeExpiresUnsentWorkAndFreshReviewUsesTheNewRoute()
    {
        using var fake = new CompanyModelFixture();
        var connection = Connection(handler: fake);
        connection.Save(Input());
        var store = Store(connection);
        var task = MeasuredQueueChecks.Seed(store);
        var old = Prepare(store, task);
        connection.Save(Input(1) with { Provider = "anthropic", Route = "anthropic/fictional-wisp" });
        store.Dashboard();
        Assert.Equal("expired", store.Attempt(old.Id).Measured!.State);
        Assert.Equal("not_sent", store.Attempt(old.Id).Phase);
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(old)));
        var fresh = Prepare(store, task);
        Assert.Equal("anthropic/fictional-wisp", fresh.Measured!.Route);
        Assert.Empty(store.Usage().Entries);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void CompanyFundingStaysOnItsReceiptAndCannotBypassMonthlyLimits()
    {
        var connection = Connection();
        connection.Save(Input());
        var store = Store(connection);
        var task = MeasuredQueueChecks.Seed(store);
        store.Apply(
            C("usage.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyUsageLimits(1, 100_000)),
            }
        );
        var attempt = Prepare(store, task);
        store.Apply(Run(attempt));
        store.Finish(attempt.Id, new("Fictional draft", 150, "fictional-receipt", new(120, 30, BilledUsd: 0.1m)));
        connection.UseHost(new(1, "remove-key-and-use-host"));
        var saved = Store(connection).Usage();
        Assert.Equal("company-key", Assert.Single(saved.Entries).Funding!.Source);
        Assert.Equal(0.1m, saved.Current.CompanyKeyReportedUsd);
        Assert.Equal(0.1m, saved.Current.ReportedUsd);
        Assert.Equal(0m, saved.Current.CompanyKeyEstimatedUsd);
        connection.Save(Input(2));
        var second = Prepare(store, MeasuredQueueChecks.Seed(store));
        Assert.Contains("Monthly usage", Assert.Throws<WorkspaceException>(() => store.Apply(Run(second))).Message);
        Assert.Single(store.Usage().Entries);
        Assert.Equal("host", connection.FundingFor("developer-job").Source);
    }

    [Fact]
    public async Task UnconfirmedProviderResponseKeepsItsUsageHoldAcrossKeyReplacement()
    {
        using var fake = new CompanyModelFixture { Unknown = true };
        var connection = Connection(handler: fake);
        connection.Save(Input());
        var store = Store(connection);
        var attempt = Prepare(store, MeasuredQueueChecks.Seed(store));
        await new CompanyScheduler(store, connection).RunMeasuredOnceAsync(Run(attempt), connection, Token);
        Assert.Equal("unknown", store.Attempt(attempt.Id).Phase);
        Assert.Equal(1, store.Usage().Current.HeldJobs);
        connection.Save(Input(1, "fictional-replacement"));
        store.RefreshModelReviews();
        Assert.Equal(1, Store(connection).Usage().Current.HeldJobs);
        Assert.Equal("company-key", Assert.Single(store.Usage().Entries).Funding!.Source);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task InFlightConnectionChangeWithholdsOutputAndPreservesUnconfirmedUsage()
    {
        using var fake = new CompanyModelFixture
        {
            Started = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var connection = Connection(handler: fake);
        connection.Save(Input());
        var store = Store(connection);
        var attempt = Prepare(store, MeasuredQueueChecks.Seed(store));
        var run = new CompanyScheduler(store, connection).RunMeasuredOnceAsync(Run(attempt), connection, Token);
        await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        connection.Save(Input(1, "fictional-replacement"));
        await run.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal("unknown", store.Attempt(attempt.Id).Phase);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.Equal(1, store.Usage().Current.HeldJobs);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public void CompanyKeyCannotBypassARequiredPurchasedAllowance()
    {
        var connection = Connection();
        connection.Save(Input());
        var store = new CompanyStore(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: connection.MeasuredIdentity,
            routing: connection.Routing
        )
        {
            BillingConfiguration = new(
                new string('b', 64),
                [
                    new(
                        "fictional",
                        "Fictional plan",
                        new("price_fictional", "usd", 1900, "month", 1, true),
                        new(1, 14000, 1, 17)
                    ),
                ]
            ),
            UsageFunding = connection.FundingFor,
        };
        var attempt = Prepare(store, MeasuredQueueChecks.Seed(store));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt)));
        Assert.Empty(store.Usage().Entries);
        Assert.True(store.Usage().SubscriptionRequired);
    }

    public async ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
