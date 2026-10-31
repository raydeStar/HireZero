using HireZero.Core;

// A morning round on an owner-approved shift (docs/adr/0171-an-owner-approved-shift.md).
public sealed partial class CompanyScheduleTests
{
    [Fact]
    public async Task AMorningRoundSeesTheWeeklyNumbersAndRecentWorkWhileOtherWorkDoesNot()
    {
        var s = Store();
        var employee = Employee(s);
        s.Apply(
            C("reading.record") with
            {
                EmployeeId = employee,
                Name = "2026-10-19",
                Body = CompanyJson.Encode(new[] { new CompanyMeasure("Visit", 1000), new CompanyMeasure("Order", 20) }),
            }
        );
        var shift = Approve(
            s,
            Draft(
                s,
                Definition(employee) with
                {
                    Name = "Morning round",
                    Skill = "marketing-morning",
                    IncludeOccurrenceTime = true,
                }
            )
        );
        var other = Approve(s, Draft(s, Definition(employee) with { Skill = "marketing-starter-plan" }));
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(s, model);
        _now = shift.NextRunAt!.Value;

        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        var round = model.Last!;
        Assert.Equal("marketing-morning", s.Work(Assert.Single(s.ScheduleDetail(shift.Id).Runs).TaskId!).Skill);
        Assert.Equal(1000, Assert.Single(round.Context.Scorecard!).Values[0].Value);
        var earlier = Assert.Single(round.Context.RecentWork!);
        Assert.Equal("in progress", earlier.Status);
        Assert.NotNull(round.Context.ScheduledOccurrence);
        Assert.Contains(CompanyStore.ShiftInstructions, round.Instructions);
        Assert.Contains("'## Jobs for today', '## Draft for job 1'", round.Instructions);

        // Other recurring work keeps its packet: no numbers, no work list, no shift instructions.
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        var plan = model.Last!;
        Assert.Equal(other.Id, s.ScheduleDetail(other.Id).Schedule.Id);
        Assert.Single(s.ScheduleDetail(other.Id).Runs);
        Assert.Null(plan.Context.Scorecard);
        Assert.Null(plan.Context.RecentWork);
        Assert.DoesNotContain(CompanyStore.ShiftInstructions, plan.Instructions);
        Assert.True(s.VerifyAudit().Intact);
    }
}
