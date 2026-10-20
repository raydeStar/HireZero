using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.ModelApis;

// API models (docs/adr/0036-api-model-provider.md) against a fake handler with fictional replies: the right API for each
// route, the job's allowance enforced as a US-cent cap before sending, truncated or tool-using replies withheld, refusals
// reported with no usage, and unconfirmed failures left unknown. Keys and model ids are fictional.
public sealed class ApiModelProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly ModelPrice Price = new(3m, 15m);

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, Dictionary<string, string> Headers, string Body)> Requests = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Reply;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(
                (
                    url,
                    request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                    await request.Content!.ReadAsStringAsync(token)
                )
            );
            var json =
                Reply
                ?? (
                    url.Contains("anthropic", StringComparison.Ordinal)
                        ? """{"id":"msg_1","content":[{"type":"text","text":"A fictional tagline."}],"stop_reason":"end_turn","usage":{"input_tokens":120,"output_tokens":30}}"""
                        : """{"id":"chatcmpl-1","choices":[{"message":{"content":"A fictional tagline."},"finish_reason":"stop"}],"usage":{"prompt_tokens":130,"completion_tokens":40,"completion_tokens_details":{"reasoning_tokens":10}}}"""
                );
            return new(Status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (ModelCall Call, CompanyMeasuredJob Job) Job(string route, decimal allowanceCents = 100)
    {
        var context = new CompanyContext(
            "ctx-1",
            "emp-1",
            "marketing",
            "thr-1",
            "Fictional responsibility",
            "Fictional company facts",
            "Fictional department facts",
            "",
            [],
            [],
            "Draft a fictional tagline."
        );
        var call = new ModelCall("attempt-1", context, "Fictional instructions.", 4000, 1000, route);
        var bytes =
            Encoding.UTF8.GetByteCount(CompanyJson.Encode(context)) + Encoding.UTF8.GetByteCount(call.Instructions);
        var job = new CompanyMeasuredJob(
            "dispatched",
            "fictional-digest",
            CompanyJson.Digest(context),
            ContentGuard.Digest(call.Instructions),
            route,
            ApiModelProvider.Identity,
            allowanceCents,
            DateTimeOffset.UtcNow.AddMinutes(10),
            bytes,
            1,
            1,
            CompanyMeasuredPolicy.Current
        );
        return (call, job);
    }

    private static (ApiModelProvider Provider, Fake Fake) Create(
        Dictionary<string, string> keys,
        Func<string, ModelPrice?>? price = null
    )
    {
        var fake = new Fake();
        return (new ApiModelProvider(name => keys.GetValueOrDefault(name), fake, price ?? (_ => Price)), fake);
    }

    // The store builds a measured call with no output limit of its own (0); the policy's output target is the limit, in
    // the request and in the worst case, never zero.
    [Fact]
    public async Task AMeasuredCallUsesItsPolicyOutputLimit()
    {
        var (provider, fake) = Create(new() { ["HIREZERO_OPENROUTER_API_KEY"] = "fictional-router-key" });
        var (call, job) = Job("openai/fictional-luna");
        await provider.CompleteMeasuredAsync(call with { MaxOutputTokens = 0 }, job, Token);
        using var json = JsonDocument.Parse(Assert.Single(fake.Requests).Body);
        Assert.Equal(
            CompanyMeasuredPolicy.Current.OutputTarget,
            json.RootElement.GetProperty("max_completion_tokens").GetInt32()
        );
    }

    // OpenRouter gets low reasoning effort and the reviewed price as a ceiling, so a variably priced router cannot bill
    // above the worst case; vendor APIs get neither.
    [Fact]
    public async Task OpenRouterCallsUseLowEffortAndTheReviewedPriceAsACeiling()
    {
        var (provider, fake) = Create(
            new()
            {
                [ApiModelProvider.OpenAIKey] = "fictional-openai-key",
                [ApiModelProvider.OpenRouterKey] = "fictional-router-key",
            }
        );
        var (call, job) = Job("fictional/router");
        await provider.CompleteMeasuredAsync(call, job, Token);
        var (openaiCall, openaiJob) = Job("openai/fictional-mini");
        await provider.CompleteMeasuredAsync(openaiCall, openaiJob, Token);

        using var router = JsonDocument.Parse(fake.Requests[0].Body);
        Assert.Equal("low", router.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        var ceiling = router.RootElement.GetProperty("provider").GetProperty("max_price");
        Assert.Equal(
            (3m, 15m),
            (ceiling.GetProperty("prompt").GetDecimal(), ceiling.GetProperty("completion").GetDecimal())
        );
        using var openai = JsonDocument.Parse(fake.Requests[1].Body);
        Assert.False(openai.RootElement.TryGetProperty("reasoning", out _));
        Assert.False(openai.RootElement.TryGetProperty("provider", out _));
    }

    [Fact]
    public async Task AnthropicRoutesUseTheMessagesApiWithTheKeyAndLimits()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-anthropic-key" });
        var (call, job) = Job("anthropic/fictional-sonnet");

        var reply = await provider.CompleteMeasuredAsync(call, job, Token);

        Assert.Equal(("A fictional tagline.", 150L, "anthropic:msg_1"), (reply.Body, reply.TotalTokens, reply.Receipt));
        var (url, headers, body) = Assert.Single(fake.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", url);
        Assert.Equal("fictional-anthropic-key", headers["x-api-key"]);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("fictional-sonnet", json.RootElement.GetProperty("model").GetString());
        Assert.Equal(1000, json.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Fictional instructions.", json.RootElement.GetProperty("system").GetString());
        Assert.False(provider.Qualified);
    }

    [Fact]
    public async Task OpenAIUsesItsOwnKeyAndOtherVendorsGoThroughOpenRouter()
    {
        var (provider, fake) = Create(
            new()
            {
                [ApiModelProvider.OpenAIKey] = "fictional-openai-key",
                [ApiModelProvider.OpenRouterKey] = "fictional-router-key",
            }
        );
        var openai = await provider.CompleteMeasuredAsync(
            Job("openai/fictional-mini").Call,
            Job("openai/fictional-mini").Job,
            Token
        );
        await provider.CompleteMeasuredAsync(
            Job("mistral/fictional-large").Call,
            Job("mistral/fictional-large").Job,
            Token
        );

        Assert.Equal(new ModelUsage(130, 40, 10), openai.Usage);
        Assert.Equal("https://api.openai.com/v1/chat/completions", fake.Requests[0].Url);
        Assert.Equal("Bearer fictional-openai-key", fake.Requests[0].Headers["Authorization"]);
        Assert.Contains("\"model\":\"fictional-mini\"", fake.Requests[0].Body);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", fake.Requests[1].Url);
        Assert.Equal("Bearer fictional-router-key", fake.Requests[1].Headers["Authorization"]);
        Assert.Contains("\"model\":\"mistral/fictional-large\"", fake.Requests[1].Body);
    }

    [Fact]
    public async Task TheAllowanceIsASpendingCapCheckedBeforeSending()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-key" });
        var (call, job) = Job("anthropic/fictional-sonnet", allowanceCents: 1);
        var worst = ApiModelProvider.WorstCaseUsd(Price, job.ApplicationBytes, 1000);
        Assert.True(worst > 0.01m);

        var refused = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job, Token)
        );
        Assert.Equal(0, refused.KnownUsage);
        Assert.Contains("more than its allowance of 1 cents", refused.Message);

        var unpriced = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-key" }, _ => null).Provider;
        Assert.Contains(
            "no reviewed price",
            (
                await Assert.ThrowsAsync<ModelTransportException>(() =>
                    unpriced.CompleteMeasuredAsync(
                        Job("anthropic/fictional-sonnet").Call,
                        Job("anthropic/fictional-sonnet").Job,
                        Token
                    )
                )
            ).Message
        );
        var keyless = Create([]).Provider;
        Assert.Contains(
            "No API key serves",
            (
                await Assert.ThrowsAsync<ModelTransportException>(() =>
                    keyless.CompleteMeasuredAsync(
                        Job("anthropic/fictional-sonnet").Call,
                        Job("anthropic/fictional-sonnet").Job,
                        Token
                    )
                )
            ).Message
        );
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task OnlyTheExactAuthorizedJobIsSent()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-key" });
        var (call, job) = Job("anthropic/fictional-sonnet");
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            provider.CompleteMeasuredAsync(call with { ModelRoute = "anthropic/other" }, job, Token)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            provider.CompleteMeasuredAsync(call, job with { State = "proposed" }, Token)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            provider.CompleteMeasuredAsync(call with { Instructions = "Changed." }, job, Token)
        );
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(
        """{"id":"m","content":[{"type":"text","text":"cut"}],"stop_reason":"max_tokens","usage":{"input_tokens":120,"output_tokens":1000}}""",
        "truncated"
    )]
    [InlineData(
        """{"id":"m","content":[{"type":"tool_use"}],"stop_reason":"tool_use","usage":{"input_tokens":120,"output_tokens":20}}""",
        "asked for a tool"
    )]
    public async Task TruncatedOrToolUsingRepliesAreWithheldWithTheirUsage(string reply, string reason)
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-key" });
        fake.Reply = reply;
        var (call, job) = Job("anthropic/fictional-sonnet");

        var withheld = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job, Token)
        );

        Assert.Contains(reason, withheld.Message);
        Assert.True(withheld.KnownUsage > 0);
        Assert.Equal("anthropic:m", withheld.Receipt);
    }

    [Fact]
    public async Task ARefusalHasNoUsageAndAServerFailureStaysUnknown()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-key" });
        var (call, job) = Job("anthropic/fictional-sonnet");
        fake.Status = HttpStatusCode.Unauthorized;
        fake.Reply = """{"type":"error","error":{"type":"authentication_error","message":"fictional"}}""";
        var refused = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job, Token)
        );
        Assert.Equal((0L, false), (refused.KnownUsage, refused.AccountingViolation));
        Assert.Contains("HTTP 401, authentication_error", refused.Message);

        fake.Status = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteMeasuredAsync(call, job, Token));
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task MoreInputThanTheBoundIsAnAccountingFault()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.AnthropicKey] = "fictional-key" });
        fake.Reply =
            """{"id":"m","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":999999,"output_tokens":5}}""";
        var (call, job) = Job("anthropic/fictional-sonnet");
        var fault = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job, Token)
        );
        Assert.True(fault.AccountingViolation);
    }

    [Fact]
    public async Task OpenRouterCarriesItsBilledCostOnTheReplyUsage()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.OpenRouterKey] = "fictional-router-key" });
        fake.Reply =
            """{"id":"gen-1","choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":130,"completion_tokens":40,"cost":0.0025}}""";
        var (call, job) = Job("fictional/router");

        var reply = await provider.CompleteMeasuredAsync(call, job, Token);

        Assert.Equal(new ModelUsage(130, 40, BilledUsd: 0.0025m), reply.Usage);
    }

    // A variably priced router may not honor the price ceiling; OpenRouter's billed cost above the allowance is an
    // accounting fault, and the reply is withheld.
    [Fact]
    public async Task AnOpenRouterBillAboveTheAllowanceIsAnAccountingFault()
    {
        var (provider, fake) = Create(new() { [ApiModelProvider.OpenRouterKey] = "fictional-router-key" });
        fake.Reply =
            """{"id":"gen-1","choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":130,"completion_tokens":40,"cost":1.25}}""";
        var (call, job) = Job("fictional/router");
        var fault = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job, Token)
        );
        Assert.True(fault.AccountingViolation);
        Assert.Contains("$1.2500", fault.Message);
        Assert.Equal(1.25m, fault.Usage?.BilledUsd);
    }

    // Codex stays the model while it is selected; the API model only on explicit opt-in.
    [Fact]
    public async Task TheHostUsesAnApiModelOnlyWhenAskedAndCodexIsNotSelected()
    {
        var root = Directory.CreateTempSubdirectory("api-models-host-").FullName;
        try
        {
            var settings = CompanySettings.Load(name =>
                name switch
                {
                    "HIREZERO_API_MODELS" => "1",
                    "HIREZERO_DATA_ROOT" => root,
                    "HIREZERO_EXECUTION_HOLD" => "0",
                    _ => null,
                }
            );
            await using var app = CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                "owner@example.invalid",
                true,
                Path.Combine(root, "host"),
                fixtureUrl: "http://127.0.0.1:0",
                fixtureTools: _ => { },
                settings: settings
            )!;
            await app.StartAsync(Token);
            var address = app.Urls.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/company");
            request.Headers.Host = "127.0.0.1:5065";
            var json = JsonDocument
                .Parse(await (await client.SendAsync(request, Token)).Content.ReadAsStringAsync(Token))
                .RootElement;
            var caps = json.GetProperty("capabilities");
            Assert.Equal("usd-cents", caps.GetProperty("allowanceUnit").GetString());
            Assert.StartsWith("API model selected", caps.GetProperty("providerStatus").GetString());
            Assert.True(caps.GetProperty("measuredOneJob").GetBoolean());
            await app.StopAsync(Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
