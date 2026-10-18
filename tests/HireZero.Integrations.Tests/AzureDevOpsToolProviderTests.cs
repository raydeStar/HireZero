using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.AzureDevOps;
using HireZero.Tools.AzureDevOps;

// Azure DevOps for the company workspace: off until the owner lists organizations, then read-only reads of the owner's
// open pull requests, only for listed organizations. Fake REST API and token; the organization is fictional.
public sealed class AzureDevOpsToolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ado-tools-" + Guid.NewGuid().ToString("N"));

    public AzureDevOpsToolProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static readonly EmployeeManifest Manifest = new(
        "ado-test",
        "Azure DevOps test",
        "Fictional manifest declaring the Azure DevOps tools.",
        "1.0.0",
        "Fictional instructions.",
        [new("chat", "Conversation", "Fictional.")],
        new("Fictional", "Azure DevOps tester", "Fictional.", [new("purpose", "Fictional purpose.")], "Fictional.")
    )
    {
        Tools =
        [
            new(AzureDevOpsTools.Status.Id, ToolRisk.Read, "Status."),
            new(AzureDevOpsTools.Attention.Id, ToolRisk.Read, "Attention."),
        ],
    };

    private (IToolbox Tools, AzureDevOpsChecks.FakeDevOps Fake, AzureDevOpsToolProvider Provider) Create()
    {
        var fake = new AzureDevOpsChecks.FakeDevOps();
        var client = new AzureDevOpsClient(fake)
        {
            TokenSource = _ => Task.FromResult(("fictional-token", DateTimeOffset.UtcNow.AddHours(1))),
        };
        var provider = new AzureDevOpsToolProvider(_root, client);
        var registry = new ToolRegistry(new MemoryToolAudit());
        provider.Register(registry);
        return (registry.For(Manifest), fake, provider);
    }

    private void Configure(object settings) =>
        File.WriteAllText(Path.Combine(_root, "azure-devops.json"), JsonSerializer.Serialize(settings));

    [Fact]
    public async Task NothingIsReadUntilOrganizationsAreListed()
    {
        var (tools, fake, provider) = Create();
        Assert.Equal("List your organizations in azure-devops.json in the data root.", provider.Status().Reason);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AzureDevOpsTools.Attention, new(null, 10), TestContext.Current.CancellationToken)
        );
        Configure(new { organizations = new[] { "bad name!" } });
        Assert.False(provider.Status().Active);
        Configure(new { organizations = new[] { "north" }, az = Path.Combine(_root, "missing", "az.cmd") });
        Assert.False(provider.Status().Active);
        Assert.Empty(fake.Urls);
    }

    [Fact]
    public async Task OnlyListedOrganizationsAreReadForTheOwnersOpenPullRequests()
    {
        var (tools, fake, provider) = Create();
        Configure(new { organizations = new[] { "north" } });
        Assert.True(provider.Status().Active);
        var token = TestContext.Current.CancellationToken;
        var pulls = await tools.InvokeAsync(AzureDevOpsTools.Attention, new(null, 10), token);
        // Only the owner's pull requests are asked for (the fake API, unlike the real one, ignores that filter).
        Assert.Contains(
            fake.Urls,
            url => url.Contains("searchCriteria.status=active") && url.Contains("searchCriteria.creatorId=me")
        );
        Assert.Contains(pulls, p => p.Id == 8 && p.MergeStatus == "conflicts");
        var blocked = pulls.Single(p => p.Id == 7);
        Assert.Contains("Build", blocked.Failing);
        Assert.NotEmpty(blocked.Problems);
        Assert.All(fake.Urls, url => Assert.StartsWith("https://dev.azure.com/north/", url));
        Assert.All(fake.Urls, url => Assert.DoesNotContain("searchCriteria.status=completed", url));
        Assert.Equal("Bearer fictional-token", fake.LastAuthorization);

        var count = fake.Urls.Count;
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AzureDevOpsTools.Attention, new("south", 10), token)
        );
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            tools.InvokeAsync(AzureDevOpsTools.Attention, new(null, 100), token)
        );
        Assert.Equal(count, fake.Urls.Count);
    }
}
