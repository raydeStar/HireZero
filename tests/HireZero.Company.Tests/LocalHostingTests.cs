using System.Net;
using Microsoft.AspNetCore.Http;

// The local request boundary (owner walkthrough, 2026-10-19): localhost on the workspace's own port is redirected to
// the loopback address it is served on, a page only; everything else stays refused, now with a message.
public sealed class LocalHostingTests
{
    private const string Expected = "127.0.0.1:5065";

    [Fact]
    public void AConfiguredLocalListenerCannotExposeTheOwnerOnTheNetwork()
    {
        var root = Path.Combine(Path.GetTempPath(), "hirezero-listen-" + Guid.NewGuid().ToString("N"));
        var values = new Dictionary<string, string>
        {
            ["HIREZERO_DATA_ROOT"] = root,
            ["HIREZERO_LISTEN"] = "http://0.0.0.0:5099",
        };
        var settings = CompanySettings.Load(name => values.GetValueOrDefault(name));
        var error = Assert.Throws<InvalidOperationException>(() =>
            CompanyWorkspace.TryCreate(["--company-local"], "owner@example.invalid", true, root, settings: settings)
        );
        Assert.Contains("loopback", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(root));
    }

    private static DefaultHttpContext Request(
        string host,
        string path = "/",
        string method = "GET",
        string? remote = null
    )
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote ?? "127.0.0.1");
        context.Request.Method = method;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?view=departments");
        return context;
    }

    [Fact]
    public void LocalhostOnTheSamePortIsRedirectedAndNothingElseIs()
    {
        Assert.Equal(
            "http://127.0.0.1:5065/?view=departments",
            LocalHosting.LocalhostRedirect(Request("localhost:5065"), Expected)
        );
        Assert.Equal(
            "http://127.0.0.1:5065/?view=departments",
            LocalHosting.LocalhostRedirect(Request("LOCALHOST:5065", remote: "::1"), Expected)
        );
        Assert.Null(LocalHosting.LocalhostRedirect(Request("localhost:5065", "/api/company"), Expected));
        Assert.Null(LocalHosting.LocalhostRedirect(Request("localhost:5065", method: "POST"), Expected));
        Assert.Null(LocalHosting.LocalhostRedirect(Request("localhost:5066"), Expected));
        Assert.Null(LocalHosting.LocalhostRedirect(Request("attacker.example:5065"), Expected));
        Assert.Null(LocalHosting.LocalhostRedirect(Request("localhost:5065", remote: "192.0.2.10"), Expected));
        var proxied = Request("localhost:5065");
        proxied.Request.Headers["X-Forwarded-For"] = "192.0.2.10";
        Assert.Null(LocalHosting.LocalhostRedirect(proxied, Expected));
        Assert.False(LocalHosting.IsDirectLocalRequest(Request("localhost:5065"), Expected));
        Assert.True(LocalHosting.IsDirectLocalRequest(Request(Expected), Expected));
    }

    [Fact]
    public async Task ARefusalSaysWhereTheWorkspaceIs()
    {
        var context = Request("attacker.example:5065");
        context.Response.Body = new MemoryStream();
        await LocalHosting.RefuseAsync(context, Expected);
        Assert.Equal(403, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains(
            "http://127.0.0.1:5065/",
            await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken)
        );
    }
}
