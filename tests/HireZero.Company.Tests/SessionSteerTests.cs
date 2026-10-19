using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Tools.AgentApps;
using Microsoft.Data.Sqlite;

// Steering from the company workspace (docs/adr/0016-publishing-one-approved-call.md): a session in an approved
// listing, an approved draft, an approved publication, then one read-only send authorized by it. The agent-app tools
// are fakes behind the real registry and publication verifier; sessions are fictional.
public sealed class SessionSteerTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("session-steer-");
    private readonly List<SteerMessage> _sent = [];
    private bool _ready = true;
    private bool _sendFails;

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
        CompanyThread Thread,
        CompanyArtifact Listing
    )
    {
        public CompanyExecutionServices Services => new(Store, Registry, null);
    }

    private async Task<Company> Hire()
    {
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        registry.Register(
            AgentAppsTools.ListSessions,
            (_, _) =>
                Task.FromResult(
                    new[]
                    {
                        new SessionSummary(
                            "claude:s-1",
                            "claude",
                            "Fictional rounding fix",
                            "quotes-service",
                            null,
                            null,
                            "idle",
                            "2026-10-18T08:00:00Z",
                            []
                        ),
                    }
                )
        );
        registry.Register(
            AgentAppsTools.CheckSteer,
            (_, _) => Task.FromResult(new SteerCheck(_ready, _ready ? "Ready." : "It is working in its app right now."))
        );
        registry.Register(
            AgentAppsTools.SendReadOnly,
            (message, _) =>
            {
                _sent.Add(message);
                return _sendFails
                    ? throw new TimeoutException("The fictional app did not answer.")
                    : Task.FromResult(new SteerReceipt("run-1", "running"));
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
                Responsibility = "Keep the owner's fictional agent sessions moving",
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
                Name = "Sessions",
            }
        );
        var action = Apply<CompanyTask>(
            store,
            Cmd("action.start") with
            {
                EmployeeId = developer.Id,
                ThreadId = thread.Id,
                Skill = CompanySessions.ReadAction,
                Body = JsonSerializer.Serialize(new { app = "claude" }),
            }
        );
        var output = await Module.RunActionAsync(
            CompanySessions.ReadAction,
            new Dictionary<string, string> { ["app"] = "claude" },
            registry.For(Module.Manifest, developer.Id, action.Id).ReadOnly(),
            Token
        );
        var listing = store.CompleteAction(action.Id, output);
        listing = Approve<CompanyArtifact>(store, "artifact.review", listing.Id, listing.Version, listing.Digest);
        return new(store, registry, developer, thread, listing);
    }

    private static CompanyArtifact Draft(
        Company company,
        string sessionId,
        string message = "Explain the rounding change."
    )
    {
        var store = company.Store;
        var task = Apply<CompanyTask>(
            store,
            Cmd("task.create") with
            {
                EmployeeId = company.Developer.Id,
                ThreadId = company.Thread.Id,
                Skill = CompanySteer.Skill,
                Body = "Ask the session to explain its change.",
                Evidence = [company.Listing.Id],
            }
        );
        var draft = Apply<CompanyArtifact>(
            store,
            Cmd("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = JsonSerializer.Serialize(new { sessionId, message }),
            }
        );
        return Approve<CompanyArtifact>(store, "artifact.review", draft.Id, draft.Version, draft.Digest);
    }

    private async Task<CompanyPublication> Approved(Company company, CompanyArtifact draft)
    {
        var p = (CompanyPublication)await Module.ExecuteAsync(ExecutionStep.Propose, draft.Id, company.Services, Token);
        return Approve<CompanyPublication>(company.Store, "publication.review", p.Id, p.Version, p.Digest);
    }

    private Task<object> Publish(Company company, CompanyPublication p) =>
        Module.ExecuteAsync(ExecutionStep.Publish, p.Id, company.Services, Token);

    [Fact]
    public async Task AnApprovedReadOnlyMessageIsSentOnce()
    {
        var company = await Hire();
        var publication = await Approved(company, Draft(company, "claude:s-1"));
        Assert.Empty(publication.Calls);
        Assert.NotNull(publication.ApprovedExternal);
        Assert.Contains("read-only", publication.Summary);
        var sent = (CompanyPublication)await Publish(company, publication);
        Assert.Equal("published", sent.State);
        Assert.Contains("run-1", sent.Reason);
        Assert.Equal([new SteerMessage("claude:s-1", "Explain the rounding change.")], _sent);
        Assert.Equal(
            ["agentapps.session.send"],
            company
                .Store.ToolCalls()
                .Where(c => c.Authority == CompanyStore.PublicationAuthorityKind + ":" + publication.Id)
                .Select(c => c.ToolId)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task OnlyListedSessionsAndCleanMessagesCanBeProposed()
    {
        var company = await Hire();
        await Assert.ThrowsAsync<WorkspaceException>(() => Approved(company, Draft(company, "claude:unlisted")));
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Approved(company, Draft(company, "claude:s-1", "Use api_key=sk-proj-fictional0000 to deploy."))
        );
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Propose, company.Listing.Id, company.Services, Token)
        );
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task ABusySessionLeavesThePublicationApproved()
    {
        var company = await Hire();
        var publication = await Approved(company, Draft(company, "claude:s-1"));
        _ready = false;
        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Equal("approved", company.Store.Publication(publication.Id).State);
        Assert.Empty(_sent);
        _ready = true;
        Assert.Equal("published", ((CompanyPublication)await Publish(company, publication)).State);
    }

    [Fact]
    public async Task AnUnconfirmedSendIsUnknownAndNeverRetried()
    {
        var company = await Hire();
        var publication = await Approved(company, Draft(company, "claude:s-1"));
        _sendFails = true;
        await Assert.ThrowsAsync<TimeoutException>(() => Publish(company, publication));
        var unknown = company.Store.Publication(publication.Id);
        Assert.Equal("unknown", unknown.State);
        Assert.Contains("its own app", unknown.Reason);
        company.Store.Recover();
        Assert.Equal(
            "unknown",
            (
                (CompanyPublication)
                    await Module.ExecuteAsync(ExecutionStep.Reconcile, publication.Id, company.Services, Token)
            ).State
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Single(_sent);
    }
}
