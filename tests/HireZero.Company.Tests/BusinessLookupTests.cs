using System.Text.Json;
using HireZero.Core;
using HireZero.Tools.Web;
using Microsoft.Data.Sqlite;

public sealed partial class BusinessLookupTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("business-lookup-");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company);

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static CompanyBusinessIdentity Identity(CompanyStore store) =>
        CompanyJson.Decode<CompanyBusinessIdentity>(
            store.Apply(
                C("business.identify") with
                {
                    Body = CompanyJson.Encode(
                        new CompanyBusinessInput(
                            "Fictional Studio",
                            "https://studio.example.invalid/",
                            "Private goal excluded from search",
                            "Fictional Valley",
                            "Crafts"
                        )
                    ),
                }
            )
        );

    private static CompanyCommand Start(CompanyBusinessIdentity business, string kind = "search") =>
        C("business.lookup.start") with
        {
            ExpectedVersion = business.Version,
            Digest = business.Digest,
            Body = kind,
            Decision = "authorize-one-lookup",
        };

    private static ToolRegistry Tools(CompanyStore store, Action request)
    {
        var tools = new ToolRegistry(store, [new CompanyBusinessResearch.SearchAuthority(store)]);
        tools.Register(
            WebTools.Search,
            (_, _) =>
            {
                request();
                return Task.FromResult(
                    new WebSearchResult([new("https://studio.example.invalid/", "Fictional Studio", "Craft supplies")])
                );
            }
        );
        tools.Register(
            WebTools.Discover,
            (_, _) =>
                Task.FromResult(
                    new WebDiscovery(
                        [
                            new(
                                "https://studio.example.invalid/",
                                "Fictional Studio",
                                "We sell fictional craft supplies.",
                                false
                            ),
                        ],
                        []
                    )
                )
        );
        return tools;
    }

    [Fact]
    public async Task SearchIsSingleUseScopedAndNeverImportsOrConfirmsItsSuggestions()
    {
        var store = Store();
        var identity = Identity(store);
        var calls = 0;
        var tools = Tools(store, () => calls++);
        var command = Start(identity);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            CompanyBusinessResearch.RunLookupAsync(
                command with
                {
                    Decision = null,
                },
                Owner,
                store,
                tools,
                TestContext.Current.CancellationToken
            )
        );
        var receipt = await CompanyBusinessResearch.RunLookupAsync(
            command,
            Owner,
            store,
            tools,
            TestContext.Current.CancellationToken
        );
        var initial = CompanyJson.Decode<CompanyBusinessLookup>(receipt);
        var completed = store.BusinessLookup(initial.Id);
        Assert.Equal("completed", completed.State);
        Assert.True(completed.Dispatched);
        Assert.Single(completed.Candidates!);
        Assert.Equal("unconfirmed", store.BusinessIdentity()!.State);
        Assert.DoesNotContain("Private goal", completed.Query);
        await CompanyBusinessResearch.RunLookupAsync(
            command,
            Owner,
            store,
            tools,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, calls);
        Assert.False(store.ConsumeBusinessSearch(initial.Id, initial.Query));
        using var dashboard = JsonDocument.Parse(CompanyJson.Encode(store.Dashboard()));
        foreach (var key in new[] { "businessSources", "businessProfiles", "knowledge", "employees" })
            Assert.Equal(0, dashboard.RootElement.GetProperty(key).GetArrayLength());
        Assert.Equal(CompanyJson.Encode(completed), CompanyJson.Encode(Store().BusinessLookup(initial.Id)));
    }

    [Fact]
    public async Task ConfirmedDiscoverySavesEvidenceButNoApprovedKnowledge()
    {
        var store = Store();
        var identity = Identity(store);
        var tools = Tools(store, () => throw new Exception("Search was not invited to this dinner."));
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            CompanyBusinessResearch.RunLookupAsync(
                Start(identity, "discovery"),
                Owner,
                store,
                tools,
                TestContext.Current.CancellationToken
            )
        );
        identity = CompanyJson.Decode<CompanyBusinessIdentity>(
            store.Apply(C("business.confirm") with { ExpectedVersion = identity.Version, Digest = identity.Digest })
        );
        var initial = CompanyJson.Decode<CompanyBusinessLookup>(
            await CompanyBusinessResearch.RunLookupAsync(
                Start(identity, "discovery"),
                Owner,
                store,
                tools,
                TestContext.Current.CancellationToken
            )
        );
        var result = store.BusinessLookup(initial.Id);
        var source = store.BusinessSource(Assert.Single(result.SourceIds!));
        Assert.Equal(identity.Version, source.BusinessVersion);
        Assert.Contains("craft supplies", source.Text);
        Assert.Empty(result.Candidates!);
    }

    [Fact]
    public void AuthorityRejectsAnotherCompanyChangedQueryStaleIdentityAndReplay()
    {
        var store = Store();
        var identity = Identity(store);
        var run = CompanyJson.Decode<CompanyBusinessLookup>(store.Apply(Start(identity)));
        var other = Store("fictional-second");
        Identity(other);
        Assert.Throws<WorkspaceException>(() => other.BusinessLookup(run.Id));
        Assert.False(other.ConsumeBusinessSearch(run.Id, run.Query));
        Assert.False(store.ConsumeBusinessSearch(run.Id, run.Query + " changed"));
        Assert.True(store.ConsumeBusinessSearch(run.Id, run.Query));
        Assert.False(store.ConsumeBusinessSearch(run.Id, run.Query));
        store.Apply(
            C("business.identify") with
            {
                ExpectedVersion = identity.Version,
                Body = CompanyJson.Encode(new CompanyBusinessInput("Other fictional studio", null, "Other goal")),
            }
        );
        var result = store.CompleteBusinessLookup(
            run.Id,
            Owner,
            [],
            [new("https://studio.example.invalid/", "Old studio", "Old suggestion")],
            []
        );
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Candidates!);
    }

    [Fact]
    public async Task RestartAndUnknownProviderResultsStayFencedWithoutAnotherCall()
    {
        var store = Store();
        var identity = Identity(store);
        var calls = 0;
        var command = Start(identity);
        var run = CompanyJson.Decode<CompanyBusinessLookup>(store.Apply(command));
        Assert.True(store.ConsumeBusinessSearch(run.Id, run.Query));
        var reopened = Store();
        reopened.Recover();
        Assert.Equal("unknown", reopened.BusinessLookup(run.Id).State);
        await CompanyBusinessResearch.RunLookupAsync(
            command,
            Owner,
            reopened,
            Tools(reopened, () => calls++),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, calls);
        var failed = new ToolRegistry(reopened, [new CompanyBusinessResearch.SearchAuthority(reopened)]);
        failed.Register<WebSearchRequest, WebSearchResult>(
            WebTools.Search,
            (_, _) =>
            {
                calls++;
                throw new HttpRequestException("Fictional unconfirmed response");
            }
        );
        command = Start(identity);
        var receipt = CompanyJson.Decode<CompanyBusinessLookup>(
            await CompanyBusinessResearch.RunLookupAsync(
                command,
                Owner,
                reopened,
                failed,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal("unknown", reopened.BusinessLookup(receipt.Id).State);
        await CompanyBusinessResearch.RunLookupAsync(
            command,
            Owner,
            reopened,
            failed,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RevokedOwnerApprovalCannotStartASearch()
    {
        var store = Store();
        var identity = Identity(store);
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var member = store.SignIn(
            "fictional-second-owner",
            "second-owner@example.invalid",
            true,
            "Fictional Second Owner"
        )!;
        var run = CompanyJson.Decode<CompanyBusinessLookup>(store.Apply(Start(identity), member.Subject, out _));
        var saved = store.Members().Single(m => m.Id == member.Subject);
        store.Apply(C("member.revoke") with { Id = saved.Id, ExpectedVersion = saved.Version });
        Assert.False(store.ConsumeBusinessSearch(run.Id, run.Query));
        var result = store.CompleteBusinessLookup(
            run.Id,
            member.Subject,
            [],
            [new("https://studio.example.invalid/", "Fictional Studio", "Late result")],
            []
        );
        Assert.Equal("withheld", result.State);
        Assert.Empty(result.Candidates!);
    }

    [Fact]
    public void QuotasAndOwnerAuthorityAreEnforcedBeforeToolExecution()
    {
        var store = Store();
        var identity = Identity(store);
        store.Apply(C("member.invite") with { Body = "approver@example.invalid", Decision = "approver" });
        var member = store.SignIn("fictional-approver", "approver@example.invalid", true, "Fictional Approver")!;
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => store.Apply(Start(identity), member.Subject, out _)).Status
        );
        for (var i = 0; i < 3; i++)
        {
            var run = CompanyJson.Decode<CompanyBusinessLookup>(store.Apply(Start(identity)));
            Assert.Throws<WorkspaceException>(() => store.Apply(Start(identity)));
            store.CompleteBusinessLookup(run.Id, Owner, [], [], ["Fictional no results"]);
        }
        Assert.Equal(429, Assert.Throws<WorkspaceException>(() => store.Apply(Start(identity))).Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
