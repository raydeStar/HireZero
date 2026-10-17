using HireZero.Core;
using HireZero.Integrations.GitHub;
using HireZero.Roles.Developer;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;
using Microsoft.Data.Sqlite;

// Developer's git and gh commands run as declared tools: reads freely, writes only under the approved workflow or
// fix loop in scope, and only for that workflow's own commit, branch and pull request (docs/adr/0007-tool-registry.md).
public sealed class DeveloperToolTests : IDisposable
{
    // The real integration with the process replaced: records what would have run.
    private sealed class RecordingCli : GitHubCli
    {
        public List<string> Ran { get; } = [];

        protected override Task<string> RunAsync(
            string executable,
            string? root,
            string[] args,
            string? input,
            CancellationToken token
        )
        {
            Ran.Add(Path.GetFileNameWithoutExtension(executable) + " " + string.Join(' ', args));
            return Task.FromResult("""{"id":7,"created_at":"2026-10-17T00:00:00Z","merged":true}""");
        }
    }

    private sealed class Client(ToolRegistry registry) : GitHubReviewClient(registry)
    {
        public Task<string> Git(params string[] args) =>
            CommandAsync("git", null, args, null, TestContext.Current.CancellationToken);

        public Task<string> Gh(string? input, params string[] args) =>
            CommandAsync("gh", null, args, input, TestContext.Current.CancellationToken);
    }

    private readonly string _database = Path.Combine(
        Path.GetTempPath(),
        "developer-tools-" + Guid.NewGuid().ToString("N") + ".sqlite"
    );
    private readonly GitHubReviewStore _store;
    private readonly RecordingCli _cli = new();
    private readonly MemoryToolAudit _audit = new();
    private readonly Client _client;
    private static readonly string Commit = new('c', 40);

    public DeveloperToolTests()
    {
        _store = new GitHubReviewStore(_database);
        var registry = new ToolRegistry(
            _audit,
            [new WorkflowAuthorityVerifier(_store), new LoopAuthorityVerifier(_store)]
        );
        _cli.Register(registry);
        _client = new Client(registry);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_database);
    }

    private PullRequestWorkflow Workflow(string state, bool publishClaimed)
    {
        var workflow = new PullRequestWorkflow(
            Guid.NewGuid().ToString("N"),
            "request",
            1,
            "k",
            "owner/example",
            10,
            42,
            new string('a', 40),
            "snap",
            new FixStrategy("Fix", ["Step"], ["test"], [], [], false, "high", false, false, ""),
            "m",
            state,
            CommitSha: Commit
        );
        _store.SaveWorkflow(workflow);
        if (publishClaimed)
        {
            _store.RegisterPublication(workflow);
            using var db = new SqliteConnection("Data Source=" + _database);
            db.Open();
            using var claim = db.CreateCommand();
            claim.CommandText =
                "UPDATE github_workflow_approvals SET consumed_at='2026-10-17T00:00:00Z' WHERE workflow_id=$id";
            claim.Parameters.AddWithValue("$id", workflow.Id);
            claim.ExecuteNonQuery();
        }
        return workflow;
    }

    private static string[] Push(string commit) =>
        ["-c", "core.hooksPath=NUL", "push", "https://github.com/owner/example.git", commit + ":refs/heads/fix"];

    [Fact]
    public async Task ReadsNeedNoAuthority()
    {
        await _client.Git("status", "--porcelain=v1");
        await _client.Gh(null, "api", "--hostname", "github.com", "--method", "GET", "repos/owner/example");
        Assert.Equal(2, _cli.Ran.Count);
        Assert.All(_audit.Records, r => Assert.Equal("completed", r.Outcome));
    }

    [Fact]
    public async Task WritesOutsideAnApprovedScopeAreRefused()
    {
        await Assert.ThrowsAsync<ReviewAccessException>(() => _client.Git("commit", "-m", "unapproved"));
        await Assert.ThrowsAsync<ReviewAccessException>(() => _client.Git(Push(Commit)));
        await Assert.ThrowsAsync<ReviewAccessException>(() =>
            _client.Gh("{}", "api", "--method", "PUT", "repos/owner/example/pulls/42/merge")
        );
        Assert.Empty(_cli.Ran);
        Assert.All(_audit.Records, r => Assert.Equal("refused", r.Outcome));
    }

    [Fact]
    public async Task ClaimedApprovalAuthorizesLocalWrites()
    {
        var unclaimed = Workflow("committing", publishClaimed: false);
        using (DeveloperAuthority.Workflow(unclaimed.Id))
            await Assert.ThrowsAsync<ReviewAccessException>(() => _client.Git("commit", "-m", "fix"));
        var claimed = Workflow("committing", publishClaimed: true);
        using (DeveloperAuthority.Workflow(claimed.Id))
            await _client.Git("commit", "-m", "fix");
        Assert.Equal(["git commit -m fix"], _cli.Ran);
        Assert.Equal(DeveloperAuthority.WorkflowKind + ":" + claimed.Id, _audit.Records[^1].Authority);
    }

    [Fact]
    public async Task PushOnlyTheApprovedCommitWhilePushing()
    {
        var committing = Workflow("committing", publishClaimed: true);
        using (DeveloperAuthority.Workflow(committing.Id))
            await Assert.ThrowsAsync<ReviewAccessException>(() => _client.Git(Push(Commit)));
        var pushing = Workflow("pushing", publishClaimed: true);
        using (DeveloperAuthority.Workflow(pushing.Id))
        {
            await Assert.ThrowsAsync<ReviewAccessException>(() => _client.Git(Push(new string('d', 40))));
            await _client.Git(Push(Commit));
        }
        Assert.Single(_cli.Ran);
    }

    [Fact]
    public async Task MergeOnlyTheWorkflowsOwnPullRequestWhilePending()
    {
        var pending = Workflow("merge-pending", publishClaimed: false);
        using (DeveloperAuthority.Workflow(pending.Id))
        {
            await Assert.ThrowsAsync<ReviewAccessException>(() =>
                _client.Gh("{}", "api", "--method", "PUT", "repos/owner/other/pulls/42/merge")
            );
            await _client.Gh("{}", "api", "--method", "PUT", "repos/owner/example/pulls/42/merge");
        }
        Assert.Single(_cli.Ran);
    }

    [Fact]
    public async Task ActiveLoopMayOnlyRequestACodexReviewOnItsPullRequest()
    {
        var loop = new FixLoop(
            "loop",
            "request",
            1,
            "owner/example",
            42,
            "working",
            1,
            3,
            false,
            null,
            null,
            null,
            null,
            null,
            "t",
            "t"
        );
        _store.SaveLoop(loop);
        var request = System.Text.Json.JsonSerializer.Serialize(new { body = "@codex review" });
        using (DeveloperAuthority.Loop(loop.Id))
        {
            await Assert.ThrowsAsync<ReviewAccessException>(() =>
                _client.Gh(
                    "{\"body\":\"ship it\"}",
                    "api",
                    "--method",
                    "POST",
                    "repos/owner/example/issues/42/comments"
                )
            );
            await _client.Gh(request, "api", "--method", "POST", "repos/owner/example/issues/42/comments");
        }
        _store.SaveLoop(loop with { State = "stopped" });
        using (DeveloperAuthority.Loop(loop.Id))
            await Assert.ThrowsAsync<ReviewAccessException>(() =>
                _client.Gh(request, "api", "--method", "POST", "repos/owner/example/issues/42/comments")
            );
        Assert.Single(_cli.Ran);
    }

    // The integration enforces the classification itself: a write sent through a read tool never runs.
    [Fact]
    public async Task IntegrationRefusesMisroutedCommands()
    {
        var tools = new ToolRegistry(_audit);
        _cli.Register(tools);
        var toolbox = tools.For(DeveloperModule.Shared);
        var token = TestContext.Current.CancellationToken;
        var refused = await Assert.ThrowsAsync<ToolFailureException>(() =>
            toolbox.InvokeAsync(GitCommands.Read, new(null, Push(Commit)), token)
        );
        Assert.Equal("refused", refused.Kind);
        await Assert.ThrowsAsync<ToolFailureException>(() =>
            toolbox.InvokeAsync(GitHubCommands.Read, new(["api", "graphql"], "mutation { deleteRepository }"), token)
        );
        Assert.Empty(_cli.Ran);
    }

    [Theory]
    [InlineData("status", "git.read")]
    [InlineData("-c core.fsmonitor=false rev-parse HEAD", "git.read")]
    [InlineData("config --local --no-includes --get remote.origin.url", "git.read")]
    [InlineData("config --local remote.origin.url https://evil.example", null)]
    [InlineData("worktree list --porcelain", "git.read")]
    [InlineData("worktree add --detach dir sha", "git.write")]
    [InlineData("remote get-url origin", "git.read")]
    [InlineData("remote set-url origin x", null)]
    [InlineData("commit -m x", "git.write")]
    [InlineData("-c core.hooksPath=NUL push url sha:refs/heads/b", "git.push")]
    [InlineData("gc", null)]
    public void GitClassification(string command, string? tool) =>
        Assert.Equal(tool, GitCommands.Classify(command.Split(' '))?.Id);

    [Theory]
    [InlineData("api --method GET repos/o/r", null, "github.read")]
    [InlineData("api --method GET repos/o/r -f x=1", null, "github.read-refused")]
    [InlineData("api repos/o/r", null, "none")]
    [InlineData("api --method PUT repos/o/r/pulls/1/merge", "{}", "github.write")]
    [InlineData("api graphql --input -", "query { viewer { login } }", "github.read")]
    [InlineData("api graphql --input -", "mutation { x }", "none")]
    [InlineData("pr view 1", null, "github.read")]
    [InlineData("pr merge 1", null, "none")]
    [InlineData("auth login --web", null, "none")]
    public void GitHubClassification(string command, string? input, string expected)
    {
        var tool = GitHubCommands.Classify(command.Split(' '), input)?.Id;
        Assert.Equal(expected is "none" or "github.read-refused" ? null : expected, tool);
    }
}
