using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Web;
using HireZero.Tools.Web;

// The web.read tool (docs/adr/0041-context-drafted-from-sources.md): https and public addresses only, bounded
// redirects and size, readable text from HTML. The pages are fictional and served by a fake handler.
public sealed class WebReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Pages(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Html(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to, UriKind.RelativeOrAbsolute) } };

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("93.184.215.14", true)]
    [InlineData("2606:4700::1111", true)]
    public void OnlyPublicAddressesAreReachable(string address, bool reachable) =>
        Assert.Equal(reachable, WebReader.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://example.invalid/")]
    [InlineData("ftp://example.invalid/")]
    [InlineData("https://user:secret@example.invalid/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://192.168.0.5/admin")]
    [InlineData("not a url")]
    public async Task UnsafeUrlsAreRefusedBeforeAnyRequest(string url)
    {
        var pages = new Pages(_ => Html("never"));
        var error = await Assert.ThrowsAsync<ToolFailureException>(() =>
            new WebReader(pages).ReadAsync(new(url), Token)
        );
        Assert.Equal(422, error.Status);
        Assert.Empty(pages.Urls);
    }

    [Fact]
    public async Task APageIsReadAsTitleAndReadableText()
    {
        var pages = new Pages(_ =>
            Html(
                "<html><head><title>Fictional &amp; Co</title><style>body{}</style><script>steal()</script></head>"
                    + "<body><h1>We make paper cranes</h1><p>For adult   hobbyists.</p><!-- hidden --></body></html>"
            )
        );
        var page = await new WebReader(pages).ReadAsync(new("https://example.invalid/"), Token);
        Assert.Equal("Fictional & Co", page.Title);
        Assert.Equal("Fictional & Co\nWe make paper cranes\nFor adult hobbyists.", page.Text);
        Assert.DoesNotContain("steal", page.Text);
        Assert.False(page.Clipped);
    }

    // Navigation, footers and sidebars are dropped, and a <main> with real text is preferred over the rest of the page;
    // a thin <main> falls back to the whole page.
    [Fact]
    public void TextPrefersTheMainContentAndDropsSiteChrome()
    {
        var story = string.Concat(Enumerable.Repeat("Paper cranes fold from one square sheet. ", 8));
        var text = WebReader.Text(
            "<body><nav><a>Home</a><a>Pricing</a></nav><div>Sign up today</div>"
                + $"<main><h1>How cranes fold</h1><p>{story}</p><aside>Related posts</aside></main>"
                + "<footer>Copyright Fictional Co</footer></body>"
        );
        Assert.StartsWith("How cranes fold\nPaper cranes fold", text);
        Assert.DoesNotContain("Pricing", text);
        Assert.DoesNotContain("Sign up today", text);
        Assert.DoesNotContain("Related posts", text);
        Assert.DoesNotContain("Copyright", text);

        var thin = WebReader.Text("<body><nav>Menu</nav><p>Fictional intro</p><main><p>Short</p></main></body>");
        Assert.Equal("Fictional intro\nShort", thin);
    }

    [Fact]
    public async Task RedirectsAreFollowedOnlyToHttpsAndAtMostThreeTimes()
    {
        var hops = 0;
        var toHttps = new Pages(request =>
            request.RequestUri!.AbsolutePath == "/old" ? Redirect("/new") : Html("<p>Moved</p>")
        );
        var page = await new WebReader(toHttps).ReadAsync(new("https://example.invalid/old"), Token);
        Assert.Equal(("https://example.invalid/new", "Moved"), (page.Url, page.Text));

        var toHttp = new Pages(_ => Redirect("http://example.invalid/plain"));
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new WebReader(toHttp).ReadAsync(new("https://example.invalid/"), Token)
        );
        var toLoopback = new Pages(_ => Redirect("https://127.0.0.1/"));
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new WebReader(toLoopback).ReadAsync(new("https://example.invalid/"), Token)
        );
        var loop = new Pages(_ => Redirect("https://example.invalid/again/" + hops++));
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new WebReader(loop).ReadAsync(new("https://example.invalid/"), Token)
        );
        Assert.Equal(WebReader.MaxRedirects + 1, loop.Urls.Count);
    }

    [Fact]
    public async Task OnlyTextIsReadAndLongTextIsClipped()
    {
        var binary = new Pages(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new WebReader(binary).ReadAsync(new("https://example.invalid/file"), Token)
        );
        var huge = new Pages(_ => Html("<p>" + new string('a', WebReader.MaxBytes + 10) + "</p>"));
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            new WebReader(huge).ReadAsync(new("https://example.invalid/"), Token)
        );
        var longText = new Pages(_ => Html("<p>" + new string('b', WebTools.MaxTextCharacters + 500) + "</p>"));
        var page = await new WebReader(longText).ReadAsync(new("https://example.invalid/"), Token);
        Assert.Equal((WebTools.MaxTextCharacters, true), (page.Text.Length, page.Clipped));
    }
}
