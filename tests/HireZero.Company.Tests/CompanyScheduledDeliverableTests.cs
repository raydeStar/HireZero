using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyScheduleTests
{
    [Fact]
    public async Task RecurringDeliverablesKeepTheirSkillLimitsAndSeparateResultApproval()
    {
        var s = Store();
        var input = Definition(Employee(s)) with { Skill = "marketing-starter-plan" };
        var schedule = Approve(s, Draft(s, input));
        Assert.Equal(CompanyMeasuredPolicy.Deliverable, schedule.Authority.Policy);
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(s, model);
        for (var i = 0; i < 2; i++)
        {
            _now = s.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
            Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
            var run = s.ScheduleDetail(schedule.Id).Runs.OrderByDescending(r => r.Slot).First();
            var task = s.Work(run.TaskId!);
            Assert.Equal("work", task.Kind);
            Assert.Equal(input.Skill, task.Skill);
            Assert.Equal("awaiting_review", task.State);
            Assert.Empty(model.Last!.Context.History);
            Assert.Contains("500 words", model.Last.Instructions);
            Assert.DoesNotContain("80 words", model.Last.Instructions);
            var artifact = MeasuredQueueChecks.Items<CompanyArtifact>(s, "artifacts").Single(a => a.TaskId == task.Id);
            Assert.Equal("needs_review", artifact.State);
            Assert.Throws<WorkspaceException>(() =>
                s.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = task.Version })
            );
            s.Apply(
                C("artifact.review") with
                {
                    Id = artifact.Id,
                    ExpectedVersion = artifact.Version,
                    Digest = artifact.Digest,
                    Decision = "approve",
                }
            );
            s.Apply(C("task.complete") with { Id = task.Id, ExpectedVersion = task.Version });
            Assert.Equal("completed", s.Work(task.Id).State);
            s.Apply(
                C("message.send") with
                {
                    EmployeeId = task.EmployeeId,
                    ThreadId = task.ThreadId,
                    Body = "Fictional conversation note outside the recurring request.",
                }
            );
            Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        }
        var budget = MeasuredQueueChecks
            .Items<CompanyBudget>(s, "budgets")
            .Single(b => b.Id == "schedule:" + schedule.Id);
        Assert.Equal(44000, budget.Limit);
        Assert.Equal(60, budget.Used);
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(2, model.Calls);
        Assert.Equal("completed", s.ScheduleDetail(schedule.Id).Schedule.State);
        using var restarted = new CompanyScheduler(Store(), model);
        Assert.False(await restarted.TickSchedulesAsync(TestContext.Current.CancellationToken));
    }

    // A schedule prepared before high-effort deliverables runs under the low-effort policy it was approved with
    // (docs/adr/0134-high-effort-deliverables.md); a new draft gets the current one.
    [Fact]
    public async Task ARecurringDeliverableKeepsTheLowEffortPolicyItWasApprovedWith()
    {
        var s = Store();
        var input = Definition(Employee(s)) with { Skill = "marketing-starter-plan" };
        var current = Draft(s, input);
        Assert.Equal(CompanyMeasuredPolicy.Deliverable, current.Authority.Policy);
        var schedule = Approve(
            s,
            Stored(current with { Authority = current.Authority with { Policy = CompanyMeasuredPolicy.DeliverableV1 } })
        );
        Assert.Equal(CompanyMeasuredPolicy.DeliverableV1, schedule.Authority.Policy);
        var model = new Model(policy: CompanyMeasuredPolicy.DeliverableV1);
        using var scheduler = new CompanyScheduler(s, model);
        _now = s.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, model.Calls);
        Assert.Contains("500 words", model.Last!.Instructions);
    }

    // Saves a draft as an earlier release stored it.
    private CompanySchedule Stored(CompanySchedule draft)
    {
        draft = draft with
        {
            Digest = CompanyJson.Digest(new { input = draft.Definition, authority = draft.Authority }),
        };
        using var db = new SqliteConnection("Data Source=" + Path.Combine(_root.Path, "company.sqlite"));
        db.Open();
        using var update = db.CreateCommand();
        update.CommandText = "UPDATE co_schedules SET payload=$payload WHERE id=$id";
        update.Parameters.AddWithValue("$payload", CompanyJson.Encode(draft));
        update.Parameters.AddWithValue("$id", draft.Id);
        Assert.Equal(1, update.ExecuteNonQuery());
        return draft;
    }

    private static CompanyArtifact ApprovedReference(CompanyStore s)
    {
        var task = MeasuredQueueChecks.Items<CompanyTask>(s, "tasks").First();
        var artifact = A<CompanyArtifact>(
            s,
            C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = "Fictional approved result: include consent-based workshop stories.",
            }
        );
        return A<CompanyArtifact>(
            s,
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
    }

    [Fact]
    public async Task SelectedResultReferencesAreReviewedAndRevocationStopsLaterOccurrences()
    {
        var s = Store();
        var employee = Employee(s);
        var reference = ApprovedReference(s);
        var schedule = Approve(
            s,
            Draft(s, Definition(employee) with { Skill = "marketing-starter-plan", Evidence = [reference.Id] })
        );
        Assert.Contains(reference.Body, schedule.Authority.Context.SelectedResults);
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(s, model);
        _now = schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(reference.Body, Assert.Single(model.Last!.Context.Evidence).Body);
        s.Apply(
            C("artifact.review") with
            {
                Id = reference.Id,
                ExpectedVersion = reference.Version,
                Digest = reference.Digest,
                Decision = "revoke",
            }
        );
        _now = s.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
        Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("blocked", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public void ResultReferencesAreCompanyScopedAndOldScheduleSerializationRemainsStable()
    {
        var s = Store();
        var employee = Employee(s);
        var reference = ApprovedReference(s);
        var other = Store("fictional-other-recurring");
        var otherEmployee = Employee(other);
        Assert.Throws<WorkspaceException>(() =>
            Draft(other, Definition(otherEmployee) with { Evidence = [reference.Id] })
        );
        var legacy = Draft(s, Definition(employee));
        Assert.DoesNotContain("\"skill\"", CompanyJson.Encode(legacy.Definition));
        Assert.DoesNotContain("\"evidence\"", CompanyJson.Encode(legacy.Definition));
        Assert.DoesNotContain("selectedResults", CompanyJson.Encode(legacy.Authority.Context));
        Assert.Equal(CompanyMeasuredPolicy.Conversation, legacy.Authority.Policy);
        Assert.Equal("active", Approve(s, legacy).State);
        Assert.Throws<WorkspaceException>(() => Draft(s, Definition(employee) with { Skill = "fictional-execution" }));
        var action = s.Roles.Single(r => r.Id == "marketing").Manifest!.Actions.First().Id;
        Assert.Throws<WorkspaceException>(() => Draft(s, Definition(employee) with { Skill = action }));
    }

    private static string Planner(CompanyStore s)
    {
        var department = A<CompanyDepartment>(s, C("department.create") with { Name = "Fictional recurring planning" });
        var knowledge = A<CompanyKnowledge>(
            s,
            C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(
                    new
                    {
                        purpose = "Paper workshops",
                        mandate = "Draft priorities",
                        constraints = "Internal planning only",
                    }
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
        return A<CompanyEmployee>(
            s,
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "ceo",
                Name = "Fictional planner",
                Responsibility = "Propose priorities",
            }
        ).Id;
    }

    [Fact]
    public async Task AnalysisModelIsBoundToTheFiniteApprovalAndChangesStopTheSchedule()
    {
        var routing = new CompanyModelRouting { AnalysisModel = "openai/fictional-analysis" };
        var s = Store(routing: () => routing);
        Employee(s);
        var planner = Planner(s);
        Assert.Throws<WorkspaceException>(() =>
            Draft(s, Definition(planner) with { Skill = PlatformSkills.MeetingTurn })
        );
        var schedule = Approve(s, Draft(s, Definition(planner) with { Skill = "company-priorities" }));
        Assert.Equal(routing.AnalysisModel, schedule.Authority.Route);
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(s, model);
        _now = schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(routing.AnalysisModel, model.Last!.ModelRoute);
        routing = routing with { AnalysisModel = null };
        _now = s.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
        Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("blocked", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task UnknownDeliverableCannotEscapeItsOccurrenceThroughRetryOrReassignment()
    {
        var s = Store();
        var schedule = Approve(s, Draft(s, Definition(Employee(s)) with { Skill = "marketing-starter-plan" }));
        var model = new Model(
            (_, _) => throw new ModelTransportException("Fictional lost response"),
            CompanyMeasuredPolicy.Deliverable
        );
        using var scheduler = new CompanyScheduler(s, model);
        _now = schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        var task = s.Work(Assert.Single(s.ScheduleDetail(schedule.Id).Runs).TaskId!);
        foreach (var action in new[] { "task.retry", "task.reassign" })
            Assert.Throws<WorkspaceException>(() =>
                s.Apply(
                    C(action) with
                    {
                        Id = task.Id,
                        ExpectedVersion = task.Version,
                        Decision = "acknowledge-uncertain",
                    }
                )
            );
        Assert.Equal("blocked", s.ScheduleDetail(schedule.Id).Schedule.State);
        Assert.False(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, model.Calls);
        Assert.Equal(1, s.Usage().Current.HeldJobs);
    }
}
