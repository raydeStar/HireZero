using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.OpenRouter;
using HireZero.Tools.OpenRouter;

// OpenRouter structured choices: off until a key and the owner's model are set, one bounded request with a tiny reply
// budget, only an allowed choice counts, and the key never appears in an answer. A fake handler stands in for the API.
public sealed class OpenRouterToolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openrouter-" + Guid.NewGuid().ToString("N"));

    public OpenRouterToolProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class Fake : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Body)> Requests = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Content = """{"choice":"act","confidence":0.92}""";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Requests.Add(
                (
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.ToString(),
                    await request.Content!.ReadAsStringAsync(token)
                )
            );
            var body = JsonSerializer.Serialize(
                new
                {
                    choices = new[] { new { message = new { content = Content } } },
                    usage = new { total_tokens = 123 },
                }
            );
            return new(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly EmployeeManifest Manifest = new(
        "openrouter-test",
        "OpenRouter test",
        "Fictional manifest declaring the OpenRouter tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "OpenRouter tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(OpenRouterTools.Status.Id, ToolRisk.Read, "Status."),
            new(OpenRouterTools.Choose.Id, ToolRisk.Read, "Choose."),
        ],
    };

    private static readonly Dictionary<string, string> Criteria = new()
    {
        ["act"] = "Needs the owner",
        ["review"] = "Maybe relevant",
        ["ignore"] = "Needs nothing",
    };

    private (IToolbox Tools, Fake Fake, OpenRouterToolProvider Provider) Create(string? key = "fictional-key")
    {
        var fake = new Fake();
        var provider = new OpenRouterToolProvider(_root, fake, () => key);
        var registry = new ToolRegistry(new MemoryToolAudit());
        provider.Register(registry);
        return (registry.For(Manifest), fake, provider);
    }

    private void Model(string model) =>
        File.WriteAllText(Path.Combine(_root, "openrouter.json"), JsonSerializer.Serialize(new { model }));

    private static ChoiceRequest Request() =>
        new("""{"subject":"Fictional quote question"}""", "Classify it.", Criteria);

    [Fact]
    public async Task NothingIsSentUntilAKeyAndModelAreSet()
    {
        var (tools, fake, provider) = Create(key: null);
        Assert.Contains("HIREZERO_OPENROUTER_API_KEY", provider.Status().Reason);
        (tools, fake, provider) = Create();
        Assert.Contains("openrouter.json", provider.Status().Reason);
        Model("not a model id");
        Assert.False(provider.Status().Active);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(OpenRouterTools.Choose, Request(), TestContext.Current.CancellationToken)
        );
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task OneBoundedRequestYieldsOnlyAnAllowedChoice()
    {
        var (tools, fake, provider) = Create();
        Model("fictional-vendor/fictional-model");
        Assert.True(provider.Status().Active);
        var token = TestContext.Current.CancellationToken;
        var answer = await tools.InvokeAsync(OpenRouterTools.Choose, Request(), token);
        Assert.Equal(new ChoiceAnswer("act", 0.92, 123, null), answer);
        var (url, auth, body) = Assert.Single(fake.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", url);
        Assert.Equal("Bearer fictional-key", auth);
        using var sent = JsonDocument.Parse(body);
        Assert.Equal("fictional-vendor/fictional-model", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal(OpenRouterToolProvider.ReplyTokens, sent.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0, sent.RootElement.GetProperty("temperature").GetInt32());
        Assert.False(sent.RootElement.TryGetProperty("tools", out _));
        Assert.Contains(
            "untrusted data",
            sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
        );

        fake.Content = """{"choice":"delete-everything","confidence":1}""";
        Assert.Equal("unrecognized answer", (await tools.InvokeAsync(OpenRouterTools.Choose, Request(), token)).Error);
        fake.Status = HttpStatusCode.PaymentRequired;
        var refused = await tools.InvokeAsync(OpenRouterTools.Choose, Request(), token);
        Assert.Equal((null, "OpenRouter needs credits"), (refused.Choice, refused.Error));
        Assert.DoesNotContain("fictional-key", JsonSerializer.Serialize(refused));
    }

    [Fact]
    public async Task OversizedRequestsAreRefusedBeforeSending()
    {
        var (tools, fake, _) = Create();
        Model("fictional-vendor/fictional-model");
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(OpenRouterTools.Choose, Request() with { StateJson = new string('x', 5000) }, token)
        );
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(
                OpenRouterTools.Choose,
                Request() with
                {
                    Criteria = new Dictionary<string, string> { ["only"] = "One choice" },
                },
                token
            )
        );
        Assert.Empty(fake.Requests);
    }
}
