using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyDelegationTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-delegation-");
    private DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private CompanyStore Store(
        string company = CompanyStore.DefaultCompanyId,
        bool sandbox = false,
        bool browser = false
    ) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-delegation",
            companyId: company
        )
        {
            Clock = () => _now,
            SandboxRuntimeIdentity = sandbox ? "fictional-browser-sandbox-v1" : null,
            BrowserRuntimeIdentity = browser ? "fictional-public-browser-v1" : null,
            BrowserExecutionAvailable = () => browser,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static T[] Items<T>(CompanyStore s, string key) => MeasuredQueueChecks.Items<T>(s, key);

    private static (CompanyEmployee Employee, CompanyThread Thread) Hire(CompanyStore s, string roleId)
    {
        var d = A<CompanyDepartment>(s, C("department.create") with { Name = "Fictional " + roleId });
        var role = s.Roles.Single(r => r.Id == roleId);
        var knowledge = A<CompanyKnowledge>(
            s,
            C("knowledge.save") with
            {
                DepartmentId = d.Id,
                Body = CompanyJson.Encode(
                    role.OnboardingFields.ToDictionary(f => f, f => "Private " + roleId + " " + f)
                ),
            }
        );
        s.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var e = A<CompanyEmployee>(
            s,
            C("employee.hire") with
            {
                DepartmentId = d.Id,
                RoleId = roleId,
                Name = "Fictional " + roleId,
                Responsibility = "Bounded internal review",
            }
        );
        return (e, Items<CompanyThread>(s, "threads").Single(t => t.EmployeeId == e.Id));
    }

    private static (CompanyPlan Plan, CompanyArtifact Source) Plan(
        CompanyStore s,
        bool dependent = true,
        long budget = 44000
    )
    {
        var ceo = Hire(s, "ceo");
        var marketing = Hire(s, "marketing");
        var developer = Hire(s, "developer");
        var task = A<CompanyTask>(
            s,
            C("task.create") with
            {
                EmployeeId = ceo.Employee.Id,
                ThreadId = ceo.Thread.Id,
                Skill = "company-plan",
                Body = "Plan a fictional launch",
            }
        );
        var proposal = new CompanyPlanProposal(
            "Fictional launch",
            [
                new(marketing.Employee.Id, marketing.Thread.Id, "chat", "Draft a campaign hypothesis", [], []),
                new(
                    developer.Employee.Id,
                    developer.Thread.Id,
                    "chat",
                    "Review website implications using the approved campaign",
                    [],
                    dependent ? [0] : []
                ),
            ],
            budget
        );
        var artifact = A<CompanyArtifact>(
            s,
            C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(proposal),
            }
        );
        artifact = Review(s, artifact, "approve");
        var plan = A<CompanyPlan>(
            s,
            C("plan.from-artifact") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
            }
        );
        return (plan, artifact);
    }

    private static CompanyArtifact Review(CompanyStore s, CompanyArtifact a, string decision) =>
        A<CompanyArtifact>(
            s,
            C("artifact.review") with
            {
                Id = a.Id,
                ExpectedVersion = a.Version,
                Digest = a.Digest,
                Decision = decision,
            }
        );

    private static CompanyPlan Approve(CompanyStore s, CompanyPlan p) =>
        A<CompanyPlan>(s, C("plan.approve") with { Id = p.Id, ExpectedVersion = p.Version, Digest = p.Digest });

    private static CompanyAttempt Prepare(CompanyStore s, string id)
    {
        var task = s.Work(id);
        return A<CompanyAttempt>(
            s,
            C("measured.prepare") with
            {
                Id = id,
                ExpectedVersion = task.Version,
                CreditAllowance = 1,
            }
        );
    }

    private static CompanyCommand Run(CompanyAttempt a) =>
        C("measured.run") with
        {
            Id = a.Id,
            ExpectedVersion = a.Version,
            Digest = a.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    private static void Finish(CompanyStore s, CompanyAttempt a, string body = "Fictional approved campaign result") =>
        s.Finish(a.Id, new(body, 100, "fictional-delegation-" + a.Id, new(70, 30, BilledUsd: 0.001m)));

    private static CompanyArtifact Complete(CompanyStore s, CompanyAttempt a)
    {
        var artifact = Review(s, Items<CompanyArtifact>(s, "artifacts").Single(x => x.TaskId == a.TaskId), "approve");
        s.Apply(C("task.complete") with { Id = a.TaskId, ExpectedVersion = s.Work(a.TaskId).Version });
        return artifact;
    }

    [Fact]
    public void DependentMeasuredWorkSharesOnlyApprovedResultsAndCompletesItsParent()
    {
        var s = Store();
        var (draft, source) = Plan(s);
        Assert.Equal(source.Id, draft.Source!.ArtifactId);
        Assert.Empty(draft.TaskIds);
        var p = Approve(s, draft);
        Assert.Equal(_now.AddDays(7), p.Deadline);
        Assert.Throws<WorkspaceException>(() => Prepare(s, p.TaskIds[1]));
        Assert.Equal(
            0,
            JsonSerializer
                .SerializeToElement(s.TaskDetail(p.TaskIds[1]), CompanyJson.Options)
                .GetProperty("attempts")
                .GetArrayLength()
        );
        var first = Prepare(s, p.TaskIds[0]);
        Assert.Equal(CompanyMeasuredPolicy.Deliverable, first.Measured!.Policy);
        Assert.DoesNotContain("Private developer", CompanyJson.Encode(s.TextCall(first.Id).Context));
        s.Apply(Run(first));
        Finish(s, first);
        Assert.Throws<WorkspaceException>(() => Prepare(s, p.TaskIds[1]));
        var artifact = Complete(s, first);
        _now = _now.AddDays(1);
        var second = Prepare(s, p.TaskIds[1]);
        var context = s.TextCall(second.Id).Context;
        Assert.Equal(artifact.Id, Assert.Single(context.Evidence).Id);
        Assert.DoesNotContain("Private marketing", CompanyJson.Encode(context));
        Assert.Contains("Private developer", context.DepartmentKnowledge);
        var command = Run(second);
        s.Apply(command);
        Finish(s, second);
        Complete(s, second);
        s.Apply(command, out var replay);
        Assert.False(replay);
        var completed = Items<CompanyPlan>(Store(), "plans").Single();
        Assert.Equal("completed", completed.State);
        var root = Items<CompanyBudget>(s, "budgets").Single(b => b.Id == p.RootId);
        Assert.Equal(44000, root.Limit);
        Assert.Equal(200, root.Used);
        Assert.Equal(0, root.Reserved);
        Assert.True(s.VerifyAudit().Intact);
    }

    [Fact]
    public void SharedAllowanceNeverGrowsAndNormalSettlementDoesNotInvalidateAnotherPreparedChild()
    {
        var s = Store();
        var p = Approve(s, Plan(s, false).Plan);
        var first = Prepare(s, p.TaskIds[0]);
        var second = Prepare(s, p.TaskIds[1]);
        s.Apply(Run(first));
        Finish(s, first);
        s.Apply(Run(second));
        Finish(s, second);
        Assert.Equal(44000, Items<CompanyBudget>(s, "budgets").Single(b => b.Id == p.RootId).Limit);
        var limited = Store("co-limited");
        var small = Approve(limited, Plan(limited, false, 22000).Plan);
        var a = Prepare(limited, small.TaskIds[0]);
        var b = Prepare(limited, small.TaskIds[1]);
        limited.Apply(Run(a));
        Assert.Throws<WorkspaceException>(() => limited.Apply(Run(b)));
        Assert.Equal("proposed", limited.Attempt(b.Id).Measured!.State);
        var root = Items<CompanyBudget>(limited, "budgets").Single(x => x.Id == small.RootId);
        Assert.Equal(22000, root.Limit);
        Assert.Equal(22000, root.Reserved);
        Finish(limited, a);
        Assert.Throws<WorkspaceException>(() => limited.Apply(Run(b)));
        Assert.Equal(22000, Items<CompanyBudget>(limited, "budgets").Single(x => x.Id == small.RootId).Limit);
    }

    [Fact]
    public void RevokedProposalFencesApprovalDispatchAndLateResultsWithoutLosingUsage()
    {
        var s = Store();
        var (draft, source) = Plan(s);
        source = Review(s, source, "revoke");
        Assert.Throws<WorkspaceException>(() => Approve(s, draft));
        Assert.DoesNotContain(Items<CompanyTask>(s, "tasks"), t => t.PlanId == draft.Id);
        source = Review(s, source, "approve");
        Assert.Throws<WorkspaceException>(() => Approve(s, draft)); // The old exact version stays revoked.
        var fresh = A<CompanyPlan>(
            s,
            C("plan.from-artifact") with
            {
                Id = source.Id,
                ExpectedVersion = source.Version,
                Digest = source.Digest,
            }
        );
        var p = Approve(s, fresh);
        var a = Prepare(s, p.TaskIds[0]);
        s.Apply(Run(a));
        Review(s, source, "revoke");
        Finish(s, a);
        Assert.Equal("withheld", s.Attempt(a.Id).Measured!.State);
        Assert.DoesNotContain(Items<CompanyArtifact>(s, "artifacts"), x => x.TaskId == a.TaskId);
        Assert.Equal(100, s.Usage().Current.Tokens);
        Assert.Equal(0, s.Usage().Current.HeldTokens);
    }

    [Fact]
    public void RevokedDependencyWithholdsItsDependentLateOutput()
    {
        var s = Store();
        var p = Approve(s, Plan(s).Plan);
        var first = Prepare(s, p.TaskIds[0]);
        s.Apply(Run(first));
        Finish(s, first);
        var artifact = Complete(s, first);
        var second = Prepare(s, p.TaskIds[1]);
        s.Apply(Run(second));
        Review(s, artifact, "revoke");
        Finish(s, second);
        Assert.Equal("withheld", s.Attempt(second.Id).Measured!.State);
        Assert.DoesNotContain(Items<CompanyArtifact>(s, "artifacts"), a => a.TaskId == second.TaskId);
        Assert.Equal(200, s.Usage().Current.Tokens);
        Assert.Equal(0, s.Usage().Current.HeldTokens);
    }

    [Fact]
    public void ExpiredUnsentPacketsCanBePreparedAgainButUnknownRequestsCannot()
    {
        var s = Store();
        var p = Approve(s, Plan(s).Plan);
        var first = Prepare(s, p.TaskIds[0]);
        _now = _now.AddMinutes(11);
        s.Claim(first.EmployeeId, false);
        Assert.Equal("expired", s.Attempt(first.Id).Measured!.State);
        var fresh = Prepare(s, first.TaskId);
        Assert.NotEqual(first.Id, fresh.Id);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(first)));
        s.Apply(Run(fresh));
        s.FailAttempt(fresh.Id, "provider-accounting-unconfirmed", true);
        Assert.Throws<WorkspaceException>(() => Prepare(s, fresh.TaskId));
        Assert.Equal(22000, s.Usage().Current.HeldTokens);
    }

    [Fact]
    public void PreparationCannotCrossCompaniesAndCoordinatorPacketsOmitPrivateColleagueContext()
    {
        var s = Store();
        var (draft, source) = Plan(s);
        var p = Approve(s, draft);
        var other = Store("co-other");
        Assert.Throws<WorkspaceException>(() => Prepare(other, p.TaskIds[0]));
        var original = Items<CompanyTask>(s, "tasks").Single(t => t.Id == source.TaskId);
        var ceoTask = A<CompanyTask>(
            s,
            C("task.create") with
            {
                EmployeeId = original.EmployeeId,
                ThreadId = original.ThreadId,
                Skill = "company-plan",
                Body = "Propose a fictional follow-up",
            }
        );
        var prepared = Prepare(s, ceoTask.Id);
        var context = s.TextCall(prepared.Id).Context;
        Assert.Contains(context.Colleagues!, c => c.RoleId == "developer" && c.Skills!.Contains("chat"));
        Assert.DoesNotContain("Private marketing", CompanyJson.Encode(context));
        Assert.DoesNotContain("Private developer", CompanyJson.Encode(context));
        Assert.Equal(CompanyMeasuredPolicy.Deliverable, prepared.Measured!.Policy);
        s.Apply(C("plan.cancel") with { Id = p.Id, ExpectedVersion = p.Version });
        Assert.Throws<WorkspaceException>(() => Prepare(s, p.TaskIds[0]));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
