using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Tools.GitHub;
using Microsoft.Data.Sqlite;

// Pull-request steps from the company workspace (docs/adr/0017-publishing-a-step-from-an-action.md): a live reading
// names one step, the owner approves the reading and the exact call, and the call runs once after a live re-check.
// GitHub is a fake behind the real registry and publication verifier; the repository is fictional.
public sealed class PullRequestStepTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string Repository = "fictional-org/quotes";
    private readonly TemporaryRoot _root = new("pr-step-");
    private readonly List<GitHubCommand> _writes = [];
    private string _head = new('a', 40);
    private string _baseHead = new('b', 40);
    private string _mergeState = "clean";
    private string _author = "dependabot[bot]";
    private bool _baseGreen = true;
    private bool _baseFailed;
    private bool _merged;
    private bool _writeFails;

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DeveloperModule Module = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static T Approve<T>(CompanyStore store, string action, string id, int version, string digest) =>
        Apply<T>(store, Cmd(action) with { Id = id, ExpectedVersion = version, Digest = digest, Decision = "approve" });

    private sealed record Company(
        CompanyStore Store,
        ToolRegistry Registry,
        CompanyEmployee Developer,
        CompanyThread Thread
    )
    {
        public CompanyExecutionServices Services => new(Store, Registry, null);
    }

    // One check: passed when green, failed when failed, otherwise still running.
    private static string Checks(bool green, bool failed = false) =>
        JsonSerializer.Serialize(
            new
            {
                total_count = 1,
                check_runs = new[]
                {
                    new
                    {
                        name = "build",
                        status = green || failed ? "completed" : "in_progress",
                        conclusion = green ? "success"
                        : failed ? "failure"
                        : null,
                    },
                },
            }
        );

    private string Read(string path) =>
        path switch
        {
            "repos/" + Repository + "/pulls/7" => JsonSerializer.Serialize(
                new
                {
                    title = "Bump a fictional package",
                    state = _merged ? "closed" : "open",
                    merged = _merged,
                    draft = false,
                    mergeable_state = _mergeState,
                    user = new { login = _author },
                    head = new { sha = _head },
                    @base = new { @ref = "main" },
                }
            ),
            "repos/" + Repository => """{"allow_merge_commit":true,"allow_squash_merge":true}""",
            "repos/" + Repository + "/branches/main" => JsonSerializer.Serialize(
                new { commit = new { sha = _baseHead } }
            ),
            _ when path == $"repos/{Repository}/commits/{_head}/check-runs?per_page=100" => Checks(true),
            _ when path == $"repos/{Repository}/commits/{_baseHead}/check-runs?per_page=100" => Checks(
                _baseGreen,
                _baseFailed
            ),
            _ when path == $"repos/{Repository}/actions/runs?head_sha={_baseHead}&per_page=30" =>
                """{"workflow_runs":[{"id":91,"name":"CI","status":"completed","conclusion":"failure","run_attempt":1,"created_at":"2026-10-18T23:00:00Z"}]}""",
            _ when path == $"repos/{Repository}/compare/main...{_head}" => """{"behind_by":2}""",
            "repos/" + Repository + "/issues/7/comments?per_page=100" => "[]",
            _ => throw new ToolFailureException("failed", "Unexpected fixture GitHub read: " + path),
        };

    private async Task<Company> Hire()
    {
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        registry.Register(GitHubCommands.Read, (c, _) => Task.FromResult(Read(c.Args[^1])));
        registry.Register(
            GitHubCommands.Write,
            (c, _) =>
            {
                _writes.Add(c);
                if (_writeFails)
                    throw new TimeoutException("The fictional GitHub did not answer.");
                _merged = true;
                return Task.FromResult("""{"merged":true,"sha":"cccccccccccccccccccccccccccccccccccccccc"}""");
            }
        );
        var department = Apply<CompanyDepartment>(store, Cmd("department.create") with { Name = "Engineering" });
        var developer = Apply<CompanyEmployee>(
            store,
            Cmd("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "developer",
                Name = "Developer partner",
                Responsibility = "Keep the owner's fictional dependency bumps moving",
            }
        );
        var fields = store
            .Roles.Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(f => f, f => "Approved fictional " + f);
        var knowledge = Apply<CompanyKnowledge>(
            store,
            Cmd("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(fields),
            }
        );
        Approve<CompanyKnowledge>(store, "knowledge.review", knowledge.Id, knowledge.Version, knowledge.Digest);
        var thread = Apply<CompanyThread>(
            store,
            Cmd("thread.create") with
            {
                EmployeeId = developer.Id,
                Name = "Dependency bumps",
            }
        );
        return new(store, registry, developer, thread);
    }

    // An approved pr-step reading for pull request 7.
    private async Task<CompanyArtifact> Reading(Company company, string step = CompanyPullRequestStep.Merge)
    {
        var input = new Dictionary<string, string>
        {
            ["repository"] = Repository,
            ["number"] = "7",
            ["step"] = step,
        };
        var action = Apply<CompanyTask>(
            company.Store,
            Cmd("action.start") with
            {
                EmployeeId = company.Developer.Id,
                ThreadId = company.Thread.Id,
                Skill = CompanyPullRequestStep.Action,
                Body = JsonSerializer.Serialize(input),
            }
        );
        var output = await Module.RunActionAsync(
            CompanyPullRequestStep.Action,
            input,
            company.Registry.For(Module.Manifest, company.Developer.Id, action.Id).ReadOnly(),
            Token
        );
        var reading = company.Store.CompleteAction(action.Id, output);
        return Approve<CompanyArtifact>(company.Store, "artifact.review", reading.Id, reading.Version, reading.Digest);
    }

    private async Task<CompanyPublication> Approved(Company company, CompanyArtifact reading)
    {
        var p = (CompanyPublication)
            await Module.ExecuteAsync(ExecutionStep.Propose, reading.Id, company.Services, Token);
        return Approve<CompanyPublication>(company.Store, "publication.review", p.Id, p.Version, p.Digest);
    }

    private Task<object> Run(Company company, ExecutionStep step, CompanyPublication p) =>
        Module.ExecuteAsync(step, p.Id, company.Services, Token);

    [Fact]
    public async Task AnApprovedMergeIsPublishedOnceAtTheReviewedHead()
    {
        var company = await Hire();
        var reading = await Reading(company);
        Assert.Contains("Call: ", reading.Body);
        var publication = await Approved(company, reading);
        Assert.Empty(publication.Calls);
        Assert.Contains("Merge " + Repository + "#7", publication.Summary);
        var merged = (CompanyPublication)await Run(company, ExecutionStep.Publish, publication);
        Assert.Equal("published", merged.State);
        var write = Assert.Single(_writes);
        Assert.Equal(
            ["--method", "PUT", $"repos/{Repository}/pulls/7/merge", "-f", "sha=" + _head, "-f", "merge_method=merge"],
            write.Args[3..]
        );
        Assert.Equal(
            ["github.write"],
            company
                .Store.ToolCalls()
                .Where(c => c.Authority == CompanyStore.PublicationAuthorityKind + ":" + publication.Id)
                .Select(c => c.ToolId)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Single(_writes);
    }

    // The base branch must be green before a merge is offered, and a head that moves after approval stops the call.
    [Fact]
    public async Task AMergeWaitsForAGreenBaseAndStopsWhenTheHeadMoves()
    {
        var company = await Hire();
        _baseGreen = false;
        var waiting = await Reading(company);
        Assert.Contains("Not eligible: The base branch's checks have not passed yet", waiting.Body);
        await Assert.ThrowsAsync<WorkspaceException>(() => Approved(company, waiting));

        _baseGreen = true;
        var publication = await Approved(company, await Reading(company));
        _head = new string('d', 40);
        await Assert.ThrowsAsync<WorkspaceException>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Equal("approved", company.Store.Publication(publication.Id).State);
        Assert.Empty(_writes);
    }

    [Fact]
    public async Task ARebaseIsOnlyAskedOfDependabotAndOtherEvidenceStaysUnpublishable()
    {
        var company = await Hire();
        _author = "fictional-person";
        _mergeState = "dirty";
        var reading = await Reading(company, CompanyPullRequestStep.Rebase);
        Assert.Contains("Not eligible: Only Dependabot's pull requests are asked to rebase.", reading.Body);
        await Assert.ThrowsAsync<WorkspaceException>(() => Approved(company, reading));

        var evidence = Apply<CompanyTask>(
            company.Store,
            Cmd("action.start") with
            {
                EmployeeId = company.Developer.Id,
                ThreadId = company.Thread.Id,
                Skill = PullRequestEvidenceAction.Id,
                Body = JsonSerializer.Serialize(new { repository = Repository, number = "7" }),
            }
        );
        var gathered = company.Store.CompleteAction(evidence.Id, "Fictional gathered evidence.");
        gathered = Approve<CompanyArtifact>(
            company.Store,
            "artifact.review",
            gathered.Id,
            gathered.Version,
            gathered.Digest
        );
        var refusal = await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Propose, gathered.Id, company.Services, Token)
        );
        Assert.Contains("no reviewed execution", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<WorkspaceException>(() =>
            company.Store.ProposePublication(
                new(null, gathered.Id, "Publish gathered evidence.", [], new string('e', 64))
            )
        );
        Assert.Empty(_writes);
    }

    [Fact]
    public async Task AnUnconfirmedMergeIsUnknownAndReconciledOnlyByReading()
    {
        var company = await Hire();
        var publication = await Approved(company, await Reading(company));
        _writeFails = true;
        await Assert.ThrowsAsync<TimeoutException>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Equal("unknown", company.Store.Publication(publication.Id).State);
        Assert.Equal("unknown", ((CompanyPublication)await Run(company, ExecutionStep.Reconcile, publication)).State);
        _merged = true;
        Assert.Equal("published", ((CompanyPublication)await Run(company, ExecutionStep.Reconcile, publication)).State);
        Assert.Single(_writes);
    }

    // A failed base branch blocks every merge until its failed jobs pass; rerun-base re-runs them once.
    [Fact]
    public async Task AFailedBaseIsReRunOnceAndBlocksMergesUntilThen()
    {
        var company = await Hire();
        _baseGreen = false;
        _baseFailed = true;
        var blocked = await Reading(company);
        Assert.Contains("Not eligible: The base branch's checks failed; re-run them (rerun-base)", blocked.Body);
        var publication = await Approved(company, await Reading(company, CompanyPullRequestStep.RerunBase));
        Assert.Contains("run 91 on the base branch", publication.Summary);
        Assert.Equal("published", ((CompanyPublication)await Run(company, ExecutionStep.Publish, publication)).State);
        var write = Assert.Single(_writes);
        Assert.Equal(["--method", "POST", $"repos/{Repository}/actions/runs/91/rerun-failed-jobs"], write.Args[3..]);
    }
}
