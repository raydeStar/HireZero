using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Web;
using HireZero.Tools.Web;

public sealed class BusinessDiscoveryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Pages(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<Uri> Requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Content(string body, string type = "text/html") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

    [Fact]
    public void DiscoveryOnlyChoosesRelevantOrdinaryLinksOnTheSameOrigin()
    {
        var links = WebReader.DiscoveryLinks(
            """
            <a href='/about#team'>About</a><a href='/services'>Services</a><a href='/pricing'>Pricing</a>
            <a href='/services'>Same services</a><a href='https://other.example.invalid/about'>About another firm</a>
            <a href='/account/services'>Services login</a><a href='/checkout?product=1'>Products</a>
            <a href='/api/services'>Services API</a><a href='/services.pdf'>Services download</a>
            <script><a href='/private/about'>About hidden page</a></script>
            <a data-href='/fake/services'>Not a real link</a><a href='javascript:alert(1)'>About</a>
            <a href='/sponsored/services' rel='sponsored nofollow'>Services</a>
            <a href='/unquoted/services' rel=nofollow>Services</a>
            <a href='http://example.invalid/about'>About insecure</a><a href='https://user:pass@example.invalid/about'>About credentials</a>
            """,
            new("https://example.invalid/")
        );
        Assert.Equal(
            new[]
            {
                "https://example.invalid/services",
                "https://example.invalid/about",
                "https://example.invalid/pricing",
            },
            links.Select(l => l.Url)
        );
    }

    [Fact]
    public async Task DiscoveryReadsThreePagesAtMostAndDoesNotRecursivelyFollowTheirLinks()
    {
        var handler = new Pages(request =>
            request.RequestUri!.AbsolutePath == "/robots.txt"
                ? Content("User-agent: *\nAllow: /", "text/plain")
                : Content(
                    "<main>Fictional public craft products.</main><a href='/services'>Services</a><a href='/about'>About</a><a href='/pricing'>Pricing</a><a href='/blog'>Blog</a>"
                )
        );
        var result = await new WebReader(handler).DiscoverAsync(
            new("https://example.invalid/", "https://example.invalid/"),
            Token
        );
        Assert.Equal(3, result.Pages.Length);
        Assert.Empty(result.Notices);
        Assert.Equal(
            new[] { "/", "/services", "/about" },
            handler.Requests.Where(u => u.AbsolutePath != "/robots.txt").Select(u => u.AbsolutePath)
        );
        Assert.Equal(3, handler.Requests.Count(u => u.AbsolutePath == "/robots.txt"));
    }

    [Fact]
    public async Task DeniedOrRedirectedChildrenAreReportedWithoutDiscardingGoodEvidence()
    {
        var handler = new Pages(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/robots.txt" => Content("User-agent: *\nDisallow: /services", "text/plain"),
                "/" => Content(
                    "<main>Fictional public paper kits.</main><a href='/services'>Services</a><a href='/about'>About</a>"
                ),
                "/about" => new(HttpStatusCode.Found)
                {
                    Headers = { Location = new("https://other.example.invalid/about") },
                },
                _ => throw new InvalidOperationException("The reader wandered past the velvet rope."),
            }
        );
        var result = await new WebReader(handler).DiscoverAsync(
            new("https://example.invalid/", "https://example.invalid/"),
            Token
        );
        Assert.Single(result.Pages);
        Assert.Equal(2, result.Notices.Length);
        Assert.DoesNotContain(handler.Requests, u => u.AbsolutePath == "/services" || u.Host != "example.invalid");
        Assert.Contains(result.Notices, n => n.Contains("crawl policy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchMakesOneFixedApiRequestAndNeverFollowsOrPromotesCandidates()
    {
        var handler = new Pages(request =>
        {
            Assert.Equal("fictional-key", Assert.Single(request.Headers.GetValues("X-Subscription-Token")));
            return Content(
                """{"web":{"results":[{"url":"https://studio.example.invalid/","title":"<b>Fictional Studio</b>","description":"Paper kits"},{"url":"https://studio.example.invalid/about","title":"Duplicate origin"},{"url":"https://127.0.0.1/","title":"Local"},{"url":"https://other.example.invalid/?token=private","title":"Private"},{"url":"https://another.example.invalid/","title":"Another studio"}]}}""",
                "application/json"
            );
        });
        var result = await new BusinessSearch(() => "fictional-key", handler).SearchAsync(
            new("Fictional Studio Valley"),
            Token
        );
        Assert.Equal(2, result.Candidates.Length);
        Assert.Equal("Fictional Studio", result.Candidates[0].Title);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.search.brave.com", request.Host);
        Assert.Contains("count=5", request.Query);
        Assert.Equal(ToolRisk.Spend, WebTools.Search.Risk);
    }

    [Fact]
    public async Task MissingKeyAndRateLimitsDoNotCauseFallbackOrRetries()
    {
        var handler = new Pages(_ => new(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new BusinessSearch(() => null, handler).SearchAsync(new("Fictional Studio"), Token)
        );
        Assert.Empty(handler.Requests);
        var unreadable = await Assert.ThrowsAsync<ToolFailureException>(() =>
            new BusinessSearch(
                () => throw new InvalidOperationException("fictional-private-secret-location"),
                handler
            ).SearchAsync(new("Fictional Studio"), Token)
        );
        Assert.DoesNotContain("fictional-private-secret-location", unreadable.Message);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new BusinessSearch(() => "invalid\nkey", handler).SearchAsync(new("Fictional Studio"), Token)
        );
        Assert.Empty(handler.Requests);
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() =>
            new BusinessSearch(() => "fictional", handler).SearchAsync(new("Fictional Studio"), Token)
        );
        Assert.Contains("not retried", failure.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task InvalidAndOversizedSearchResponsesAreBoundedAndNeverExposeRawDiagnostics()
    {
        foreach (
            var response in new[] { "{\"web\":null}", "{\"web\":{\"results\":{}}}", new string('x', 256 * 1024 + 1) }
        )
        {
            var handler = new Pages(_ => Content(response, "application/json"));
            var error = await Assert.ThrowsAsync<ToolFailureException>(() =>
                new BusinessSearch(() => "fictional", handler).SearchAsync(new("Fictional Studio"), Token)
            );
            Assert.DoesNotContain(response, error.Message);
            Assert.Single(handler.Requests);
        }
    }
}
