using HireZero.Core;
using HireZero.Roles.Developer;

// The owner's checkout context comes from git.read commands run only in that project's own folder.
public sealed class RepositoryContextTests : IDisposable
{
    private sealed class Client : GitHubReviewClient
    {
        public List<string> Calls { get; } = [];

        protected override Task<string> CommandAsync(
            string executable,
            string? root,
            string[] args,
            string? input,
            CancellationToken token
        )
        {
            Calls.Add(executable + "|" + root + "|" + args[0]);
            return Task.FromResult(
                args[0] switch
                {
                    "rev-parse" => "main\n",
                    "log" => "2026-10-05 b1ca9c9 Patch Next.js to 16.3.6",
                    "status" => " M src/app.cs\n?? notes.md\n",
                    _ => "",
                }
            );
        }
    }

    private readonly string _root = Directory.CreateTempSubdirectory("repository-context-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static ProjectRef Project(string? root) =>
        new("p", "Fixture", "owner/fixture", root, [], "2026-10-05T00:00:00Z", 1);

    [Fact]
    public async Task OwnerSeesBranchCommitsAndChangesFromTheProjectCheckout()
    {
        var client = new Client();
        var sections = await new RepositoryContext(client).ContributeAsync(
            Project(_root),
            Audience.Owner,
            TestContext.Current.CancellationToken
        );
        var section = Assert.Single(sections);
        Assert.Equal("Repository (local checkout)", section.Title);
        Assert.Contains("Current branch: main; 2 uncommitted change(s)", section.Body);
        Assert.Contains("b1ca9c9", section.Body);
        Assert.All(client.Calls, call => Assert.StartsWith("git|" + _root + "|", call));
    }

    [Fact]
    public async Task VisitorsAndProjectsWithoutACheckoutGetNothing()
    {
        var client = new Client();
        var token = TestContext.Current.CancellationToken;
        Assert.Empty(await new RepositoryContext(client).ContributeAsync(Project(_root), Audience.Visitor, token));
        Assert.Empty(await new RepositoryContext(client).ContributeAsync(Project(null), Audience.Owner, token));
        Assert.Empty(client.Calls);
    }
}
