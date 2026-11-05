using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.ModelApis;
using Microsoft.Data.Sqlite;

public sealed class CompanySignupSpendTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("signup-spend-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly ModelPrice Price = new(1, 1);
    private const string Route = "openai/fictional-luna";

    private sealed class Fake : HttpMessageHandler
    {
        public int Calls;
        public bool Unknown;
        public bool Refused;
        public bool ToolCall;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            if (Unknown)
                throw new HttpRequestException("Fictional connection lost after dispatch.");
            return Task.FromResult(
                new HttpResponseMessage(Refused ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        ToolCall
                            ? """{"id":"fictional","choices":[{"message":{"content":"withheld","tool_calls":[{"type":"function"}]},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":20}}"""
                            : """{"id":"fictional","choices":[{"message":{"content":"A fictional reply."},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":20}}"""
                    ),
                }
            );
        }
    }

    private CompanyStore Company(int companyCap = 50, int dailyCap = 500)
    {
        var database = System.IO.Path.Combine(_root.Path, "company.sqlite");
        var root = new CompanyStore(database, "owner@example.invalid", CompanyWorkspace.Definitions());
        var policy = new CompanySignupPolicy(CompanyCapCents: companyCap, DailyCapCents: dailyCap);
        root.ConfigureSignup(policy);
        var created = root.Signup("guest", "guest@example.invalid", true, "Guest", policy);
        return new(database, "guest", CompanyWorkspace.Definitions(), companyId: created.Id);
    }

    private static (ModelCall Call, CompanyMeasuredJob Job) Request(string id = "attempt")
    {
        var context = new CompanyContext(
            "context",
            "employee",
            "marketing",
            "thread",
            "Fictional responsibility",
            "Fictional company",
            "Fictional department",
            "",
            [],
            [],
            "Draft a tagline."
        );
        var call = new ModelCall(id, context, "Fictional instructions.", 4000, 1000, Route);
        var job = new CompanyMeasuredJob(
            "dispatched",
            "digest",
            CompanyJson.Digest(context),
            ContentGuard.Digest(call.Instructions),
            Route,
            ApiModelProvider.Identity,
            5,
            DateTimeOffset.UtcNow.AddMinutes(5),
            Encoding.UTF8.GetByteCount(CompanyJson.Encode(context)) + Encoding.UTF8.GetByteCount(call.Instructions),
            1,
            1,
            CompanyMeasuredPolicy.Current
        );
        return (call, job);
    }

    private static CompanyModelConnection Connection(CompanyStore store, ApiModelProvider api) =>
        new(store.CompanyId, api, new CompanyModelRouting(Route, Route), () => [new(Route, "Fictional Luna", Price)])
        {
            SpendStore = store,
        };

    [Fact]
    public async Task LifetimeCapRefusesWorstCaseBeforeHttpSend()
    {
        var store = Company(companyCap: 1);
        store.ReserveModelSpend("earlier", .009m);
        using var fake = new Fake();
        using var api = new ApiModelProvider(_ => "fictional-key", fake, _ => Price);
        var request = Request();
        var error = await Assert.ThrowsAsync<ModelTransportException>(() =>
            Connection(store, api).CompleteMeasuredAsync(request.Call, request.Job, Token)
        );
        Assert.Contains("company's free allowance", error.Message);
        Assert.Equal(0, error.KnownUsage);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task DailyCapIncludesOtherCompaniesAndRefusesBeforeHttpSend()
    {
        var store = Company(dailyCap: 1);
        var other = new CompanyStore(store.Database, "owner@example.invalid", CompanyWorkspace.Definitions());
        other.ReserveModelSpend("earlier", .009m);
        using var fake = new Fake();
        using var api = new ApiModelProvider(_ => "fictional-key", fake, _ => Price);
        var request = Request();
        var error = await Assert.ThrowsAsync<ModelTransportException>(() =>
            Connection(store, api).CompleteMeasuredAsync(request.Call, request.Job, Token)
        );
        Assert.Contains("daily free allowance", error.Message);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task RealUsageSettlesSuccessAndWithheldToolRepliesAndKnownRefusalsReleaseTheirHold()
    {
        var store = Company();
        using var fake = new Fake();
        using var api = new ApiModelProvider(_ => "fictional-key", fake, _ => Price);
        var connection = Connection(store, api);
        var request = Request();
        await connection.CompleteMeasuredAsync(request.Call, request.Job, Token);
        Assert.Equal(49.997m, store.FreeAllowance()!.CompanyRemainingCents);
        Assert.Equal(499.997m, store.FreeAllowance()!.DailyRemainingCents);
        fake.ToolCall = true;
        var withheld = await Assert.ThrowsAsync<ModelTransportException>(() =>
            connection.CompleteMeasuredAsync(request.Call with { DispatchId = "tool" }, request.Job, Token)
        );
        Assert.Contains("tool", withheld.Message);
        Assert.Equal(49.994m, store.FreeAllowance()!.RemainingCents);
        fake.Refused = true;
        await Assert.ThrowsAsync<ModelTransportException>(() =>
            connection.CompleteMeasuredAsync(request.Call with { DispatchId = "refused" }, request.Job, Token)
        );
        Assert.Equal(49.994m, store.FreeAllowance()!.RemainingCents);
        Assert.Equal(3, fake.Calls);
    }

    [Fact]
    public async Task UnknownOutcomeKeepsWorstCaseAcrossRestartAndCannotBeDispatchedAgain()
    {
        var store = Company();
        using var fake = new Fake { Unknown = true };
        using var api = new ApiModelProvider(_ => "fictional-key", fake, _ => Price);
        var request = Request();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Connection(store, api).CompleteMeasuredAsync(request.Call, request.Job, Token)
        );
        var worst = ApiModelProvider.WorstCaseUsd(Price, request.Job.ApplicationBytes, request.Call.MaxOutputTokens);
        Assert.Equal(50 - worst * 100, store.FreeAllowance()!.RemainingCents);
        var reopened = new CompanyStore(
            store.Database,
            "guest",
            CompanyWorkspace.Definitions(),
            companyId: store.CompanyId
        );
        await Assert.ThrowsAsync<ModelTransportException>(() =>
            Connection(reopened, api).CompleteMeasuredAsync(request.Call, request.Job, Token)
        );
        Assert.Equal(1, fake.Calls);
        Assert.Equal(store.FreeAllowance(), reopened.FreeAllowance());
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
