using HireZero.Core;

public sealed partial class CompanyScheduleTests
{
    [Fact]
    public void ChangedScopeIsVisibleBeforeTheDueTimeWithoutConsumingOrReapprovingAnything()
    {
        var store = Store();
        var employee = Employee(store);
        var draft = Draft(store, Definition(employee));
        Assert.True(store.ScheduleDetail(draft.Id).Schedule.ReviewCurrent);
        var pausedDraft = A<CompanySchedule>(store, Review(draft, "pause"));
        Assert.True(store.ScheduleDetail(draft.Id).Schedule.ReviewCurrent);
        var approved = Approve(store, pausedDraft);
        Assert.True(store.Schedules().Single().ReviewCurrent);
        store.Apply(C("notes.save") with { EmployeeId = employee, Body = "A corrected fictional audience." });
        var audit = store.VerifyAudit();

        var listed = Assert.Single(store.Schedules());
        var detail = store.ScheduleDetail(approved.Id);
        Assert.False(listed.ReviewCurrent);
        Assert.False(detail.Schedule.ReviewCurrent);
        Assert.Equal(approved, detail.Schedule with { ReviewCurrent = null });
        Assert.Empty(detail.Runs);
        Assert.True(detail.Schedule.NextRunAt > _now);
        var need = Assert.Single(
            store.Today("today", _now.AddDays(-1), _now.AddDays(1)).NeedsYou,
            item => item.Kind == "schedule"
        );
        Assert.Equal(approved.Id, need.TargetId);
        Assert.Equal(employee, need.EmployeeId);
        Assert.Contains("changed", need.Detail);
        Assert.Equal(audit, store.VerifyAudit());
        Assert.Null(store.ClaimDueSchedule());

        var paused = A<CompanySchedule>(store, Review(approved, "pause"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(paused)));
        var replacement = Draft(store, Definition(employee));
        Assert.True(store.ScheduleDetail(replacement.Id).Schedule.ReviewCurrent);
        Assert.Equal("draft", replacement.State);
        Assert.Equal(0, replacement.Consumed);
    }

    [Fact]
    public void UpdatedRoleInstructionsInvalidateTheDisplayedReviewWhileTerminalSchedulesStayHistorical()
    {
        var store = Store();
        var employee = Employee(store);
        var active = Approve(store, Draft(store, Definition(employee)));
        var ended = Approve(store, Draft(store, Definition(employee) with { Name = "Ended fictional work" }));
        store.Apply(Review(ended, "cancel"));
        var roles = CompanyWorkspace
            .Definitions()
            .Select(role =>
                role with
                {
                    InstructionsForSkill = skill =>
                        (role.InstructionsForSkill?.Invoke(skill) ?? role.Instructions)
                        + " Updated fictional drafting guidance.",
                }
            );
        var updated = new CompanyStore(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            roles.ToArray(),
            measuredRuntimeIdentity: Runtime
        )
        {
            Clock = () => _now,
            MeasuredTerms = _ => new("host", "credits"),
        };

        Assert.False(updated.ScheduleDetail(active.Id).Schedule.ReviewCurrent);
        Assert.Null(updated.ScheduleDetail(ended.Id).Schedule.ReviewCurrent);
        Assert.Equal(active.Digest, updated.ScheduleDetail(active.Id).Schedule.Digest);
        Assert.Equal(active.Version, updated.ScheduleDetail(active.Id).Schedule.Version);
        var needs = updated.Today("today", _now.AddDays(-1), _now.AddDays(1)).NeedsYou;
        Assert.Equal(active.Id, Assert.Single(needs, need => need.Kind == "schedule").TargetId);
        _now = active.NextRunAt!.Value;
        Assert.Null(updated.ClaimDueSchedule());
        Assert.Equal("blocked", updated.ScheduleDetail(active.Id).Schedule.State);
        Assert.Empty(updated.ScheduleDetail(active.Id).Runs);
    }
}
