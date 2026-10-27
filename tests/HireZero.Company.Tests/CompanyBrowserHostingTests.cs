using System.Net;
using System.Net.Http.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyBrowserHostingTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid",
        Runtime = "fictional-public-browser-v1";
    private readonly TemporaryRoot _root = new("browser-host-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand c) => CompanyJson.Decode<T>(store.Apply(c));

    private CompanyStore Store(
        string company = CompanyStore.DefaultCompanyId,
        Func<DateTimeOffset>? clock = null,
        Func<bool>? browserAvailable = null
    ) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company)
        {
            BrowserRuntimeIdentity = Runtime,
            BrowserExecutionAvailable = browserAvailable ?? (() => true),
            Clock = clock ?? (() => DateTimeOffset.UtcNow),
        };

    private static void Enable(CompanyStore store) =>
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyComputeLimits(Paused: false)),
            }
        );

    private static CompanyCommand Prepare(CompanyTask task) =>
        C("browser.prepare") with
        {
            Id = task.Id,
            ExpectedVersion = task.Version,
            Body = CompanyJson.Encode(
                new CompanyBrowserInput(
                    "Review fictional pages",
                    ["https://paper.example.invalid"],
                    [new("navigate", "https://paper.example.invalid"), new("read", "body")],
                    new(5)
                )
            ),
        };

    private static CompanyCommand Run(CompanyBrowserJob job) =>
        C("browser.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };

    private WebApplication App(CompanyStore store, BrowserFixtureExecutor? executor) =>
        CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureDatabase: store.Database,
            fixtureBrowser: executor,
            settings: CompanySettings.Load(_ => null)
        )!;

    private static HttpClient Client(WebApplication app)
    {
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        return client;
    }

    private static async Task Until(Func<bool> done)
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!done() && DateTimeOffset.UtcNow < end)
            await Task.Delay(30, Token);
        Assert.True(done(), "The fictional browser butler expected a saved receipt before the deadline.");
    }

    [Fact]
    public async Task StartupFencesQueuedAndDispatchedWorkInEveryCompanyWithoutNewVisits()
    {
        var first = Store();
        var second = Store("fictional-second");
        Enable(first);
        Enable(second);
        var unsent = Apply<CompanyBrowserJob>(first, Prepare(MeasuredQueueChecks.Seed(first)));
        first.Apply(Run(unsent));
        var uncertain = Apply<CompanyBrowserJob>(second, Prepare(MeasuredQueueChecks.Seed(second)));
        second.Apply(Run(uncertain));
        Assert.NotNull(second.TakeBrowserDispatch(uncertain.Id, Runtime));
        var executor = new BrowserFixtureExecutor();
        await using var app = App(first, executor);
        await app.StartAsync(Token);
        await Until(() =>
            first.BrowserJob(unsent.Id).SettledAt is not null && second.BrowserJob(uncertain.Id).Release is not null
        );
        Assert.Equal("not-sent", first.BrowserJob(unsent.Id).State);
        Assert.Equal(0, first.ComputeUsage().HeldJobs);
        Assert.Equal("unknown", second.BrowserJob(uncertain.Id).State);
        Assert.Equal(17, second.ComputeUsage().HeldSeconds);
        Assert.Equal(0, executor.Executions);
        Assert.Null(second.TakeBrowserDispatch(uncertain.Id, Runtime));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task HttpApprovalRunsOncePersistsCapturesAndRecoversWhileDisabledWithinItsCompany()
    {
        var store = Store();
        var foreign = Store("fictional-other");
        Enable(store);
        var task = MeasuredQueueChecks.Seed(store);
        store.Claim(task.EmployeeId, false);
        task = store.Work(task.Id);
        var executor = new BrowserFixtureExecutor { ReleaseFailures = 1 };
        await using var app = App(store, executor);
        await app.StartAsync(Token);
        using var client = Client(app);
        var prepare = Prepare(task);
        using var wrongRoute = await client.PostAsJsonAsync(
            "/api/company/browsers/prepare",
            C("compute.configure"),
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongRoute.StatusCode);
        using var proposed = await client.PostAsJsonAsync(
            "/api/company/browsers/prepare",
            prepare,
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
        var job = CompanyJson.Decode<CompanyBrowserJob>(await proposed.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, executor.Executions);
        using var invalid = await client.PostAsJsonAsync(
            "/api/company/commands",
            Run(job) with
            {
                Digest = "changed",
            },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        var run = Run(job);
        using var accepted = await client.PostAsJsonAsync("/api/company/commands", run, CompanyJson.Options, Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await Until(() => store.BrowserJob(job.Id).Release is { Confirmed: false });
        var saved = store.BrowserJob(job.Id);
        Assert.Equal("completed", saved.State);
        Assert.Single(saved.Execution!.Result!.Captures);
        Assert.Equal(17, store.ComputeUsage().HeldSeconds);
        executor.ExecutionEnabled = false;
        using var repeated = await client.PostAsJsonAsync("/api/company/commands", run, CompanyJson.Options, Token);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1, executor.Executions);
        using var disabled = await client.PostAsJsonAsync(
            "/api/company/browsers/prepare",
            prepare with
            {
                RequestId = Guid.NewGuid().ToString("D"),
            },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        using var recovered = await client.PostAsJsonAsync(
            "/api/company/browsers/" + job.Id + "/recover",
            new { },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.True(store.BrowserJob(job.Id).Release!.Confirmed);
        Assert.Equal(1, executor.Executions);
        Assert.Equal(0, store.ComputeUsage().HeldSeconds);
        var output = C("browser.output.prepare") with
        {
            Id = saved.Id,
            ExpectedVersion = saved.Version,
            Digest = saved.ResultDigest,
            Decision = "prepare-review",
            Body = CompanyJson.Encode(new CompanyBrowserArtifactInput(2, "Fictional website reviews and stationery.")),
        };
        using var artifactResponse = await client.PostAsJsonAsync(
            "/api/company/browsers/outputs/prepare",
            output,
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, artifactResponse.StatusCode);
        var artifact = CompanyJson.Decode<CompanyArtifact>(await artifactResponse.Content.ReadAsStringAsync(Token));
        Assert.Equal("needs_review", artifact.State);
        Assert.Equal(job.Id, artifact.BrowserOrigin!.JobId);
        client.DefaultRequestHeaders.Add("X-HireZero-Company", foreign.CompanyId);
        using var refused = await client.PostAsJsonAsync(
            "/api/company/browsers/" + job.Id + "/recover",
            new { },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Empty(foreign.ComputeSummaries());
        using var foreignOutput = await client.PostAsJsonAsync(
            "/api/company/browsers/outputs/prepare",
            output,
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.NotFound, foreignOutput.StatusCode);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task StartupExpiresSettledCapturesAcrossCompaniesWithoutAnyExecutionRuntime()
    {
        var earlier = DateTimeOffset.UtcNow.AddDays(-31);
        var first = Store(clock: () => earlier);
        var second = Store("fictional-unopened-retention", () => earlier);
        var executor = new BrowserFixtureExecutor();
        var jobs = new List<(CompanyStore Store, string Id)>();
        foreach (var company in new[] { first, second })
        {
            Enable(company);
            var job = Apply<CompanyBrowserJob>(company, Prepare(MeasuredQueueChecks.Seed(company)));
            company.Apply(Run(job));
            job = company.TakeBrowserDispatch(job.Id, Runtime)!;
            var result = await executor.ExecuteAsync(job.Request, () => company.BrowserAuthorized(job.Id), Token);
            company.RecordBrowserResult(job.Id, Runtime, result);
            company.RecordBrowserRelease(job.Id, Runtime, new(true, "released", result.Release.ResourceId));
            company.Apply(
                C("browser.retention") with
                {
                    ExpectedVersion = 0,
                    Body = CompanyJson.Encode(new CompanyBrowserRetentionInput(30)),
                    Decision = "apply-capture-retention",
                }
            );
            Assert.Null(company.BrowserJob(job.Id).CaptureDeletion);
            jobs.Add((company, job.Id));
        }
        await using var app = App(first, null);
        await app.StartAsync(Token);
        await Until(() => jobs.All(j => j.Store.BrowserJob(j.Id).CaptureDeletion is not null));
        foreach (var (company, id) in jobs)
        {
            var deleted = company.BrowserJob(id);
            Assert.Null(deleted.Execution!.Result);
            Assert.Equal("retention", deleted.CaptureDeletion!.Reason);
            Assert.True(deleted.Release!.Confirmed);
        }
        Assert.Equal(2, executor.Executions);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task MissingAndChangedRuntimeDoNotActivateOrMisrepresentRecovery()
    {
        var store = Store();
        Enable(store);
        var job = Apply<CompanyBrowserJob>(store, Prepare(MeasuredQueueChecks.Seed(store)));
        store.Apply(Run(job));
        await using (var app = App(store, null))
        {
            await app.StartAsync(Token);
            using var client = Client(app);
            using var response = await client.GetAsync("/api/company", Token);
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            Assert.False(json.RootElement.GetProperty("capabilities").GetProperty("browserExecution").GetBoolean());
            using var recover = await client.PostAsJsonAsync(
                "/api/company/browsers/" + job.Id + "/recover",
                new { },
                CompanyJson.Options,
                Token
            );
            Assert.Equal(HttpStatusCode.Conflict, recover.StatusCode);
            Assert.Null(store.BrowserJob(job.Id).SettledAt);
            await app.StopAsync(Token);
        }
        using var service = new CompanyBrowserService(
            () => [store],
            new BrowserFixtureExecutor { Identity = "fictional-replacement" },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CompanyBrowserService>.Instance
        );
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => service.RecoverAsync(store, job.Id, Token));
        Assert.Contains("restore its original configuration", error.Message);
        Assert.Null(store.BrowserJob(job.Id).SettledAt);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
