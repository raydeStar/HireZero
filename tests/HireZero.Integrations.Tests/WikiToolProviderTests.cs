using System.Net;
using HireZero.Core;
using HireZero.Integrations.Wiki;
using HireZero.Tools.Wiki;

// The wiki.* integration adds the session's request token itself when the caller supplies none, so an approved call's
// digest does not depend on a per-session value; a session without a token sends nothing.
public sealed class WikiToolProviderTests
{
    private sealed class Handler(string? token) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Csrf)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var csrf = request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null;
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery.TrimStart('/'), csrf));
            var body =
                request.RequestUri.AbsolutePath == "/api/session"
                    ? token is null
                        ? "{}"
                        : "{\"csrf\":\"" + token + "\"}"
                    : "{\"id\":\"fictional\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class Clients(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://127.0.0.1:5094/") };
    }

    private sealed class Allow : IToolAuthorityVerifier
    {
        public string Kind => "fictional";

        public bool Authorizes(ToolInvocation invocation) => true;
    }

    private static readonly EmployeeManifest Manifest = new(
        "wiki-test",
        "Wiki test",
        "Fictional manifest declaring the Wiki tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Wiki tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(WikiTools.Read.Id, ToolRisk.Read, "Read."),
            new(WikiTools.Contribute.Id, ToolRisk.Write, "Propose."),
        ],
    };

    private static (IToolbox Tools, Handler Handler) Wiki(string? token)
    {
        var handler = new Handler(token);
        var registry = new ToolRegistry(new MemoryToolAudit(), [new Allow()]);
        new WikiToolProvider(new Clients(handler)).Register(registry);
        return (registry.For(Manifest), handler);
    }

    [Fact]
    public async Task APostWithoutATokenUsesTheSessionToken()
    {
        var (tools, handler) = Wiki("session-token");
        var reply = await tools.InvokeAsync(
            WikiTools.Contribute,
            new WikiCall("contribution", "POST", "api/proposals", "{}"),
            TestContext.Current.CancellationToken,
            new("fictional", "allow")
        );
        Assert.True(reply.Success);
        Assert.Equal(
            [("GET", "api/session", (string?)null), ("POST", "api/proposals", "session-token")],
            handler.Requests
        );
    }

    [Fact]
    public async Task ACallerTokenIsSentAsIs()
    {
        var (tools, handler) = Wiki("session-token");
        await tools.InvokeAsync(
            WikiTools.Contribute,
            new WikiCall("contribution", "POST", "api/proposals", "{}", "caller-token"),
            TestContext.Current.CancellationToken,
            new("fictional", "allow")
        );
        Assert.Equal([("POST", "api/proposals", (string?)"caller-token")], handler.Requests);
    }

    [Fact]
    public async Task ASessionWithoutATokenSendsNothing()
    {
        var (tools, handler) = Wiki(null);
        var refused = await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(
                WikiTools.Contribute,
                new WikiCall("contribution", "POST", "api/proposals", "{}"),
                TestContext.Current.CancellationToken,
                new("fictional", "allow")
            )
        );
        Assert.Equal("refused", refused.Kind);
        Assert.DoesNotContain(handler.Requests, r => r.Method == "POST");
    }
}
