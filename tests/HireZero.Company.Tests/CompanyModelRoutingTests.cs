using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;

public sealed partial class CompanyModelConnectionTests
{
    private const string AnalysisRoute = "openai/fictional-sol";

    private static CompanyTask AnalysisTask(CompanyStore store)
    {
        T A<T>(CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));
        var department = A<CompanyDepartment>(C("department.create") with { Name = "Fictional planning" });
        var knowledge = A<CompanyKnowledge>(
            C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(
                    new
                    {
                        purpose = "Paper craft workshops",
                        mandate = "Draft priorities",
                        constraints = "Internal advice only",
                    }
                ),
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var employee = A<CompanyEmployee>(
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "ceo",
                Name = "Fictional planner",
                Responsibility = "Propose internal priorities",
            }
        );
        var thread = MeasuredQueueChecks
            .Items<CompanyThread>(store, "threads")
            .Single(t => t.EmployeeId == employee.Id);
        return A<CompanyTask>(
            C("task.create") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = "company-priorities",
                Body = "Propose three priorities from the supplied fictional facts.",
            }
        );
    }

    [Fact]
    public void AnalysisChoiceRequiresCompatiblePricesAndPersistsWithoutCallingAProvider()
    {
        using var fake = new CompanyModelFixture();
        var connection = Connection(handler: fake);
        Assert.Throws<WorkspaceException>(() => connection.Save(Input() with { AnalysisRoute = "openai/unpriced" }));
        Assert.Throws<WorkspaceException>(() =>
            connection.Save(Input() with { AnalysisRoute = "anthropic/fictional-wisp" })
        );
        var saved = connection.Save(Input() with { AnalysisRoute = AnalysisRoute });
        Assert.Equal(AnalysisRoute, saved.AnalysisRoute);
        Assert.Equal(AnalysisRoute, Connection().Routing.AnalysisModel);
        Assert.Equal("openai/fictional-luna", connection.Routing.TextModel);
        Assert.Null(Connection("fictional-second").Routing.AnalysisModel);
        Assert.Empty(fake.Requests);
        connection.Save(Input(1));
        Assert.Null(Connection().Routing.AnalysisModel);
        var hostChoice = new CompanyModelConnection(
            "fictional-host-choice",
            new UnavailableModelProvider(),
            new CompanyModelRouting { AnalysisModel = "openai/fictional-host-analysis" },
            CompanyModelFixture.Models,
            fake
        );
        hostChoice.Initialize(_root.Path, _protection);
        hostChoice.Save(Input());
        Assert.Null(hostChoice.Routing.AnalysisModel);
    }

    [Fact]
    public void QueuedAnalysisUsesCurrentChoiceAndCannotRunThroughAutomaticClaim()
    {
        var connection = Connection();
        connection.Save(Input());
        var store = Store(connection);
        var analysis = AnalysisTask(store);
        var routine = MeasuredQueueChecks.Seed(store);
        Assert.Equal(connection.Routing.TextModel, analysis.ModelRoute);
        connection.Save(Input(1) with { AnalysisRoute = AnalysisRoute });
        Assert.Null(store.Claim(analysis.EmployeeId, true));
        var prepared = Prepare(store, analysis);
        Assert.Equal(AnalysisRoute, prepared.Measured!.Route);
        Assert.Equal(AnalysisRoute, store.Work(analysis.Id).ModelRoute);
        Assert.Equal(connection.Routing.TextModel, Prepare(store, routine).Measured!.Route);
        Assert.Empty(store.Usage().Entries);
    }

    [Fact]
    public void ChangingAnalysisChoiceExpiresApprovalAndUsesRoutineOnlyAfterFreshReview()
    {
        var connection = Connection();
        connection.Save(Input() with { AnalysisRoute = AnalysisRoute });
        var store = Store(connection);
        var task = AnalysisTask(store);
        var old = Prepare(store, task);
        connection.Save(Input(1));
        store.Dashboard();
        Assert.Equal("expired", store.Attempt(old.Id).Measured!.State);
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(old)));
        var fresh = Prepare(store, task);
        Assert.Equal("openai/fictional-luna", fresh.Measured!.Route);
        Assert.NotEqual(old.Measured!.Digest, fresh.Measured.Digest);
    }

    [Fact]
    public async Task ExactAnalysisModelReachesProviderAndItsUsageReceipt()
    {
        using var fake = new CompanyModelFixture();
        var connection = Connection(handler: fake);
        connection.Save(Input() with { AnalysisRoute = AnalysisRoute });
        var store = Store(connection);
        var task = AnalysisTask(store);
        var prepared = Prepare(store, task);
        using var scheduler = new CompanyScheduler(store, connection);
        await scheduler.RunMeasuredOnceAsync(Run(prepared), connection, Token);
        Assert.Equal("fictional-sol", Assert.Single(fake.RequestedModels));
        Assert.Equal(Input().Key, Assert.Single(fake.Requests).Key);
        Assert.Equal("completed", store.Attempt(prepared.Id).Measured!.State);
        var receipt = Assert.Single(store.Usage().Entries);
        Assert.Equal(AnalysisRoute, receipt.Route);
        Assert.Equal("company-key", receipt.Funding!.Source);
        Assert.Equal(.00324m, receipt.EstimatedUsd);
        var conversation = CompanyJson.Decode<JsonElement>(
            store.Apply(
                C("conversation.prepare") with
                {
                    EmployeeId = task.EmployeeId,
                    ThreadId = task.ThreadId,
                    Scope = "employee",
                    Body = "Explain the priorities briefly.",
                    CreditAllowance = 100,
                }
            )
        );
        Assert.Equal(
            connection.Routing.TextModel,
            conversation.GetProperty("attempt").GetProperty("measured").GetProperty("route").GetString()
        );
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task FailedAnalysisNeverFallsBackToRoutineOrReplaysAutomatically()
    {
        using var fake = new CompanyModelFixture { Unknown = true };
        var connection = Connection(handler: fake);
        connection.Save(Input() with { AnalysisRoute = AnalysisRoute });
        var store = Store(connection);
        var task = AnalysisTask(store);
        var prepared = Prepare(store, task);
        using var scheduler = new CompanyScheduler(store, connection);
        await scheduler.RunMeasuredOnceAsync(Run(prepared), connection, Token);
        Assert.Equal("fictional-sol", Assert.Single(fake.RequestedModels));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(prepared)));
        Assert.Null(store.Claim(task.EmployeeId, true));
        Assert.Single(fake.Requests);
        Assert.Equal(1, store.Usage().Current.HeldJobs);
    }

    [Fact]
    public async Task NativeAdapterSendsTheApprovedAnalysisModelToBothThreadAndTurn()
    {
        var nativeRoute = "openai/gpt-6.1-sol";
        var model = new CompanyCodexConnection(
            new(
                MeasuredQueueChecks.Fake,
                CompanyCodingFactory.Identity(MeasuredQueueChecks.Fake),
                Path.Combine(_root.Path, "native"),
                SyntheticMode: "company-analysis-routing",
                QualificationCreditBudget: CompanyCodexConnection.QualificationPlanningCreditsPerTurn
            ),
            new CompanyModelRouting() { AnalysisModel = nativeRoute }
        );
        var (call, _) = Packet(Connection());
        var receipt = await model.CompletePilotAsync(call with { ModelRoute = nativeRoute }, Token);
        Assert.Equal("gpt-6.1-sol", receipt.Model);
        Assert.True(receipt.TotalTokens > 0);
    }
}
