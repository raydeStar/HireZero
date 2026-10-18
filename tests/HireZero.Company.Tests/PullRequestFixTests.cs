using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Integrations.Git;
using HireZero.Roles.Developer;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// The company Developer's pull request fix (docs/adr/0012-company-publication.md): reviewed execution in the owner's
// checkout of the PR head, then one commit of exactly the reviewed files and one leased push, each authorized by the
// owner's publication approval. Real git in a temporary repository; GitHub and the push are fakes behind the registry.
public sealed class PullRequestFixTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string Repository = "owner/quotes";
    private const string BranchName = "fix/rounding";

    private readonly TemporaryRoot _root = new("pr-fix-");
    private readonly List<GitCommand> _pushes = [];
    private readonly List<GitCommand> _writes = [];
    private string _remoteHead = "";
    private bool _protected;
    private Func<GitCommand, string>? _push;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static readonly string GitPath =
        GitToolRegistration.FindGit() ?? throw new InvalidOperationException("Git is required for this test.");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Checkout => Path.Combine(_root.Path, "checkout");

    private async Task<string> Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo(GitPath)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var empty = Path.Combine(_root.Path, "empty-config");
        if (!File.Exists(empty))
            File.WriteAllText(empty, "");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = empty;
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new ToolFailureException("failed", "Fixture git failed.", detail: await error);
        return await output;
    }

    private sealed record Fix(
        CompanyStore Store,
        CompanyPullRequestFix Developer,
        ToolRegistry Registry,
        CompanyEmployee Employee,
        CompanyTask Task,
        string Head
    );

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static T Approve<T>(CompanyStore store, string action, string id, int version, string digest) =>
        Apply<T>(store, Cmd(action) with { Id = id, ExpectedVersion = version, Digest = digest, Decision = "approve" });

    private string Pull() =>
        JsonSerializer.Serialize(
            new
            {
                state = "open",
                head = new
                {
                    sha = _remoteHead,
                    @ref = BranchName,
                    repo = new { full_name = Repository, default_branch = "main" },
                },
            }
        );

    private ToolRegistry Registry(CompanyStore store)
    {
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        Handlers(registry);
        return registry;
    }

    // Real git and the checkout tools; a fake push that moves the fake remote branch; fake GitHub reads.
    private void Handlers(ToolRegistry registry)
    {
        registry.AddGitTools(new GitCheckoutInspector(GitPath));
        registry.Register(GitCommands.Read, (c, _) => Git(c.Root!, c.Args));
        registry.Register(
            GitCommands.Write,
            (c, _) =>
            {
                _writes.Add(c);
                return Git(c.Root!, c.Args);
            }
        );
        registry.Register(
            GitCommands.Push,
            (c, _) =>
            {
                _pushes.Add(c);
                if (_push is not null)
                    return Task.FromResult(_push(c));
                _remoteHead = c.Args[^1].Split(':')[0];
                return Task.FromResult("");
            }
        );
        registry.Register(
            GitHubCommands.Read,
            (c, _) =>
                Task.FromResult(
                    c.Args[^1] switch
                    {
                        "repos/" + Repository + "/pulls/7" => Pull(),
                        "repos/" + Repository + "/branches/fix%2Frounding" => JsonSerializer.Serialize(
                            new { @protected = _protected, commit = new { sha = _remoteHead } }
                        ),
                        "repos/" + Repository => """{"permissions":{"push":true}}""",
                        _ => throw new ToolFailureException("failed", "Unexpected fixture GitHub read."),
                    }
                )
        );
    }

    private CompanyCodingFactory Coding()
    {
        var fake = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../HireZero.FakeCodex/bin/Debug/net10.0/HireZero.FakeCodex.exe"
            )
        );
        return new(
            Path.Combine(_root.Path, "coding.sqlite"),
            fake,
            Path.Combine(_root.Path, "scoped-runtime"),
            CompanyCodingFactory.Identity(fake)
        );
    }

    // A hired Developer, approved evidence for PR 7 at the checkout's head, and a pr-fix task on the owner's checkout.
    private async Task<Fix> Assign(CompanyStore? existing = null, ToolRegistry? tools = null)
    {
        Directory.CreateDirectory(Checkout);
        await Git(Checkout, "init", "-q");
        await Git(Checkout, "config", "user.name", "Fictional Fixture");
        await Git(Checkout, "config", "user.email", "fixture@example.invalid");
        await Git(Checkout, "config", "commit.gpgsign", "false");
        await File.WriteAllTextAsync(Path.Combine(Checkout, "Quote.cs"), "// Fictional quote rounding\n", Token);
        await Git(Checkout, "add", "Quote.cs");
        await Git(Checkout, "commit", "-q", "-m", "Fictional baseline");
        var head = (await Git(Checkout, "rev-parse", "HEAD")).Trim();
        _remoteHead = head;

        var store =
            existing
            ?? new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var department = Apply<CompanyDepartment>(store, Cmd("department.create") with { Name = "Engineering" });
        var employee = Apply<CompanyEmployee>(
            store,
            Cmd("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "developer",
                Name = "Developer partner",
                Responsibility = "Fix fictional pull requests",
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
                EmployeeId = employee.Id,
                Name = "Fixes",
            }
        );
        var action = Apply<CompanyTask>(
            store,
            Cmd("action.start") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = PullRequestEvidenceAction.Id,
                Body = JsonSerializer.Serialize(new { repository = Repository, number = "7" }),
            }
        );
        using var view = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    title = "Fix rounding in quotes",
                    body = "Rounds half up.",
                    state = "OPEN",
                    headRefName = BranchName,
                    baseRefName = "main",
                    headRefOid = head,
                }
            )
        );
        using var comments = JsonDocument.Parse("[]");
        var evidence = store.CompleteAction(
            action.Id,
            PullRequestEvidenceAction.Format(
                Repository,
                7,
                view.RootElement,
                comments.RootElement,
                DateTimeOffset.UtcNow
            )
        );
        Approve<CompanyArtifact>(store, "artifact.review", evidence.Id, evidence.Version, evidence.Digest);
        var task = Apply<CompanyTask>(
            store,
            Cmd("task.create") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = DeveloperModule.Fix,
                Body = "Fix the negative-total rounding finding.",
                Evidence = [evidence.Id],
            }
        );
        task = Apply<CompanyTask>(
            store,
            Cmd("workspace.grant") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = Checkout,
            }
        );
        var registry = tools ?? Registry(store);
        return new(store, new(store, registry, Coding()), registry, employee, task, head);
    }

    // The fix ran under an approved grant and its publication is proposed; nothing is approved for publication yet.
    private async Task<(Fix Fix, CompanyPublication Publication)> Proposed()
    {
        var fix = await Assign();
        var grant = await fix.Developer.PrepareAsync(fix.Task.Id, Token);
        Assert.Equal(fix.Head, grant.Revision);
        Approve<CompanyExecutionGrant>(fix.Store, "execution.review", grant.Id, grant.Version, grant.Digest);
        await fix.Developer.ExecuteFixtureAsync(grant.Id, Token);
        Assert.Equal("completed", fix.Store.Execution(grant.Id).State);
        var publication = await fix.Developer.ProposePublicationAsync(grant.Id, Token);
        return (fix, publication);
    }

    private static CompanyPublication ApproveAll(Fix fix, CompanyPublication publication)
    {
        var artifact = fix.Store.Artifact(publication.ArtifactId);
        Approve<CompanyArtifact>(fix.Store, "artifact.review", artifact.Id, artifact.Version, artifact.Digest);
        return Approve<CompanyPublication>(
            fix.Store,
            "publication.review",
            publication.Id,
            publication.Version,
            publication.Digest
        );
    }

    [Fact]
    public async Task AnApprovedFixIsCommittedAndPushedOnceWithALease()
    {
        var (fix, publication) = await Proposed();
        Assert.Equal("proposed", publication.State);
        Assert.Contains("owner/quotes:fix/rounding", publication.Summary);
        Assert.Empty(_writes);

        // The change must be approved before its publication, and the publication at its exact digest.
        Assert.Throws<WorkspaceException>(() =>
            Approve<CompanyPublication>(
                fix.Store,
                "publication.review",
                publication.Id,
                publication.Version,
                publication.Digest
            )
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));
        Assert.Empty(_writes);
        var artifact = fix.Store.Artifact(publication.ArtifactId);
        Approve<CompanyArtifact>(fix.Store, "artifact.review", artifact.Id, artifact.Version, artifact.Digest);
        Assert.Throws<WorkspaceException>(() =>
            Approve<CompanyPublication>(
                fix.Store,
                "publication.review",
                publication.Id,
                publication.Version,
                new string('0', 64)
            )
        );
        Approve<CompanyPublication>(
            fix.Store,
            "publication.review",
            publication.Id,
            publication.Version,
            publication.Digest
        );

        var published = await fix.Developer.PublishAsync(publication.Id, Token);
        Assert.Equal("published", published.State);
        var commit = published.Result!;
        Assert.Equal(commit, _remoteHead);
        Assert.Equal(fix.Head, (await Git(Checkout, "rev-parse", "HEAD^")).Trim());
        Assert.Equal("fixture-change.txt", (await Git(Checkout, "diff", "--name-only", "HEAD^", "HEAD")).Trim());
        var push = Assert.Single(_pushes);
        Assert.Contains("--force-with-lease=refs/heads/fix/rounding:" + fix.Head, push.Args);
        Assert.Equal(["https://github.com/owner/quotes.git", commit + ":refs/heads/fix/rounding"], push.Args[^2..]);
        Assert.Equal(2, _writes.Count);
        var authorized = fix
            .Store.ToolCalls()
            .Where(c => c.Authority == CompanyStore.PublicationAuthorityKind + ":" + publication.Id)
            .ToArray();
        Assert.Equal(["git.push", "git.write", "git.write"], authorized.Select(c => c.ToolId).Order());
        Assert.All(authorized, c => Assert.Equal("completed", c.Outcome));

        // Never again: not the publication, not the push with the same authority.
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));
        var tools = fix.Registry.For(DeveloperModule.Shared, fix.Employee.Id, fix.Task.Id);
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(
                GitCommands.Push,
                push,
                TestContext.Current.CancellationToken,
                new(CompanyStore.PublicationAuthorityKind, publication.Id)
            )
        );
        Assert.Single(_pushes);
    }

    [Fact]
    public async Task OnlyTheProposedCallsAreAuthorizedInOrder()
    {
        var (fix, publication) = await Proposed();
        ApproveAll(fix, publication);
        var authority = new ToolAuthority(CompanyStore.PublicationAuthorityKind, publication.Id);
        var tools = fix.Registry.For(DeveloperModule.Shared, fix.Employee.Id, fix.Task.Id);
        var token = TestContext.Current.CancellationToken;
        var add = new GitCommand(
            fix.Store.Execution(publication.GrantId).Workspace,
            ["add", "--", "fixture-change.txt"]
        );

        // Before the claim, nothing is authorized.
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(GitCommands.Write, add, token, authority)
        );
        fix.Store.ClaimPublication(publication.Id);
        // Another task's toolbox, another command, or the commit before the add: all refused.
        var other = fix.Registry.For(DeveloperModule.Shared, fix.Employee.Id, "another-task");
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            other.InvokeAsync(GitCommands.Write, add, token, authority)
        );
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(GitCommands.Write, add with { Args = ["add", "--", "Quote.cs"] }, token, authority)
        );
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(GitCommands.Write, add with { Args = ["reset", "--hard"] }, token, authority)
        );
        await tools.InvokeAsync(GitCommands.Write, add, token, authority);
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(GitCommands.Write, add, token, authority)
        );
        // A push is refused until every local call ran and the push was bound.
        Assert.Throws<WorkspaceException>(() =>
            fix.Store.BindPublication(publication.Id, fix.Head, new string('a', 64))
        );
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(GitCommands.Push, add with { Args = ["push"] }, token, authority)
        );
        Assert.Empty(_pushes);

        // A restart while publishing fails it, since nothing was sent; it is never resumed.
        fix.Store.Recover();
        Assert.Equal(
            ("failed", "publication-interrupted"),
            (fix.Store.Publication(publication.Id).State, fix.Store.Publication(publication.Id).Reason)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));
    }

    [Fact]
    public async Task AChangedCheckoutOrBranchStopsThePublicationBeforeAnyPush()
    {
        var (fix, publication) = await Proposed();
        ApproveAll(fix, publication);

        // The branch moved: refused before the publication starts, so nothing is consumed.
        _remoteHead = new string('b', 40);
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));
        Assert.Equal("approved", fix.Store.Publication(publication.Id).State);
        _remoteHead = fix.Head;
        _protected = true;
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));
        _protected = false;

        // The checkout no longer holds the reviewed diff: the publication fails without committing or pushing.
        await File.AppendAllTextAsync(Path.Combine(Checkout, "fixture-change.txt"), " and an unreviewed edit", Token);
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));
        Assert.Equal("failed", fix.Store.Publication(publication.Id).State);
        Assert.Empty(_writes);
        Assert.Empty(_pushes);
        Assert.Equal(fix.Head, (await Git(Checkout, "rev-parse", "HEAD")).Trim());
    }

    [Fact]
    public async Task AnUnconfirmedPushIsUnknownAndOnlyReconciledByReading()
    {
        var (fix, publication) = await Proposed();
        ApproveAll(fix, publication);
        _push = _ => throw new TimeoutException("The fixture push timed out.");
        await Assert.ThrowsAsync<TimeoutException>(() => fix.Developer.PublishAsync(publication.Id, Token));
        var unknown = fix.Store.Publication(publication.Id);
        Assert.Equal("unknown", unknown.State);
        fix.Store.Recover();
        Assert.Equal("unknown", fix.Store.Publication(publication.Id).State);
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PublishAsync(publication.Id, Token));

        // The branch is not at the commit: still unknown. Once it is, a read confirms it. No second push either way.
        Assert.Equal("unknown", (await fix.Developer.ReconcileAsync(publication.Id, Token)).State);
        _remoteHead = unknown.Result!;
        Assert.Equal("published", (await fix.Developer.ReconcileAsync(publication.Id, Token)).State);
        Assert.Single(_pushes);
    }

    [Fact]
    public async Task AFixStartsOnlyAtTheReviewedHeadInACleanCheckout()
    {
        var fix = await Assign();
        _remoteHead = new string('c', 40);
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PrepareAsync(fix.Task.Id, Token));
        _remoteHead = fix.Head;
        await File.AppendAllTextAsync(Path.Combine(Checkout, "Quote.cs"), "// unreviewed\n", Token);
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PrepareAsync(fix.Task.Id, Token));
        await Git(Checkout, "checkout", "--", "Quote.cs");
        var grant = await fix.Developer.PrepareAsync(fix.Task.Id, Token);
        Assert.Equal(fix.Head, grant.Revision);
    }

    // Without a qualified coding runner the module prepares but refuses to run, before anything starts.
    [Fact]
    public async Task WithoutARunnerExecutionIsPreparedButNotRun()
    {
        var fix = await Assign();
        var module = new DeveloperModule();
        var services = new CompanyExecutionServices(fix.Store, fix.Registry, null);
        var grant = (CompanyExecutionGrant)
            await module.ExecuteAsync(ExecutionStep.Prepare, fix.Task.Id, services, Token);
        grant = Approve<CompanyExecutionGrant>(fix.Store, "execution.review", grant.Id, grant.Version, grant.Digest);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            module.ExecuteAsync(ExecutionStep.Run, grant.Id, services, Token)
        );
        Assert.Equal(
            ("approved", (string?)null),
            (fix.Store.Execution(grant.Id).State, fix.Store.Execution(grant.Id).RunId)
        );
        Assert.False(File.Exists(Path.Combine(Checkout, "fixture-change.txt")));
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            module.ExecuteAsync(ExecutionStep.Propose, grant.Id, services, Token)
        );
    }

    // The company host drives every step through the employee's module; each step is single use.
    [Fact]
    public async Task TheHostRunsEachStepOnceThroughTheModule()
    {
        var previous = Environment.GetEnvironmentVariable(HeldWorkspace.Variable);
        Environment.SetEnvironmentVariable(HeldWorkspace.Variable, "0");
        try
        {
            await using var app = CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                Owner,
                true,
                Path.Combine(_root.Path, "host"),
                fixtureUrl: "http://127.0.0.1:0",
                fixtureTools: Handlers,
                fixtureCoding: Coding()
            )!;
            var store = app.Services.GetRequiredService<CompanyStore>();
            var fix = await Assign(store, app.Services.GetRequiredService<ToolRegistry>());
            await app.StartAsync(Token);
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(address),
            };
            client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
            client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
            client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
            async Task<(HttpStatusCode Status, string Body)> Step(string step, string id)
            {
                using var response = await client.PostAsJsonAsync(
                    "/api/company/executions/" + step + "/" + id,
                    new { },
                    Token
                );
                return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
            }
            T Ok<T>((HttpStatusCode Status, string Body) reply)
            {
                Assert.True(reply.Status == HttpStatusCode.OK, reply.Body);
                return CompanyJson.Decode<T>(reply.Body);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await Step("launch", fix.Task.Id)).Status);
            var grant = Ok<CompanyExecutionGrant>(await Step("prepare", fix.Task.Id));
            Assert.InRange((int)(await Step("run", grant.Id)).Status, 400, 499);
            Approve<CompanyExecutionGrant>(store, "execution.review", grant.Id, grant.Version, grant.Digest);
            Assert.Equal("completed", Ok<CompanyExecutionGrant>(await Step("run", grant.Id)).State);
            Assert.InRange((int)(await Step("run", grant.Id)).Status, 400, 499);
            var publication = Ok<CompanyPublication>(await Step("propose", grant.Id));
            Assert.InRange((int)(await Step("propose", grant.Id)).Status, 400, 499);
            ApproveAll(fix, publication);
            Assert.Equal("published", Ok<CompanyPublication>(await Step("publish", publication.Id)).State);
            Assert.InRange((int)(await Step("publish", publication.Id)).Status, 400, 499);
            Assert.Single(_pushes);
            var detail = JsonSerializer.SerializeToElement(store.TaskDetail(fix.Task.Id), CompanyJson.Options);
            Assert.Equal("published", detail.GetProperty("publications")[0].GetProperty("state").GetString());
            var role = JsonSerializer
                .SerializeToElement(store.Dashboard(), CompanyJson.Options)
                .GetProperty("roles")
                .EnumerateArray()
                .Single(r => r.GetProperty("id").GetString() == "developer");
            Assert.Equal(
                [DeveloperModule.Fix],
                role.GetProperty("publishingSkills").EnumerateArray().Select(e => e.GetString()!)
            );
            await app.StopAsync(Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HeldWorkspace.Variable, previous);
        }
    }

    private CompanyStore Store(CompanyExecutionLimits limits, Func<DateTimeOffset>? clock = null) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions())
        {
            ExecutionDefaults = limits,
            Clock = clock ?? (() => DateTimeOffset.UtcNow),
        };

    // The owner approves the run's limits with the grant, and its reservation is sized from them.
    [Fact]
    public async Task LimitsAreApprovedWithTheGrantAndSizeItsReservation()
    {
        var limits = new CompanyExecutionLimits(8000, 120, "medium");
        var fix = await Assign(Store(limits));
        var grant = await fix.Developer.PrepareAsync(fix.Task.Id, Token);
        Assert.Equal(limits, grant.Limits);
        Approve<CompanyExecutionGrant>(fix.Store, "execution.review", grant.Id, grant.Version, grant.Digest);
        await fix.Developer.ExecuteFixtureAsync(grant.Id, Token);
        Assert.Equal("completed", fix.Store.Execution(grant.Id).State);
        var budgets = JsonSerializer
            .SerializeToElement(fix.Store.Dashboard(), CompanyJson.Options)
            .GetProperty("budgets")
            .EnumerateArray()
            .Single(b => b.GetProperty("id").GetString() == "employee:" + fix.Employee.Id);
        Assert.Equal(0, budgets.GetProperty("reserved").GetInt64());
        Assert.InRange(budgets.GetProperty("used").GetInt64(), 1, 8000);
    }

    // A run whose limits exceed the employee's budget is never started.
    [Fact]
    public async Task ARunLargerThanTheBudgetIsNotSent()
    {
        var fix = await Assign(Store(new(30000, 120, "medium")));
        var grant = await fix.Developer.PrepareAsync(fix.Task.Id, Token);
        Approve<CompanyExecutionGrant>(fix.Store, "execution.review", grant.Id, grant.Version, grant.Digest);
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.ExecuteFixtureAsync(grant.Id, Token));
        Assert.Equal(
            ("not_sent", (string?)null),
            (fix.Store.Execution(grant.Id).State, fix.Store.Execution(grant.Id).RunId)
        );
        Assert.False(File.Exists(Path.Combine(Checkout, "fixture-change.txt")));
    }

    [Fact]
    public async Task LimitsAboveTheCeilingAreRefused()
    {
        var fix = await Assign(Store(new(2_000_000, 120, "medium")));
        await Assert.ThrowsAsync<WorkspaceException>(() => fix.Developer.PrepareAsync(fix.Task.Id, Token));
        Assert.False(new CompanyExecutionLimits(1000, 120, "extreme").Within());
        Assert.False(new CompanyExecutionLimits(1000, 7200, "low").Within());
    }

    // A run approved near the end of its proposal window may still last its approved time, and no longer; usage up to
    // its reservation settles.
    [Fact]
    public async Task ADispatchedRunLastsItsApprovedTimeAndSettlesWithinItsReservation()
    {
        var now = DateTimeOffset.UtcNow;
        var fix = await Assign(Store(new(8000, 900, "medium"), () => now));
        var grant = await fix.Developer.PrepareAsync(fix.Task.Id, Token);
        now = now.AddMinutes(9);
        Approve<CompanyExecutionGrant>(fix.Store, "execution.review", grant.Id, grant.Version, grant.Digest);
        Assert.True(fix.Store.DispatchExecution(grant.Id));
        now = now.AddMinutes(11);
        Assert.True(fix.Store.ExecutionCurrent(grant.Id));
        fix.Store.BindExecution(grant.Id, "fixture-run");
        fix.Store.Finish(grant.AttemptId, new("Fictional result", 7000, "codex:fixture-run"));
        Assert.Equal("awaiting_review", fix.Store.Work(fix.Task.Id).State);
        now = now.AddMinutes(6);
        Assert.False(fix.Store.ExecutionCurrent(grant.Id));
    }
}
