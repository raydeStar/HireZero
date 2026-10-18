using System.Net;
using HireZero.Core;
using HireZero.Integrations.Microsoft365;
using HireZero.Tools.Microsoft365;
using Microsoft.AspNetCore.DataProtection;

// The m365.* tools for the company workspace: reads only when configured and signed in, a status that says why not,
// and owner-started reads that never move the legacy sync cursor. Graph is faked; every address is fictional.
public sealed class Microsoft365ToolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m365-tools-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    public Microsoft365ToolProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private sealed record Kit(MicrosoftStore Store, MicrosoftConnector Connector, FakeMicrosoft Graph, IToolbox Tools);

    private static readonly EmployeeManifest Manifest = new(
        "m365-test",
        "Inbox test",
        "Fictional manifest declaring the Microsoft 365 tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Inbox tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(Microsoft365Tools.Status.Id, ToolRisk.Read, "Status."),
            new(Microsoft365Tools.ReadMail.Id, ToolRisk.Read, "Mail."),
            new(Microsoft365Tools.ReadTeams.Id, ToolRisk.Read, "Teams."),
        ],
    };

    private Kit Create()
    {
        var store = new MicrosoftStore(_root);
        var graph = new FakeMicrosoft();
        var connector = new MicrosoftConnector(
            store,
            graph,
            new EphemeralDataProtectionProvider(),
            MicrosoftOptions.Company
        )
        {
            Clock = () => _now,
        };
        var registry = new ToolRegistry(new MemoryToolAudit());
        new Microsoft365ToolProvider(store, connector).Register(registry);
        return new(store, connector, graph, registry.For(Manifest));
    }

    private static async Task SignIn(Kit kit)
    {
        kit.Store.SaveSettings(new("00000000-0000-0000-0000-000000000001"));
        var url = new Uri(kit.Connector.Begin());
        var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"]!;
        Assert.Equal(
            MicrosoftOptions.Company.RedirectUri,
            System.Web.HttpUtility.ParseQueryString(url.Query)["redirect_uri"]
        );
        await kit.Connector.CompleteAsync("code-1", state, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheStatusSaysWhyItIsNotActiveAndReadsAreRefused()
    {
        var kit = Create();
        var token = TestContext.Current.CancellationToken;
        var status = await kit.Tools.InvokeAsync(Microsoft365Tools.Status, new(), token);
        Assert.Equal((false, "Add your Microsoft app (client) ID."), (status.Active, status.Reason));
        kit.Store.SaveSettings(new("00000000-0000-0000-0000-000000000001"));
        Assert.Equal(
            "Sign in to Microsoft 365.",
            (await kit.Tools.InvokeAsync(Microsoft365Tools.Status, new(), token)).Reason
        );
        var refused = await Assert.ThrowsAsync<ToolFailureException>(() =>
            kit.Tools.InvokeAsync(Microsoft365Tools.ReadMail, new(3, 10), token)
        );
        Assert.Equal("refused", refused.Kind);
        Assert.DoesNotContain(kit.Graph.Requests, r => r.Contains("graph.microsoft.com"));
    }

    [Fact]
    public async Task AnOwnerStartedReadDoesNotMoveTheSyncCursor()
    {
        var kit = Create();
        await SignIn(kit);
        var token = TestContext.Current.CancellationToken;
        Assert.True((await kit.Tools.InvokeAsync(Microsoft365Tools.Status, new(), token)).Active);
        kit.Graph.Mail.Enqueue(
            (
                HttpStatusCode.OK,
                """{"value":[{"id":"m-1","conversationId":"c-1","receivedDateTime":"2026-10-18T08:30:00Z","subject":"Fictional quote question","bodyPreview":"Can you check the rounding?","from":{"emailAddress":{"name":"Riley","address":"riley@example.invalid"}}}]}"""
            )
        );
        var messages = await kit.Tools.InvokeAsync(Microsoft365Tools.ReadMail, new(3, 10), token);
        var message = Assert.Single(messages);
        Assert.Equal(("outlook", "m-1", "Fictional quote question"), (message.Source, message.Id, message.Subject));
        var query = kit.Graph.Requests.Single(r => r.Contains("/mailFolders/inbox/messages?"));
        Assert.Contains("receivedDateTime+ge+2026-10-18T06:00:00Z", query);
        Assert.Contains("$top=10", query);
        Assert.DoesNotContain(kit.Graph.Requests, r => r.Contains("/delta"));
        Assert.Null(kit.Store.Account()!.MailCursor);
        Assert.Null(kit.Store.Account()!.LastMailSync);
    }

    [Fact]
    public async Task ReadBoundsAndSwitchesAreEnforced()
    {
        var kit = Create();
        await SignIn(kit);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            kit.Tools.InvokeAsync(Microsoft365Tools.ReadMail, new(100, 10), token)
        );
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            kit.Tools.InvokeAsync(Microsoft365Tools.ReadMail, new(3, 500), token)
        );
        kit.Store.SaveSettings(kit.Store.Settings() with { Mail = false });
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            kit.Tools.InvokeAsync(Microsoft365Tools.ReadMail, new(3, 10), token)
        );
        Assert.DoesNotContain(kit.Graph.Requests, r => r.Contains("/mailFolders/"));
    }
}
