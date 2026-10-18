using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.AzureDevOps;

// Azure DevOps against a fake REST API and a fake token: no network, no az sign-in.
public static class AzureDevOpsChecks
{
    internal sealed class FakeDevOps : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Urls = [];
        public string? LastAuthorization;
        public bool SignInPage;

        public HttpClient CreateClient(string name) => new(this, false);

        static string Pr(
            int id,
            string title,
            string authorId,
            string merge = "succeeded",
            bool draft = false,
            int[]? votes = null,
            string? closed = null
        ) =>
            JsonSerializer.Serialize(
                new
                {
                    pullRequestId = id,
                    title,
                    isDraft = draft,
                    mergeStatus = merge,
                    sourceRefName = "refs/heads/fix/" + id,
                    targetRefName = "refs/heads/dev",
                    createdBy = new { displayName = authorId == "me" ? "Morgan Lee" : "Pat Lee", id = authorId },
                    creationDate = "2026-10-01T10:00:00Z",
                    closedDate = closed,
                    reviewers = (votes ?? []).Select(vote => new { vote }),
                    repository = new
                    {
                        id = "repo-1",
                        name = "Northwind.API",
                        project = new { id = "proj-1", name = "Northwind.API" },
                    },
                }
            );

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            LastAuthorization = request.Headers.Authorization?.ToString();
            if (SignInPage)
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation)
                    {
                        Content = new StringContent("<html>sign in</html>", Encoding.UTF8, "text/html"),
                    }
                );
            string body;
            if (url.Contains("/connectionData"))
                body = """{"authenticatedUser":{"id":"me","providerDisplayName":"Morgan Lee"}}""";
            else if (url.Contains("/policy/evaluations"))
                body = url.Contains("%2F7")
                    ? """{"value":[{"status":"rejected","configuration":{"isBlocking":true,"type":{"displayName":"Build"}}},{"status":"approved","configuration":{"isBlocking":true,"type":{"displayName":"Minimum number of reviewers"}}},{"status":"rejected","configuration":{"isBlocking":false,"type":{"displayName":"Optional scan"}}}]}"""
                    : """{"value":[]}""";
            else if (url.Contains("/threads"))
                body = url.Contains("/pullRequests/7/")
                    ? """{"value":[{"status":"active","comments":[{"commentType":"text"}]},{"status":"active","comments":[{"commentType":"system"}]},{"status":"fixed","comments":[{"commentType":"text"}]}]}"""
                    : """{"value":[]}""";
            else if (url.Contains("searchCriteria.status=completed"))
                body =
                    "{\"value\":["
                    + Pr(5, "Earlier fix", "me", closed: "2026-10-02T12:00:00Z")
                    + ","
                    + Pr(4, "Someone else", "other", closed: "2026-10-01T12:00:00Z")
                    + "]}";
            else if (url.Contains("searchCriteria.status=active"))
                body =
                    "{\"value\":["
                    + Pr(7, "Bound AssetChat batches", "me", votes: [-5, 0])
                    + ","
                    + Pr(8, "Draft idea", "me", draft: true, merge: "conflicts")
                    + ","
                    + Pr(9, "Pat's change", "other", votes: [10])
                    + "]}";
            else
                throw new InvalidOperationException("Unexpected fake Azure DevOps request: " + url);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        GitRemote.Register(new AzureDevOpsOriginFormat());
        check(
            GitRemote.Parse("https://north@dev.azure.com/north/North%20DevOps%20Platform/_git/Northwind.MyNorthwind")
                is {
                    Host: AzureDevOpsOriginFormat.Host,
                    Organization: "north",
                    Project: "North DevOps Platform",
                    Name: "Northwind.MyNorthwind"
                } origin
                && origin.Slug == "North DevOps Platform/Northwind.MyNorthwind",
            "Azure DevOps remotes give organization, decoded project and repository, without the user name."
        );
        check(
            GitRemote.Parse("git@ssh.dev.azure.com:v3/north/Streamline/Streamline")?.Project == "Streamline"
                && GitRemote
                    .Parse("https://north.visualstudio.com/DefaultCollection/Automation/_git/Automation")
                    ?.Organization == "north"
                && GitRemote.Parse("https://github.com/Northwind/PartnerQuoteDesk.git")
                    is { Host: GitOrigin.GitHub, Organization: "Northwind", Name: "PartnerQuoteDesk" }
                && GitRemote.Parse("https://dev.azure.com/north/Bad%2FProject/_git/repo") is null,
            "SSH and legacy remotes parse; GitHub stays owner/name; encoded path separators are refused."
        );

        var fake = new FakeDevOps();
        var tokens = 0;
        var client = new AzureDevOpsClient(fake)
        {
            TokenSource = _ =>
            {
                tokens++;
                return Task.FromResult(("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
            },
        };
        var mine = await client.PullRequestsAsync(
            "north",
            "active",
            25,
            default,
            creatorId: (await client.MeAsync("north", default)).Id
        );
        check(
            mine.Length == 3
                && mine[0] is { Id: 7, Project: "Northwind.API", Source: "fix/7", Target: "dev", AuthorId: "me" }
                && mine[0].Votes.SequenceEqual([-5, 0])
                && mine[0].Url == "https://dev.azure.com/north/Northwind.API/_git/Northwind.API/pullrequest/7"
                && fake.Urls.Last().Contains("searchCriteria.creatorId=me")
                && fake.LastAuthorization == "Bearer fake-token"
                && tokens == 1,
            "PRs list org-wide by creator with the cached token."
        );
        var attention = await client.AttentionAsync(mine[0], default);
        check(
            attention.FailingPolicies.SequenceEqual(["Build"])
                && attention.OpenThreads == 1
                && attention.Problems.Contains("failing: Build")
                && attention.Problems.Contains("a reviewer is waiting for you")
                && attention.Problems.Contains("1 open comment thread"),
            "Attention: failing blocking policies (optional ones ignored), open human comment threads, and blocking votes."
        );
        check(
            (await client.AttentionAsync(mine[1], default)).Problems.SequenceEqual(["merge conflicts"])
                && (await client.AttentionAsync(mine[2], default)).Problems.Length == 0,
            "Merge conflicts are a problem; an approved PR with nothing failing isn't."
        );
        fake.SignInPage = true;
        try
        {
            await client.GetAsync("north", "_apis/projects", default);
            check(false, "A sign-in page is reported as a sign-in problem.");
        }
        catch (WorkspaceException error)
        {
            check(error.Message.Contains("az login"), "A sign-in page is reported as a sign-in problem.");
        }
        fake.SignInPage = false;
        try
        {
            await client.GetAsync("north/evil", "_apis/projects", default);
            check(false, "Organization names can't smuggle a path.");
        }
        catch (WorkspaceException)
        {
            check(true, "Organization names can't smuggle a path.");
        }

        var project = new ProjectRef(
            "northwind.api/northwind.api",
            "Northwind.API",
            "Northwind.API/Northwind.API",
            null,
            [],
            DateTimeOffset.UtcNow.ToString("O"),
            2,
            new GitOrigin(AzureDevOpsOriginFormat.Host, "north", "Northwind.API", "Northwind.API", true)
        );
        var context = new AzureDevOpsProjectContext(client);
        var owner = string.Join(
            "\n",
            (await context.ContributeAsync(project, Audience.Owner, default)).Select(section =>
                section.Title + ": " + section.Body
            )
        );
        check(
            owner.Contains("!7 Bound AssetChat batches by Morgan Lee")
                && owner.Contains("waiting for the author")
                && owner.Contains("!8 Draft idea")
                && owner.Contains("merge conflicts")
                && owner.Contains("!9 Pat's change by Pat Lee")
                && owner.Contains("1 approval")
                && owner.Contains("!5 Earlier fix"),
            "The owner sees the repository's open and completed PRs with their state."
        );
        var boss = string.Join(
            "\n",
            (await context.ContributeAsync(project, Audience.Boss, default)).Select(section => section.Body)
        );
        check(
            boss.Contains("!7 Bound AssetChat batches")
                && boss.Contains("!5 Earlier fix")
                && !boss.Contains("Pat")
                && !boss.Contains("Someone else"),
            "A boss sees only the owner's own PRs."
        );
        check(
            (await context.ContributeAsync(project, Audience.Visitor, default)).Single().Body
                == "3 open PR(s); 2 completed recently.",
            "A visitor sees counts."
        );
        check(
            (
                await context.ContributeAsync(
                    project with
                    {
                        Origin = new GitOrigin(GitOrigin.GitHub, "Northwind", null, "x"),
                    },
                    Audience.Owner,
                    default
                )
            ).Length == 0,
            "GitHub projects are left to the GitHub contributor."
        );

        var sharing = new SharingPolicy(
            Path.Combine(Path.GetTempPath(), "hz-share-" + Guid.NewGuid().ToString("N")),
            "owner@northwind.example"
        );
        check(
            sharing.SharesWithBoss(project)
                && !sharing.SharesWithBoss(
                    project with
                    {
                        Origin = new GitOrigin(AzureDevOpsOriginFormat.Host, "someone", "P", "R", true),
                    }
                ),
            "The company's Azure DevOps organization (north for northwind.example) is shared with a boss; others aren't."
        );
    }
}
