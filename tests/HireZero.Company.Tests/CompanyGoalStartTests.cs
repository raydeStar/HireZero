using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyGoalStartTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-goal-start-");

    private CompanyStore Store() =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-goal-runtime"
        );

    private sealed record Prepared(CompanyMessage Message, CompanyTask Task, CompanyAttempt Attempt);

    private static CompanyCommand C(string action) => MeasuredQueueChecks.C(action);

    private static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    private static CompanyCommand Goal() =>
        C("goal.prepare") with
        {
            RoleId = "marketing",
            Skill = "marketing-starter-plan",
            CreditAllowance = 1,
            Decision = "use-details-and-prepare",
            Body = CompanyJson.Encode(
                new CompanyGoalStartInput(
                    "Fictional paper studio",
                    "We sell paper craft kits.",
                    "Prepare a useful weekly content plan.",
                    "Fictional marketer",
                    "Marketing",
                    "Prepare internal content drafts.",
                    new()
                    {
                        ["purpose"] = "Useful content",
                        ["audience"] = "Unknown",
                        ["positioning"] = "Use approved facts",
                        ["voice"] = "Clear",
                        ["claims"] = "No invented claims",
                        ["constraints"] = "Internal drafts only",
                    },
                    "Prepare a useful weekly content plan. Include three complete fictional posts and a learning goal."
                )
            ),
        };

    [Fact]
    public void OwnerGoalCreatesACompleteReviewableAssignmentWithoutRunningAModel()
    {
        var store = Store();
        var command = Goal();
        var result = store.Apply(command);
        var prepared = CompanyJson.Decode<Prepared>(result);
        Assert.Equal(result, store.Apply(command));
        Assert.Single(MeasuredQueueChecks.Items<CompanyDepartment>(store, "departments"));
        Assert.Equal("active", Assert.Single(MeasuredQueueChecks.Items<CompanyEmployee>(store, "employees")).State);
        Assert.Equal("confirmed", store.BusinessIdentity()!.State);
        var profile = Assert.Single(MeasuredQueueChecks.Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.Equal("approved", profile.State);
        Assert.Equal(7, profile.Fields.Count(f => f.Verification == "unknown" && f.Value == "Unknown"));
        Assert.All(profile.Fields, field => Assert.Empty(field.Citations));
        var call = store.TextCall(prepared.Attempt.Id);
        Assert.Contains("We sell paper craft kits.", call.Context.CompanyKnowledge);
        Assert.Contains("Prepare a useful weekly content plan.", call.Context.Input);
        Assert.Equal("proposed", prepared.Attempt.Measured!.State);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.Null(store.Claim(prepared.Task.EmployeeId, true));
        Assert.Equal(result, Store().Apply(command));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void FailedPreparationRollsBackTheEntireSetup()
    {
        var store = Store();
        Assert.Throws<WorkspaceException>(() => store.Apply(Goal() with { CreditAllowance = 0 }));
        Assert.Null(store.BusinessIdentity());
        Assert.Empty(MeasuredQueueChecks.Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyEmployee>(store, "employees"));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyDepartment>(store, "departments"));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyKnowledge>(store, "knowledge"));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyTask>(store, "tasks"));
    }

    [Fact]
    public void LaterGoalsReuseTheEmployeeAndDoNotRewriteApprovedBusinessFacts()
    {
        var store = Store();
        var first = A<Prepared>(store, Goal());
        store.Apply(
            C("measured.run") with
            {
                Id = first.Attempt.Id,
                ExpectedVersion = first.Attempt.Version,
                Digest = first.Attempt.Measured!.Digest,
                Decision = "accept-soft-limits",
            }
        );
        store.Finish(
            first.Attempt.Id,
            new ModelReply("Fictional finished draft.", 20, "fictional-goal-result", new(10, 10))
        );
        var profile = Assert.Single(MeasuredQueueChecks.Items<CompanyBusinessProfile>(store, "businessProfiles"));
        var knowledge = MeasuredQueueChecks
            .Items<CompanyKnowledge>(store, "knowledge")
            .Single(k => k.Id == profile.KnowledgeId);
        var next = Goal() with
        {
            Id = knowledge.Id,
            ExpectedVersion = knowledge.Version,
            Digest = knowledge.Digest,
            EmployeeId = first.Task.EmployeeId,
        };
        var input = CompanyJson.Decode<CompanyGoalStartInput>(next.Body!);
        next = next with
        {
            Body = CompanyJson.Encode(
                input with
                {
                    Goal = "Draft a new campaign.",
                    Prompt = "Draft a new campaign. Include finished examples.",
                }
            ),
        };
        var second = A<Prepared>(store, next);
        Assert.Contains("Draft a new campaign.", store.TextCall(second.Attempt.Id).Context.Input);
        Assert.Equal(first.Task.EmployeeId, second.Task.EmployeeId);
        Assert.NotEqual(first.Task.ThreadId, second.Task.ThreadId);
        Assert.Empty(store.TextCall(second.Attempt.Id).Context.History);
        Assert.NotEqual(first.Task.Id, second.Task.Id);
        Assert.Single(MeasuredQueueChecks.Items<CompanyEmployee>(store, "employees"));
        Assert.Equal(
            CompanyJson.Encode(profile),
            CompanyJson.Encode(
                Assert.Single(MeasuredQueueChecks.Items<CompanyBusinessProfile>(store, "businessProfiles"))
            )
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(next with { RequestId = Guid.NewGuid().ToString(), Digest = "stale" })
        );
        Assert.Equal(2, MeasuredQueueChecks.Items<CompanyTask>(store, "tasks").Length);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void ExistingUnapprovedBusinessResearchIsNeverAutomaticallyConfirmed()
    {
        var store = Store();
        store.Apply(
            C("business.identify") with
            {
                Body = CompanyJson.Encode(new CompanyBusinessInput("Fictional prior studio", null, "Review first.")),
            }
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Goal()));
        Assert.Equal("unconfirmed", store.BusinessIdentity()!.State);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.Empty(MeasuredQueueChecks.Items<CompanyEmployee>(store, "employees"));
    }

    [Fact]
    public void GoalSetupRequiresOwnerAuthorityAndAnInternalSkill()
    {
        var store = Store();
        Assert.Equal(CompanyRoles.Owner, CompanyAccess.Required("goal.prepare"));
        store.Apply(C("member.invite") with { Body = "approver@example.invalid", Decision = CompanyRoles.Approver });
        store.SignIn("https://idp.example.invalid|approver", "approver@example.invalid", true, "Fictional approver");
        Assert.Throws<WorkspaceException>(() => store.Apply(Goal(), "https://idp.example.invalid|approver", out _));
        Assert.Throws<WorkspaceException>(() => store.Apply(Goal() with { Decision = null }));
        Assert.Throws<WorkspaceException>(() => store.Apply(Goal() with { Skill = "publish" }));
        Assert.Null(store.BusinessIdentity());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
