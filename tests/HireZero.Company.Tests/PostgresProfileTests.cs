using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Node;
using HireZero.Roles.Developer;
using HireZero.Tools.Node;
using Microsoft.Data.Sqlite;

// Per-repository trusted PostgreSQL test profiles (docs/adr/0020-trusted-postgres-in-live-coding-runs.md): the owner
// sets one through an approved publication, and a live coding run gets the toolchain only when the repository has a
// profile and the toolchain is installed and intact. The toolchain status is a fake; nothing is downloaded.
public sealed class PostgresProfileTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string Repository = "fictional-org/quotes";
    private readonly TemporaryRoot _root = new("postgres-profile-");
    private bool _ready = true;

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DeveloperModule Module = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private string Folder => Path.Combine(_root.Path, "Runtime", "trusted-postgres");

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static T Approve<T>(CompanyStore store, string action, string id, int version, string digest) =>
        Apply<T>(store, Cmd(action) with { Id = id, ExpectedVersion = version, Digest = digest, Decision = "approve" });

    private (CompanyStore Store, ToolRegistry Registry, TrustedPostgresProfiles Profiles) Workspace()
    {
        var database = Path.Combine(_root.Path, "company.sqlite");
        var store = new CompanyStore(database, Owner, CompanyWorkspace.Definitions());
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        var profiles = new TrustedPostgresProfiles(database);
        profiles.Register(registry);
        registry.Register(
            TrustedPostgresTools.Status,
            (_, _) =>
                Task.FromResult(
                    new TrustedPostgresStatus(Folder, TrustedPostgresToolchain.Package, "17.6.0-beta.15", _ready, true)
                )
        );
        return (store, registry, profiles);
    }

    private static (CompanyEmployee Developer, CompanyThread Thread) Hire(CompanyStore store)
    {
        var department = Apply<CompanyDepartment>(store, Cmd("department.create") with { Name = "Engineering" });
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
        var developer = Apply<CompanyEmployee>(
            store,
            Cmd("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "developer",
                Name = "Developer partner",
                Responsibility = "Keep fictional database tests runnable",
            }
        );
        var thread = Apply<CompanyThread>(
            store,
            Cmd("thread.create") with
            {
                EmployeeId = developer.Id,
                Name = "Tools",
            }
        );
        return (developer, thread);
    }

    private static async Task<CompanyArtifact> Reading(
        CompanyStore store,
        ToolRegistry registry,
        CompanyEmployee developer,
        CompanyThread thread,
        Dictionary<string, string> input
    )
    {
        var action = Apply<CompanyTask>(
            store,
            Cmd("action.start") with
            {
                EmployeeId = developer.Id,
                ThreadId = thread.Id,
                Skill = CompanyPostgresProfile.Action,
                Body = JsonSerializer.Serialize(input),
            }
        );
        var output = await Module.RunActionAsync(
            CompanyPostgresProfile.Action,
            input,
            registry.For(Module.Manifest, developer.Id, action.Id).ReadOnly(),
            Token
        );
        var reading = store.CompleteAction(action.Id, output);
        return Approve<CompanyArtifact>(store, "artifact.review", reading.Id, reading.Version, reading.Digest);
    }

    [Fact]
    public async Task TheOwnerSetsAndRemovesAProfileThroughAnApprovedPublication()
    {
        var (store, registry, profiles) = Workspace();
        var (developer, thread) = Hire(store);
        var services = new CompanyExecutionServices(store, registry, null);
        var set = new Dictionary<string, string>
        {
            ["repository"] = Repository,
            ["database"] = "quotes_test",
            ["env"] = """{"DATABASE_URL":"{url}","DB_PORT":"{port}"}""",
        };

        var reading = await Reading(store, registry, developer, thread, set);
        Assert.Contains("Current: off", reading.Body);
        Assert.Null(profiles.Find(Repository));
        var proposed = (CompanyPublication)
            await Module.ExecuteAsync(ExecutionStep.Propose, reading.Id, services, Token);
        // Nothing is saved before the owner approves the publication.
        Assert.Null(profiles.Find(Repository));
        var publication = Approve<CompanyPublication>(
            store,
            "publication.review",
            proposed.Id,
            proposed.Version,
            proposed.Digest
        );
        var done = (CompanyPublication)
            await Module.ExecuteAsync(ExecutionStep.Publish, publication.Id, services, Token);
        Assert.Equal("published", done.State);
        var saved = profiles.Find("Fictional-Org/Quotes");
        Assert.NotNull(saved);
        Assert.Equal("quotes_test", saved.Database);
        Assert.Equal("{url}", saved.Env["DATABASE_URL"]);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Publish, publication.Id, services, Token)
        );

        // The same setting again names no change; an empty database turns the profile off.
        var same = await Reading(store, registry, developer, thread, set);
        Assert.Contains("Not eligible", same.Body);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Propose, same.Id, services, Token)
        );
        var off = await Reading(
            store,
            registry,
            developer,
            thread,
            new()
            {
                ["repository"] = Repository,
                ["database"] = "off",
                ["env"] = "{}",
            }
        );
        var removal = (CompanyPublication)await Module.ExecuteAsync(ExecutionStep.Propose, off.Id, services, Token);
        removal = Approve<CompanyPublication>(store, "publication.review", removal.Id, removal.Version, removal.Digest);
        await Module.ExecuteAsync(ExecutionStep.Publish, removal.Id, services, Token);
        Assert.Null(profiles.Find(Repository));
    }

    [Theory]
    [InlineData("not a repository", "quotes_test", "{}")]
    [InlineData(Repository, "Quotes-Test", "{}")]
    [InlineData(Repository, "quotes_test", """{"lowercase":"x"}""")]
    [InlineData(Repository, "quotes_test", "DATABASE_URL={url}")]
    public async Task AnInvalidProfileIsRefusedBeforeAnythingIsNamed(string repository, string database, string env)
    {
        var (_, registry, _) = Workspace();
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.RunActionAsync(
                CompanyPostgresProfile.Action,
                new Dictionary<string, string>
                {
                    ["repository"] = repository,
                    ["database"] = database,
                    ["env"] = env,
                },
                registry.For(Module.Manifest).ReadOnly(),
                Token
            )
        );
        Assert.Equal(422, error.Status);
    }

    // The integration refuses an invalid profile itself, whoever calls it.
    [Fact]
    public void TheProfileStoreRefusesAnInvalidProfile()
    {
        var (_, _, profiles) = Workspace();
        Assert.Throws<WorkspaceException>(() =>
            profiles.Save(new(Repository, "quotes_test", new() { ["BAD NAME"] = "x" }))
        );
        Assert.Null(profiles.Find(Repository));
    }

    [Fact]
    public async Task ARunGetsTheToolchainOnlyWithAProfileAndAnIntactInstall()
    {
        var (store, registry, profiles) = Workspace();
        var tools = registry.For(Module.Manifest);
        Assert.Null(await CompanyPostgresRun.ForAsync(tools, Repository, Token));

        profiles.Save(new(Repository, "quotes_test", new() { ["DATABASE_URL"] = "{url}" }));
        Assert.Null(await CompanyPostgresRun.ForAsync(tools, null, Token));
        Assert.Null(await CompanyPostgresRun.ForAsync(tools, "fictional-org/other", Token));
        var run = await CompanyPostgresRun.ForAsync(tools, Repository, Token);
        Assert.NotNull(run);
        Assert.Equal(Folder, run.Directory);
        Assert.Contains("node \"" + Path.Combine(Folder, "runner.mjs").Replace('\\', '/') + "\" -- ", run.PromptLine);
        using var file = JsonDocument.Parse(run.ProfileJson);
        Assert.Equal("quotes_test", file.RootElement.GetProperty("database").GetString());
        Assert.Equal("{url}", file.RootElement.GetProperty("env").GetProperty("DATABASE_URL").GetString());

        _ready = false;
        Assert.Null(await CompanyPostgresRun.ForAsync(tools, Repository, Token));

        // A workspace without the toolchain's tools runs without it.
        Assert.Null(await CompanyPostgresRun.ForAsync(new ToolRegistry(store).For(Module.Manifest), Repository, Token));
    }
}
