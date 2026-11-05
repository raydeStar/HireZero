using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HireZero.Core;
using HireZero.Integrations.ModelApis;

public sealed class VultrProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal const string Route = ModelVendorPolicy.VultrRoute;

    internal sealed class Fake : HttpMessageHandler
    {
        public readonly List<(Uri Url, string? Key, JsonNode Body)> Requests = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Disconnect;
        public bool WaitForCancellation;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Reply =
            """{"id":"fictional-vultr-receipt","choices":[{"message":{"content":"A fictional draft."},"finish_reason":"stop"}],"usage":{"prompt_tokens":130,"completion_tokens":12000,"completion_tokens_details":{"reasoning_tokens":11000}}}""";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Requests.Add(
                (
                    request.RequestUri!,
                    request.Headers.Authorization?.ToString(),
                    JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!
                )
            );
            Started.TrySetResult();
            if (Disconnect)
                throw new HttpRequestException(
                    "Fictional connection lost after send; the butler keeps the reservation."
                );
            if (WaitForCancellation)
                await Task.Delay(Timeout.Infinite, token);
            return new(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }

    internal static ApiModelProvider Provider(Fake fake, Func<string, ModelPrice?>? price = null) =>
        new(
            name => name == ApiModelProvider.VultrKey ? "fictional-vultr-key" : null,
            fake,
            price ?? ModelRoutes.Price,
            vendorPolicy: new("vultr")
        );

    private static (ModelCall Call, CompanyMeasuredJob Job) Packet()
    {
        var context = new CompanyContext(
            "fictional-context",
            "employee",
            "marketing",
            "thread",
            "Fictional draft",
            "",
            "",
            "",
            [],
            [],
            "Draft a fictional plan."
        );
        var call = new ModelCall(Guid.NewGuid().ToString("N"), context, "Use only the fictional facts.", 0, 0, Route);
        var job = new CompanyMeasuredJob(
            "dispatched",
            "fictional-digest",
            CompanyJson.Digest(context),
            ContentGuard.Digest(call.Instructions),
            Route,
            ApiModelProvider.Identity,
            5,
            DateTimeOffset.UtcNow.AddMinutes(10),
            Encoding.UTF8.GetByteCount(CompanyJson.Encode(context)) + Encoding.UTF8.GetByteCount(call.Instructions),
            1,
            1,
            CompanyMeasuredPolicy.WithReasoningRoom(CompanyMeasuredPolicy.Deliverable)
        );
        return (call, job);
    }

    [Fact]
    public async Task VultrUsesBearerLowEffortAndTheFullReviewedOutputLimit()
    {
        using var fake = new Fake();
        using var provider = Provider(fake);
        var (call, job) = Packet();
        var reply = await provider.CompleteMeasuredAsync(call, job, Token);
        Assert.Equal(new ModelUsage(130, 12000, 11000), reply.Usage);
        Assert.Equal("vultr:fictional-vultr-receipt", reply.Receipt);
        Assert.Equal(12130, reply.TotalTokens);
        var request = Assert.Single(fake.Requests);
        Assert.Equal("https://api.vultrinference.com/v1/chat/completions", request.Url.AbsoluteUri);
        Assert.Equal("Bearer fictional-vultr-key", request.Key);
        Assert.Equal("deepseek-v4.1-flash-1017", request.Body["model"]!.GetValue<string>());
        Assert.Equal("low", request.Body["reasoning_effort"]!.GetValue<string>());
        Assert.Equal(24000, request.Body["max_tokens"]!.GetValue<int>());
        Assert.Null(request.Body["max_completion_tokens"]);
        Assert.Null(request.Body["tools"]);
        Assert.Null(request.Body["provider"]);
        Assert.Equal(call.Instructions, request.Body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(CompanyJson.Encode(call.Context), request.Body["messages"]![1]!["content"]!.GetValue<string>());
        Assert.True(job.Policy.LocalDeadlineSeconds >= 300);
        Assert.Equal(new ModelPrice(0.15m, 0.60m), ModelRoutes.Price(Route));
    }

    [Fact]
    public async Task WorstCasePriceKeyAndConsumedAuthorityAreRequiredBeforeSend()
    {
        using var fake = new Fake();
        using var provider = Provider(fake);
        var (call, job) = Packet();
        var error = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job with { CreditAllowance = 1 }, Token)
        );
        Assert.Equal(new ModelUsage(0, 0), error.Usage);
        using var unpriced = Provider(fake, _ => null);
        Assert.Contains(
            "no reviewed price",
            (
                await Assert.ThrowsAsync<ModelTransportException>(() =>
                    unpriced.CompleteMeasuredAsync(call, job, Token)
                )
            ).Message
        );
        using var keyless = new ApiModelProvider(
            name => name == ApiModelProvider.OpenRouterKey ? "fictional-router-key" : null,
            fake
        );
        Assert.Null(keyless.Endpoint(Route));
        await Assert.ThrowsAsync<ModelTransportException>(() => keyless.CompleteMeasuredAsync(call, job, Token));
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            provider.CompleteMeasuredAsync(call, job with { State = "prepared" }, Token)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            provider.CompleteMeasuredAsync(call, job with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, Token)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            provider.CompleteMeasuredAsync(call with { Instructions = "Changed" }, job, Token)
        );
        await Assert.ThrowsAsync<ModelUnavailableException>(() => provider.CompleteAsync(call, Token));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Refused4xxHasZeroUsageButRedirectAndServerFailureStayUnknown()
    {
        using var fake = new Fake { Status = HttpStatusCode.Unauthorized, Reply = "{}" };
        using var provider = Provider(fake);
        var (call, job) = Packet();
        var refusal = await Assert.ThrowsAsync<ModelTransportException>(() =>
            provider.CompleteMeasuredAsync(call, job, Token)
        );
        Assert.Equal(new ModelUsage(0, 0), refusal.Usage);
        foreach (var status in new[] { HttpStatusCode.Redirect, HttpStatusCode.InternalServerError })
        {
            fake.Status = status;
            (call, job) = Packet();
            await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteMeasuredAsync(call, job, Token));
            var replay = await Assert.ThrowsAsync<ModelTransportException>(() =>
                provider.CompleteMeasuredAsync(call, job, Token)
            );
            Assert.Null(replay.KnownUsage);
        }
        Assert.Equal(3, fake.Requests.Count);
    }

    [Fact]
    public async Task ToolFunctionTruncatedAndExcessOutputRepliesAreWithheldWithUsage()
    {
        foreach (
            var change in new Action<JsonNode>[]
            {
                json => json["choices"]![0]!["message"]!["tool_calls"] = JsonNode.Parse("[{\"type\":\"function\"}]"),
                json => json["choices"]![0]!["message"]!["function_call"] = JsonNode.Parse("{\"name\":\"fictional\"}"),
                json => json["choices"]![0]!["finish_reason"] = "length",
                json => json["usage"]!["completion_tokens"] = 24001,
            }
        )
        {
            using var fake = new Fake();
            var json = JsonNode.Parse(fake.Reply)!;
            change(json);
            fake.Reply = json.ToJsonString();
            using var provider = Provider(fake);
            var (call, job) = Packet();
            var error = await Assert.ThrowsAsync<ModelTransportException>(() =>
                provider.CompleteMeasuredAsync(call, job, Token)
            );
            Assert.NotNull(error.Usage);
            Assert.Equal("vultr:fictional-vultr-receipt", error.Receipt);
            Assert.Single(fake.Requests);
        }
    }

    [Fact]
    public async Task MissingAndInconsistentUsageWithholdTheAnswerAndPreserveItsReceipt()
    {
        foreach (
            var change in new Action<JsonNode>[]
            {
                json => json.AsObject().Remove("usage"),
                json => json["usage"]!["completion_tokens_details"]!["reasoning_tokens"] = 24001,
            }
        )
        {
            using var fake = new Fake();
            var json = JsonNode.Parse(fake.Reply)!;
            change(json);
            fake.Reply = json.ToJsonString();
            using var provider = Provider(fake);
            var (call, job) = Packet();
            var withheld = await Assert.ThrowsAsync<ModelTransportException>(() =>
                provider.CompleteMeasuredAsync(call, job, Token)
            );
            Assert.Equal("vultr:fictional-vultr-receipt", withheld.Receipt);
            Assert.Null(withheld.KnownUsage);
            Assert.Single(fake.Requests);
        }
    }

    [Fact]
    public async Task CancellationStopsTheRequestAndAnAlreadyCanceledCallSendsNothing()
    {
        using var fake = new Fake { WaitForCancellation = true };
        using var provider = Provider(fake);
        var (call, job) = Packet();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var completion = provider.CompleteMeasuredAsync(call, job, cancellation.Token);
        await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        var fresh = Packet();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.CompleteMeasuredAsync(fresh.Call, fresh.Job, cancellation.Token)
        );
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task BaseUrlAllowsOnlyHttpsOrLoopbackAndNeverLeaksTheKeyIntoAUrl()
    {
        foreach (
            var url in new[]
            {
                "http://example.invalid/v1",
                "ftp://localhost/v1",
                "https://key@example.invalid/v1",
                "https://example.invalid/v1?key=fictional",
                "https://example.invalid/v1#fragment",
            }
        )
            Assert.Throws<InvalidOperationException>(() => new ApiModelProvider(vultrBaseUrl: url));
        using var fake = new Fake();
        using var provider = new ApiModelProvider(
            _ => "fictional-key",
            fake,
            vultrBaseUrl: "http://127.0.0.1:12345/v1"
        );
        var (call, job) = Packet();
        await provider.CompleteMeasuredAsync(call, job, Token);
        Assert.Equal("http://127.0.0.1:12345/v1/chat/completions", Assert.Single(fake.Requests).Url.AbsoluteUri);
    }
}
