using HireZero.Core;
using HireZero.Roles.Marketing;
using Microsoft.Data.Sqlite;

public sealed class CompanyAssignmentTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-assignment-");

    private sealed record Prepared(CompanyMessage Message, CompanyTask Task, CompanyAttempt Attempt);

    private CompanyStore Store(string id = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-assignment-model",
            companyId: id
        );

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static CompanyKnowledge Knowledge(CompanyStore s)
    {
        var draft = A<CompanyKnowledge>(
            s,
            C("knowledge.save") with
            {
                Body = CompanyJson.Encode(
                    new
                    {
                        business = "Fictional paper studio",
                        goal = "Prepare a small owner-reviewed launch",
                        background = new string('a', 3000),
                    }
                ),
            }
        );
        return A<CompanyKnowledge>(
            s,
            C("knowledge.review") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Digest,
                Decision = "approve",
            }
        );
    }

    private static CompanyCommand Prepare(CompanyTask seed, CompanyKnowledge knowledge) =>
        C("assignment.prepare") with
        {
            EmployeeId = seed.EmployeeId,
            ThreadId = seed.ThreadId,
            Skill = MarketingModule.StarterPlan,
            Body =
                "Prepare a one-week marketing starter plan using approved facts. Separate observations, hypotheses, sample copy and questions.",
            Id = knowledge.Id,
            ExpectedVersion = knowledge.Version,
            Digest = knowledge.Digest,
            CreditAllowance = 1,
        };

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    [Fact]
    public void AnAssignmentIsAtomicBoundedAndNeedsArtifactReviewBeforeCompletion()
    {
        var store = Store();
        var seed = MeasuredQueueChecks.Seed(store);
        var knowledge = Knowledge(store);
        var command = Prepare(seed, knowledge);
        var receipt = store.Apply(command);
        Assert.Equal(receipt, store.Apply(command, out var appliedAgain));
        Assert.False(appliedAgain);
        var prepared = CompanyJson.Decode<Prepared>(receipt);
        Assert.Equal("work", prepared.Task.Kind);
        Assert.Equal(MarketingModule.StarterPlan, prepared.Task.Skill);
        Assert.Equal(knowledge.Id, prepared.Task.CompanyKnowledgeId);
        Assert.Equal(2, MeasuredQueueChecks.Items<CompanyTask>(store, "tasks").Length);
        Assert.Equal("proposed", prepared.Attempt.Measured!.State);
        Assert.Equal(CompanyMeasuredPolicy.Deliverable, prepared.Attempt.Measured.Policy);
        Assert.InRange(prepared.Attempt.Measured.ApplicationBytes, 4097, 24 * 1024);
        var call = Store().TextCall(prepared.Attempt.Id);
        Assert.Contains("at most 500 words", call.Instructions);
        Assert.DoesNotContain("at most 80 words", call.Instructions);
        Assert.Contains("success measure", call.Instructions);
        Assert.Equal(knowledge.Body, call.Context.CompanyKnowledge);
        Assert.All(
            MeasuredQueueChecks.Items<CompanyBudget>(store, "budgets"),
            b =>
            {
                Assert.Equal(0, b.Used);
                Assert.Equal(0, b.Reserved);
            }
        );
        var run = Run(prepared.Attempt);
        store.Apply(run);
        var body =
            "DRAFT FOR INTERNAL REVIEW\n"
            + string.Join(
                "\n",
                Enumerable.Repeat(
                    "Hypothesis: test a small paper demonstration; record questions before deciding next steps.",
                    40
                )
            );
        Assert.True(body.Length > CompanyMeasuredPolicy.Current.VisibleCharacterStop);
        store.Finish(prepared.Attempt.Id, new ModelReply(body, 1100, "fictional-first-deliverable", new(300, 800)));
        var work = store.Work(prepared.Task.Id);
        Assert.Equal("awaiting_review", work.State);
        var artifact = Assert.Single(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.Equal("needs_review", artifact.State);
        Assert.Equal(body, artifact.Body);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("task.complete") with { Id = work.Id, ExpectedVersion = work.Version })
        );
        store.Apply(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        work = store.Work(work.Id);
        store.Apply(C("task.complete") with { Id = work.Id, ExpectedVersion = work.Version });
        Assert.Equal("completed", Store().Work(work.Id).State);
        store.Apply(run, out var replay);
        Assert.False(replay);
        Assert.Single(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void FailedPreparationLeavesNoMessageTaskOrAttemptAndCannotCrossCompanies()
    {
        var store = Store();
        var seed = MeasuredQueueChecks.Seed(store);
        var knowledge = Knowledge(store);
        var command = Prepare(seed, knowledge);
        foreach (
            var bad in new[]
            {
                command with
                {
                    Digest = "wrong",
                },
                command with
                {
                    ExpectedVersion = 0,
                },
                command with
                {
                    Id = null,
                },
                command with
                {
                    Skill = "developer-change",
                },
                command with
                {
                    Skill = PlatformSkills.Chat,
                },
                command with
                {
                    CreditAllowance = 0,
                },
                command with
                {
                    Body = "",
                },
            }
        )
            Assert.Throws<WorkspaceException>(() => store.Apply(bad));
        var neighbor = Store("co-assignment-neighbor");
        Assert.Throws<WorkspaceException>(() => neighbor.Apply(command));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyTask>(neighbor, "tasks"));
        Assert.Single(MeasuredQueueChecks.Items<CompanyTask>(store, "tasks"));
        using var db = new StoreConnection(store.Database.Open(), store.Database);
        Assert.Equal("0", db.Scalar("SELECT count(*) FROM co_messages WHERE company_id=$0", store.CompanyId));
        Assert.Equal("0", db.Scalar("SELECT count(*) FROM co_attempts WHERE company_id=$0", store.CompanyId));
        store.Apply(C("member.invite") with { Body = "viewer@example.invalid", Decision = CompanyRoles.Viewer });
        var viewer = store.SignIn("fictional-assignment-viewer", "viewer@example.invalid", true, "Fictional Viewer")!;
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.Apply(command, viewer.Subject, out _)).Status);
    }

    [Fact]
    public void AChangedBriefFencesPreparationAndItsAlreadyPreparedAssignment()
    {
        var store = Store();
        var seed = MeasuredQueueChecks.Seed(store);
        var knowledge = Knowledge(store);
        var command = Prepare(seed, knowledge);
        var prepared = A<Prepared>(store, command);
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "revoke",
            }
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(command with { RequestId = Guid.NewGuid().ToString("D") }));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(prepared.Attempt)));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
    }

    [Fact]
    public void ARevokedBriefWithholdsLateAssignmentTextButSettlesReportedUsage()
    {
        var store = Store();
        var seed = MeasuredQueueChecks.Seed(store);
        var knowledge = Knowledge(store);
        var prepared = A<Prepared>(store, Prepare(seed, knowledge));
        store.Apply(Run(prepared.Attempt));
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "revoke",
            }
        );
        store.Finish(
            prepared.Attempt.Id,
            new ModelReply("Fictional obsolete plan", 240, "fictional-withheld-plan", new(160, 80))
        );
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.Equal("withheld", store.Attempt(prepared.Attempt.Id).Measured!.State);
        var budget = MeasuredQueueChecks.Items<CompanyBudget>(store, "budgets").Single(b => b.Id == "company-total");
        Assert.Equal(240, budget.Used);
        Assert.Equal(0, budget.Reserved);
    }

    [Fact]
    public void DeveloperFirstAssignmentIsBoundedAdviceWithoutRepositoryAuthority() =>
        CheckRoleStarter("developer", "technical-starter-plan", "not browsed");

    [Fact]
    public void CoordinatorFirstAssignmentWorksBeforeAnyOtherEmployeeExists() =>
        CheckRoleStarter("ceo", "company-priorities", "not a delegation JSON");

    private void CheckRoleStarter(string roleId, string skill, string instruction)
    {
        var store = Store();
        var knowledge = Knowledge(store);
        var role = store.Roles.Single(r => r.Id == roleId);
        var department = A<CompanyDepartment>(
            store,
            C("department.create") with
            {
                Name = "Fictional starter department",
            }
        );
        var context = A<CompanyKnowledge>(
            store,
            C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(role.OnboardingFields.ToDictionary(f => f, f => "Fictional approved " + f)),
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = context.Id,
                ExpectedVersion = context.Version,
                Digest = context.Digest,
                Decision = "approve",
            }
        );
        var employee = A<CompanyEmployee>(
            store,
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = roleId,
                Name = "Fictional first specialist",
                Responsibility = "Prepare internal advice",
            }
        );
        var thread = Assert.Single(MeasuredQueueChecks.Items<CompanyThread>(store, "threads"));
        var prepared = A<Prepared>(
            store,
            C("assignment.prepare") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = skill,
                Body = "Prepare a modest first plan from the approved brief.",
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                CreditAllowance = 1,
            }
        );
        Assert.Equal(CompanyMeasuredPolicy.Deliverable, prepared.Attempt.Measured!.Policy);
        Assert.Equal(knowledge.Id, prepared.Task.CompanyKnowledgeId);
        Assert.Null(prepared.Task.Resource);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyPlan>(store, "plans"));
        var call = store.TextCall(prepared.Attempt.Id);
        Assert.Contains(instruction, call.Instructions);
        Assert.Contains("Fictional paper studio", call.Context.CompanyKnowledge);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        store.Apply(Run(prepared.Attempt));
        store.Finish(
            prepared.Attempt.Id,
            new ModelReply("Fictional first assessment for owner review.", 100, "fictional-role-starter", new(70, 30))
        );
        Assert.Equal(
            "needs_review",
            Assert.Single(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts")).State
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("task.complete") with
                {
                    Id = prepared.Task.Id,
                    ExpectedVersion = store.Work(prepared.Task.Id).Version,
                }
            )
        );
        Assert.Empty(MeasuredQueueChecks.Items<CompanyPlan>(store, "plans"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
