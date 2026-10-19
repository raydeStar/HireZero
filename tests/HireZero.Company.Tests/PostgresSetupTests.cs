using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Tools.Node;
using Microsoft.Data.Sqlite;

// Trusted PostgreSQL setup (ported in Phase 2B.5j): a reading names the pinned install, the owner approves it, and the
// two exact local calls run once under the publication's authority. The toolchain is a fake; nothing is downloaded.
public sealed class PostgresSetupTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("postgres-setup-");
    private readonly List<string> _calls = [];
    private bool _installed;

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

    [Fact]
    public async Task ThePinnedInstallRunsOnceAsTheApprovedCalls()
    {
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        var folder = Path.Combine(_root.Path, "Runtime", "trusted-postgres");
        registry.Register(
            TrustedPostgresTools.Status,
            (_, _) =>
                Task.FromResult(
                    new TrustedPostgresStatus(
                        folder,
                        "@embedded-postgres/windows-x64",
                        "17.6.0-beta.15",
                        _installed,
                        _installed
                    )
                )
        );
        registry.Register(
            TrustedPostgresTools.PrepareRunner,
            (_, _) =>
            {
                _calls.Add("prepare");
                return Task.FromResult(folder);
            }
        );
        registry.Register(
            NodeTools.InstallPinned,
            (install, _) =>
            {
                _calls.Add("install " + install.Package + "@" + install.Version);
                _installed = true;
                return Task.FromResult(install.Package);
            }
        );
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
                Responsibility = "Keep the fictional test toolchain ready",
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

        async Task<CompanyArtifact> Reading()
        {
            var input = new Dictionary<string, string> { ["step"] = "install" };
            var action = Apply<CompanyTask>(
                store,
                Cmd("action.start") with
                {
                    EmployeeId = developer.Id,
                    ThreadId = thread.Id,
                    Skill = CompanyPostgresSetup.Action,
                    Body = JsonSerializer.Serialize(input),
                }
            );
            var output = await Module.RunActionAsync(
                CompanyPostgresSetup.Action,
                input,
                registry.For(Module.Manifest, developer.Id, action.Id).ReadOnly(),
                Token
            );
            var reading = store.CompleteAction(action.Id, output);
            return Approve<CompanyArtifact>(store, "artifact.review", reading.Id, reading.Version, reading.Digest);
        }

        var services = new CompanyExecutionServices(store, registry, null);
        var proposed = (CompanyPublication)
            await Module.ExecuteAsync(ExecutionStep.Propose, (await Reading()).Id, services, Token);
        Assert.Equal(2, proposed.Calls.Length);
        Assert.Null(proposed.ApprovedExternal);
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
        Assert.Equal(["prepare", "install @embedded-postgres/windows-x64@17.6.0-beta.15"], _calls);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Publish, publication.Id, services, Token)
        );

        // Once installed and intact, a reading names no step.
        var ready = await Reading();
        Assert.Contains("Not eligible: It is installed and intact.", ready.Body);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Propose, ready.Id, services, Token)
        );
        Assert.Equal(2, _calls.Count);
    }
}
