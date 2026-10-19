using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Marketing;
using HireZero.Tools.Advertising;
using Microsoft.Data.Sqlite;

// Approved ad changes (docs/adr/0033-approved-ad-changes.md): a live reading names one change, the owner approves the
// reading and the exact call, and the change runs once after a live re-check; an unknown outcome is settled only by
// reading. The ads account is a fake behind the real registry and publication verifier; campaigns are fictional.
public sealed class MarketingAdChangeTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("ad-change-");
    private readonly List<AdsChange> _changes = [];
    private AdsCampaign _campaign = new("4401", "Fictional search", "enabled", "7701", 100m, false, "XTS");
    private bool _changeFails;

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly MarketingModule Module = new();

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
        CompanyEmployee Marketer,
        CompanyThread Thread
    )
    {
        public CompanyExecutionServices Services => new(Store, Registry, null);
    }

    private Company Hire(bool changesOn = true)
    {
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        registry.Register(AdsTools.Campaign, (r, _) => Task.FromResult(_campaign with { Id = r.CampaignId }));
        if (changesOn)
            registry.Register(
                AdsTools.Change,
                (change, _) =>
                {
                    _changes.Add(change);
                    if (_changeFails)
                        throw new TimeoutException("The fictional ads account did not answer.");
                    _campaign =
                        change.Kind == AdsTools.Pause
                            ? _campaign with
                            {
                                Status = "paused",
                            }
                            : _campaign with
                            {
                                DailyBudget = change.NewDailyBudget,
                            };
                    return Task.FromResult(new AdsChangeResult("fictional/campaigns/" + change.CampaignId));
                }
            );
        var department = Apply<CompanyDepartment>(store, Cmd("department.create") with { Name = "Growth" });
        var fields = store
            .Roles.Single(r => r.Id == "marketing")
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
        var marketer = Apply<CompanyEmployee>(
            store,
            Cmd("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "marketing",
                Name = "Marketing partner",
                Responsibility = "Keep the fictional campaigns on budget",
            }
        );
        var thread = Apply<CompanyThread>(store, Cmd("thread.create") with { EmployeeId = marketer.Id, Name = "Ads" });
        return new(store, registry, marketer, thread);
    }

    private async Task<CompanyArtifact> Reading(Company company, string change, string amount = "0")
    {
        var input = new Dictionary<string, string>
        {
            ["campaign"] = "4401",
            ["change"] = change,
            ["amount"] = amount,
        };
        var action = Apply<CompanyTask>(
            company.Store,
            Cmd("action.start") with
            {
                EmployeeId = company.Marketer.Id,
                ThreadId = company.Thread.Id,
                Skill = MarketingAdChange.Action,
                Body = JsonSerializer.Serialize(input),
            }
        );
        var output = await Module.RunActionAsync(
            MarketingAdChange.Action,
            input,
            company.Registry.For(Module.Manifest, company.Marketer.Id, action.Id).ReadOnly(),
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
    public async Task AnApprovedPauseIsPublishedOnceWithItsApproval()
    {
        var company = Hire();
        var publication = await Approved(company, await Reading(company, AdsTools.Pause));
        Assert.Empty(publication.Calls);
        Assert.StartsWith("Pause campaign 4401 (\"Fictional search\")", publication.Summary);

        var done = (CompanyPublication)await Run(company, ExecutionStep.Publish, publication);

        Assert.Equal("published", done.State);
        Assert.Equal(new AdsChange("4401", AdsTools.Pause, "enabled", null, null, null), Assert.Single(_changes));
        Assert.Equal(
            ["ads.change"],
            company
                .Store.ToolCalls()
                .Where(c => c.Authority == CompanyStore.PublicationAuthorityKind + ":" + publication.Id)
                .Select(c => c.ToolId)
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Single(_changes);
    }

    [Fact]
    public async Task ABudgetMovesAtMostHalfAndNeverOnASharedBudget()
    {
        var company = Hire();
        Assert.Contains(
            "Not eligible: One step may move the budget by at most half: between XTS 50.00 and XTS 150.00.",
            (await Reading(company, AdsTools.Budget, "151")).Body
        );
        Assert.Contains(
            "Not eligible: That is already its daily budget.",
            (await Reading(company, AdsTools.Budget, "100")).Body
        );
        _campaign = _campaign with { BudgetShared = true };
        var shared = await Reading(company, AdsTools.Budget, "120");
        Assert.Contains("Not eligible: Its budget is shared", shared.Body);
        await Assert.ThrowsAsync<WorkspaceException>(() => Approved(company, shared));

        _campaign = _campaign with { BudgetShared = false };
        var publication = await Approved(company, await Reading(company, AdsTools.Budget, "125.004"));
        Assert.Contains("from XTS 100.00 to XTS 125.00", publication.Summary);
        await Run(company, ExecutionStep.Publish, publication);
        Assert.Equal(new AdsChange("4401", AdsTools.Budget, "enabled", "7701", 100m, 125m), Assert.Single(_changes));
    }

    [Fact]
    public async Task ACampaignThatMovedAfterApprovalOrChangesOffSendNothing()
    {
        var company = Hire();
        var publication = await Approved(company, await Reading(company, AdsTools.Budget, "80"));
        _campaign = _campaign with { DailyBudget = 90m };
        await Assert.ThrowsAsync<WorkspaceException>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Equal("approved", company.Store.Publication(publication.Id).State);
        Assert.Empty(_changes);

        SqliteConnection.ClearAllPools();
        var off = Hire(changesOn: false);
        _campaign = _campaign with { DailyBudget = 100m };
        var waiting = await Approved(off, await Reading(off, AdsTools.Pause));
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => Run(off, ExecutionStep.Publish, waiting));
        Assert.Contains("Ad changes are off", error.Message);
        Assert.Equal("approved", off.Store.Publication(waiting.Id).State);
    }

    [Fact]
    public async Task AnUnansweredChangeIsUnknownNeverRetriedAndSettledByReading()
    {
        var company = Hire();
        var publication = await Approved(company, await Reading(company, AdsTools.Pause));
        _changeFails = true;

        await Assert.ThrowsAnyAsync<Exception>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Equal("unknown", company.Store.Publication(publication.Id).State);
        await Assert.ThrowsAsync<WorkspaceException>(() => Run(company, ExecutionStep.Publish, publication));
        Assert.Single(_changes);

        // Still enabled: reconciling reads, changes nothing and stays unknown.
        Assert.Equal("unknown", ((CompanyPublication)await Run(company, ExecutionStep.Reconcile, publication)).State);
        _campaign = _campaign with { Status = "paused" };
        Assert.NotEqual(
            "unknown",
            ((CompanyPublication)await Run(company, ExecutionStep.Reconcile, publication)).State
        );
        Assert.Single(_changes);
    }
}
