using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Google;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

// HireZero's MCP endpoint (docs/adr/0048-assistant-connect-requests.md): an assistant on this computer lists the
// integrations, raises a Connect card and points at an environment variable that holds a key; it never sends or
// receives a key, a browser can never reach it, and only the owner's yes reads the variable. Fictional names.
public sealed class CompanyMcpTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-mcp-");
    private const string Variable = "HIREZERO_FICTIONAL_ADS_DEVELOPER_TOKEN_FOR_TESTS";

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(Variable, null);
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    [Fact]
    public async Task AnAssistantRaisesCardsAndPointsAtKeysWithoutEverHandlingOne()
    {
        var token = TestContext.Current.CancellationToken;
        var model = MeasuredQueueChecks.Model(_root.Path, "company-measured-success");
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            "owner@example.invalid",
            true,
            Path.Combine(_root.Path, "data"),
            fixture: model,
            fixtureUrl: "http://127.0.0.1:0"
        )!;
        await app.StartAsync(token);
        var store = app.Services.GetRequiredService<CompanyStore>();
        var address = new Uri(
            app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
        );
        using var mcp = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = address };
        mcp.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        var id = 0;
        async Task<JsonElement> Call(string method, object? parameters = null)
        {
            using var response = await mcp.PostAsJsonAsync(
                "/mcp",
                new
                {
                    jsonrpc = "2.0",
                    id = ++id,
                    method,
                    @params = parameters,
                },
                token
            );
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var session))
                mcp.DefaultRequestHeaders.Add("Mcp-Session-Id", session.Single());
            return JsonDocument
                .Parse(await response.Content.ReadAsStringAsync(token))
                .RootElement.GetProperty("result");
        }
        async Task<(bool Error, string Text)> Tool(string name, object arguments)
        {
            var result = await Call("tools/call", new { name, arguments });
            return (
                result.GetProperty("isError").GetBoolean(),
                result.GetProperty("content")[0].GetProperty("text").GetString()!
            );
        }

        await Call(
            "initialize",
            new { protocolVersion = "2025-06-18", clientInfo = new { name = "Fictional assistant" } }
        );
        var tools = (await Call("tools/list"))
            .GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString());
        Assert.Equal(["list_integrations", "request_connection", "suggest_key_source"], tools);

        // The list names each integration's keys, never a value.
        var (_, list) = await Tool("list_integrations", new { });
        Assert.Contains(GoogleAdsProvider.DeveloperTokenSecret, list);
        Assert.DoesNotContain("fictional-ads-token-value", list);

        var (refused, why) = await Tool("request_connection", new { integration_id = "no-such-thing", reason = "x" });
        Assert.True(refused);
        Assert.Contains("No such integration", why);
        await Tool("request_connection", new { integration_id = "azure-devops", reason = "To read the work items." });

        // A key source points at a variable; it must be one of the integration's keys.
        var (wrong, _) = await Tool(
            "suggest_key_source",
            new
            {
                integration_id = "google-ads",
                secret_name = "NOT_ITS_KEY",
                env_var = Variable,
                reason = "x",
            }
        );
        Assert.True(wrong);
        await Tool(
            "suggest_key_source",
            new
            {
                integration_id = "google-ads",
                secret_name = GoogleAdsProvider.DeveloperTokenSecret,
                env_var = Variable,
                reason = "The token is already in your environment.",
            }
        );
        var requests = MeasuredQueueChecks.Items<CompanyConnectionRequest>(store, "connections");
        Assert.All(requests, r => Assert.Equal(("Fictional assistant", null), (r.From, r.ThreadId)));
        var ads = Assert.Single(requests, r => r.IntegrationId == "google-ads");
        Assert.Equal(("env:" + Variable, GoogleAdsProvider.DeveloperTokenSecret), (ads.KeySource, ads.SecretName));
        var now = DateTimeOffset.UtcNow;
        Assert.Contains(
            store.Today("today", now.AddHours(-1), now.AddHours(1)).NeedsYou,
            n => n.Title == "Connect Azure DevOps · asked by Fictional assistant"
        );

        // No browser can reach the endpoint: any Origin is refused.
        using (var browser = new HttpRequestMessage(HttpMethod.Post, "/mcp"))
        {
            browser.Headers.Add("Origin", "http://127.0.0.1:5065");
            browser.Content = JsonContent.Create(
                new
                {
                    jsonrpc = "2.0",
                    id = 99,
                    method = "tools/list",
                }
            );
            using var refusedResponse = await mcp.SendAsync(browser, token);
            Assert.Equal(HttpStatusCode.Forbidden, refusedResponse.StatusCode);
        }

        // The owner's yes reads the variable here; the answer never carries the value.
        Environment.SetEnvironmentVariable(Variable, "fictional-ads-token-value");
        using var page = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = address };
        page.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        page.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        page.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        using var used = await page.PostAsJsonAsync(
            "/api/company/integrations/connections/" + ads.Id + "/use-key-source",
            new { },
            token
        );
        var body = await used.Content.ReadAsStringAsync(token);
        Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        Assert.DoesNotContain("fictional-ads-token-value", body);
        Assert.Contains(
            store.Feed(0, 500),
            e => e.Type == "integration.secret" && e.Text.Contains("from the environment variable " + Variable)
        );
        await app.StopAsync(token);
    }
}
