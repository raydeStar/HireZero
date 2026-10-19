using HireZero.Core;

// Core reads GitHub-style origins itself; other hosts' formats come from integrations (docs/adr/0009). Without such
// a format, another host's URL is not mistaken for a GitHub origin.
public sealed class GitRemoteTests
{
    [Fact]
    public void GitHubStyleOriginsAreReadWithoutAnyIntegration()
    {
        var origin = GitRemote.Parse("https://github.com/Northwind/PartnerQuoteDesk.git");
        Assert.Equal(new GitOrigin(GitOrigin.GitHub, "Northwind", null, "PartnerQuoteDesk"), origin);
        Assert.Equal("Northwind/PartnerQuoteDesk", origin!.Slug);
        Assert.False(origin.AbbreviatedOrganization);
    }

    [Fact]
    public void AnUnregisteredHostsGitPathIsNotAGitHubOrigin() =>
        Assert.Null(GitRemote.Parse("https://example.invalid/north/Platform/_git/Northwind.API"));
}
