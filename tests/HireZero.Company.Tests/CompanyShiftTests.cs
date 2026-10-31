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

    [Fact]
    public void WeekdayShiftsSkipSaturdaysAndSundays()
    {
        // Friday 2026-10-30, then Monday to Friday, then the following Monday.
        var weekdays = Definition() with
        {
            Cadence = "weekdays",
            StartDate = "2026-10-30",
            LocalTime = "08:00",
        };
        Assert.Equal(
            ["2026-10-30", "2026-11-02", "2026-11-03", "2026-11-06", "2026-11-09"],
            new[] { 0, 1, 2, 5, 6 }.Select(i => CompanyScheduleCalendar.Slot(weekdays, i).ToString("yyyy-MM-dd"))
        );
        // A weekend start waits for Monday.
        Assert.Equal(
            "2026-11-02",
            CompanyScheduleCalendar.Slot(weekdays with { StartDate = "2026-10-31" }, 0).ToString("yyyy-MM-dd")
        );
        Assert.Throws<WorkspaceException>(() =>
            CompanyScheduleCalendar.Slot(weekdays with { Cadence = "fortnightly" }, 0)
        );
    }

    [Fact]
    public async Task KeptLessonsReachEveryLaterRoundWithoutStoppingTheShift()
    {
        var s = Store();
        var employee = Employee(s);
        var shift = Approve(
            s,
            Draft(s, Definition(employee) with { Skill = "marketing-morning", Cadence = "weekdays" })
        );
        CompanyMemoryItem Save(string title, string kind = "feedback", string? audience = null) =>
            A<CompanyMemoryItem>(
                s,
                C("memory.save") with
                {
                    Body = CompanyJson.Encode(
                        new CompanyMemoryInput(
                            kind,
                            title,
                            title + " statement.",
                            "Kept from a round.",
                            EmployeeId: audience ?? employee
                        )
                    ),
                }
            );
        void Keep(CompanyMemoryItem m) =>
            s.Apply(
                C("memory.review") with
                {
                    Id = m.Id,
                    ExpectedVersion = m.Version,
                    Digest = m.Digest,
                    Decision = "approve",
                }
            );
        Keep(Save("Posts naming the starter kit brought orders"));
        Keep(Save("No paid ads this quarter", "decision"));
        Save("An unapproved idea");
        Keep(Save("A project note", "project"));
        Assert.True(s.ScheduleDetail(shift.Id).Schedule.ReviewCurrent, "Kept lessons do not stop the shift");

        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(s, model);
        _now = s.ScheduleDetail(shift.Id).Schedule.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        var lessons = model.Last!.Context.Lessons!;
        Assert.Equal(
            ["No paid ads this quarter", "Posts naming the starter kit brought orders"],
            lessons.Select(l => l.Title).Order()
        );
        Assert.Contains(CompanyStore.LessonInstructions, model.Last.Instructions);
        Assert.Contains("Friday: results and lessons", model.Last.Instructions);
    }
}
