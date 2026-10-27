using System.Net;
using System.Net.Http.Json;
using System.Text;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanySandboxHostingTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("sandbox-host-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company)
        {
            SandboxRuntimeIdentity = "fictional-browser-sandbox-v1",
        };

    private static CompanySandboxInput Input(string script = "console.log('Fictional')") =>
        new("Fictional report", script, [], ["report.txt"], new(5));

    private static void Enable(CompanyStore store) =>
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyComputeLimits(Paused: false)),
            }
        );

    private static CompanySandboxJob Prepare(CompanyStore store, CompanyTask task) =>
        Apply<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(Input()),
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

    private static async Task Until(Func<bool> done)
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!done() && DateTimeOffset.UtcNow < end)
            await Task.Delay(30, Token);
        Assert.True(done(), "The fictional butler expected a durable outcome before the deadline.");
    }

    [Fact]
    public async Task StartupRecoversEveryCompanyWithoutReplayingAnyApprovedScript()
    {
        var first = Store();
        var second = Store("fictional-second");
        Enable(first);
        Enable(second);
        var unsent = Prepare(first, MeasuredQueueChecks.Seed(first));
        first.Apply(Run(unsent));
        var unknown = Prepare(second, MeasuredQueueChecks.Seed(second));
        second.Apply(Run(unknown));
        Assert.NotNull(second.TakeSandboxDispatch(unknown.Id, "fictional-browser-sandbox-v1"));
        var executor = new SandboxFixtureExecutor();
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureDatabase: first.Database,
            fixtureSandbox: executor,
            settings: CompanySettings.Load(_ => null)
        )!;
        await app.StartAsync(Token);
        await Until(() =>
            first.SandboxJob(unsent.Id).SettledAt is not null && second.SandboxJob(unknown.Id).Release is not null
        );
        Assert.Equal("not-sent", first.SandboxJob(unsent.Id).State);
        Assert.Equal(0, first.ComputeUsage().HeldJobs);
        Assert.Equal("unknown", second.SandboxJob(unknown.Id).State);
        Assert.Equal(1, second.ComputeUsage().HeldJobs);
        Assert.Equal(0, executor.Executions);
        Assert.Null(second.TakeSandboxDispatch(unknown.Id, executor.Identity));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task HttpReviewSupportsFullSizedScriptsAndNewApprovalsRunOnceInTheirOwnCompany()
    {
        var store = Store();
        var foreign = Store("fictional-other");
        Enable(store);
        var task = MeasuredQueueChecks.Seed(store);
        store.Claim(task.EmployeeId, false);
        task = store.Work(task.Id);
        var executor = new SandboxFixtureExecutor();
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureDatabase: store.Database,
            fixtureSandbox: executor,
            settings: CompanySettings.Load(_ => null)
        )!;
        await app.StartAsync(Token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(address),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        var proposal = C("sandbox.prepare") with
        {
            Id = task.Id,
            ExpectedVersion = task.Version,
            Body = CompanyJson.Encode(Input("// " + new string('x', 20_000) + "\nconsole.log('Fictional')")),
        };
        using var proposed = await client.PostAsJsonAsync(
            "/api/company/sandboxes/prepare",
            proposal,
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
        var job = CompanyJson.Decode<CompanySandboxJob>(await proposed.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, executor.Executions);
        using var secondProposal = await client.PostAsJsonAsync(
            "/api/company/sandboxes/prepare",
            proposal with
            {
                RequestId = Guid.NewGuid().ToString("D"),
            },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, secondProposal.StatusCode);
        var secondJob = CompanyJson.Decode<CompanySandboxJob>(await secondProposal.Content.ReadAsStringAsync(Token));
        executor.ReleaseFailures = 1;
        executor.OutputText = new string('\u7eb8', 12000);
        var command = Run(job);
        using var started = await client.PostAsJsonAsync("/api/company/commands", command, CompanyJson.Options, Token);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var repeated = await client.PostAsJsonAsync("/api/company/commands", command, CompanyJson.Options, Token);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        await Until(() => store.SandboxJob(job.Id).Release is { Confirmed: false });
        executor.ExecutionEnabled = false;
        var compute = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/company/compute", Token);
        Assert.False(compute.GetProperty("available").GetBoolean());
        Assert.True(compute.GetProperty("recoveryAvailable").GetBoolean());
        Assert.Equal("operator-disabled", compute.GetProperty("runtime").GetProperty("status").GetString());
        using var deniedApproval = await client.PostAsJsonAsync(
            "/api/company/commands",
            Run(secondJob),
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, deniedApproval.StatusCode);
        Assert.Null(store.SandboxJob(secondJob.Id).StartedAt);
        using var repeatedWhileDisabled = await client.PostAsJsonAsync(
            "/api/company/commands",
            command,
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, repeatedWhileDisabled.StatusCode);
        using var unavailable = await client.PostAsJsonAsync(
            "/api/company/sandboxes/prepare",
            proposal with
            {
                RequestId = Guid.NewGuid().ToString("D"),
            },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        using var recovered = await client.PostAsync(
            "/api/company/sandboxes/" + job.Id + "/recover",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            Token
        );
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.NotNull(store.SandboxJob(job.Id).SettledAt);
        var result = store.SandboxJob(job.Id);
        using var preparedOutput = await client.PostAsJsonAsync(
            "/api/company/sandboxes/outputs/prepare",
            C("sandbox.output.prepare") with
            {
                Id = result.Id,
                ExpectedVersion = result.Version,
                Digest = result.ResultDigest,
                Decision = "prepare-review",
                Body = CompanyJson.Encode(new CompanySandboxArtifactInput("report.txt", executor.OutputText)),
            },
            CompanyJson.Options,
            Token
        );
        Assert.Equal(HttpStatusCode.OK, preparedOutput.StatusCode);
        var artifact = CompanyJson.Decode<CompanyArtifact>(await preparedOutput.Content.ReadAsStringAsync(Token));
        Assert.Equal(executor.OutputText, artifact.Body);
        Assert.Equal("needs_review", artifact.State);
        Assert.Equal(result.Id, artifact.SandboxOrigin!.JobId);
        Assert.Equal(1, executor.Executions);
        Assert.Equal("completed", store.SandboxJob(job.Id).State);
        client.DefaultRequestHeaders.Add("X-HireZero-Company", foreign.CompanyId);
        using var refused = await client.PostAsync(
            "/api/company/sandboxes/" + job.Id + "/recover",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            Token
        );
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Empty(foreign.SandboxSummaries());
        Assert.Equal(0, foreign.ComputeUsage().Jobs);
        await app.StopAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    [Fact]
    public async Task ManualRecoveryExplainsAnOriginalRuntimeMismatch()
    {
        var store = Store();
        Enable(store);
        var job = Prepare(store, MeasuredQueueChecks.Seed(store));
        store.Apply(Run(job));
        var replacement = new SandboxFixtureExecutor { Identity = "fictional-replacement-runtime" };
        using var service = new CompanySandboxService(
            () => [store],
            replacement,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CompanySandboxService>.Instance
        );
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => service.RecoverAsync(store, job.Id, Token));
        Assert.Contains("restore its original configuration", error.Message);
        Assert.Null(store.SandboxJob(job.Id).SettledAt);
        Assert.Equal(0, replacement.Executions);
    }
}
