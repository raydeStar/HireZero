using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Web;

public sealed class WebResearchTests
{
    private sealed class Pages(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<Uri> Seen = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Seen.Add(request.RequestUri!);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Text(string value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "text/plain") };

    [Fact]
    public void RulesUseTheSpecificAgentAndLongestPathWithoutBorrowingAnotherAgentsPermission()
    {
        var policy =
            "User-agent: *\nDisallow: /\n\nUser-agent: HireZero-source-reader\nDisallow: /private\nAllow: /private/public$\nDisallow: /*?token=*\nCrawl-delay: 3";
        Assert.Equal((true, 3d), WebReader.ResearchRules(policy, new("https://example.invalid/about")));
        Assert.False(WebReader.ResearchRules(policy, new("https://example.invalid/private/report")).Allowed);
        Assert.True(WebReader.ResearchRules(policy, new("https://example.invalid/private/public")).Allowed);
        Assert.False(WebReader.ResearchRules(policy, new("https://example.invalid/private/public/extra")).Allowed);
        Assert.False(WebReader.ResearchRules(policy, new("https://example.invalid/about?token=sample")).Allowed);
        Assert.True(
            WebReader.ResearchRules("User-agent: DifferentBot\nDisallow: /", new("https://example.invalid/")).Allowed
        );
        Assert.False(
            WebReader
                .ResearchRules("User-agent: *\nDisallow: /private", new("https://example.invalid/%70rivate"))
                .Allowed
        );
    }

    [Fact]
    public async Task RobotsExclusionOrUnavailablePolicyNeverReadsThePage()
    {
        foreach (
            var status in new[]
            {
                HttpStatusCode.OK,
                HttpStatusCode.TooManyRequests,
                HttpStatusCode.Forbidden,
                HttpStatusCode.ServiceUnavailable,
            }
        )
        {
            var pages = new Pages(_ => status == HttpStatusCode.OK ? Text("User-agent: *\nDisallow: /") : new(status));
            await Assert.ThrowsAsync<ToolFailureException>(() =>
                new WebReader(pages).ResearchAsync(
                    new("https://example.invalid/about", "https://example.invalid/"),
                    TestContext.Current.CancellationToken
                )
            );
            Assert.Equal("/robots.txt", Assert.Single(pages.Seen).AbsolutePath);
        }
    }

    [Fact]
    public async Task ResearchCannotFetchAnUnconfirmedOriginOrFollowItThroughARedirect()
    {
        var pages = new Pages(request =>
            request.RequestUri!.AbsolutePath == "/robots.txt"
                ? new(HttpStatusCode.NotFound)
                : new(HttpStatusCode.Found) { Headers = { Location = new("https://other.example.invalid/") } }
        );
        var reader = new WebReader(pages);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            reader.ResearchAsync(
                new("https://other.example.invalid/", "https://example.invalid/"),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Empty(pages.Seen);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            reader.ResearchAsync(
                new("https://example.invalid/", "https://example.invalid/"),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(2, pages.Seen.Count);
        Assert.All(pages.Seen, url => Assert.Equal("example.invalid", url.Host));
    }

    [Fact]
    public async Task PermittedEvidenceIsReturnedWithoutFollowingLinksOrExecutingInstructions()
    {
        var pages = new Pages(request =>
            request.RequestUri!.AbsolutePath == "/robots.txt"
                ? Text("User-agent: *\nAllow: /")
                : Text("Fictional paper kits for adults. See https://other.example.invalid/ for details.")
        );
        var page = await new WebReader(pages).ResearchAsync(
            new("https://example.invalid/about", "https://example.invalid/"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("https://example.invalid/about", page.Url);
        Assert.Contains("Fictional paper kits", page.Text);
        Assert.Equal(2, pages.Seen.Count);
    }
}
