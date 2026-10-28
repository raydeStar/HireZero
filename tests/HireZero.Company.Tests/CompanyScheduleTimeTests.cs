using HireZero.Core;

public sealed partial class CompanyScheduleTests
{
    [Fact]
    public async Task ApprovedOccurrenceTimeUsesTheLocalSlotAcrossDaylightSavingAndRestart()
    {
        var store = Store();
        var definition = Definition(Employee(store)) with
        {
            Cadence = "weekly",
            TimeZone = "America/Denver",
            StartDate = "2026-11-01",
            LocalTime = "01:30",
            IncludeOccurrenceTime = true,
        };
        var schedule = Approve(store, Draft(store, definition));
        var model = new Model();
        _now = schedule.NextRunAt!.Value.AddMinutes(20);
        Assert.True(await new CompanyScheduler(store, model).TickSchedulesAsync(TestContext.Current.CancellationToken));
        var first = model.Last!;
        Assert.Equal(definition.Prompt, first.Context.Input);
        Assert.Equal(
            new CompanyScheduledOccurrence(
                DateTimeOffset.Parse("2026-11-01T01:30:00-06:00"),
                "America/Denver",
                "weekly",
                1,
                2
            ),
            first.Context.ScheduledOccurrence
        );
        Assert.Equal(TimeSpan.FromHours(-6), first.Context.ScheduledOccurrence!.ScheduledFor.Offset);
        Assert.Contains("not necessarily the actual execution time", first.Instructions);
        var originalPacket = CompanyJson.Encode(first.Context);
        var restarted = Store();
        _now = restarted.ScheduleDetail(schedule.Id).Schedule.NextRunAt!.Value;
        Assert.True(
            await new CompanyScheduler(restarted, model).TickSchedulesAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(
            new CompanyScheduledOccurrence(
                DateTimeOffset.Parse("2026-11-08T01:30:00-07:00"),
                "America/Denver",
                "weekly",
                2,
                2
            ),
            model.Last!.Context.ScheduledOccurrence
        );
        Assert.Equal(TimeSpan.FromHours(-7), model.Last.Context.ScheduledOccurrence!.ScheduledFor.Offset);
        Assert.Equal(originalPacket, CompanyJson.Encode(restarted.TextCall(first.DispatchId).Context));
        Assert.Equal(2, model.Calls);
        Assert.Equal("completed", restarted.ScheduleDetail(schedule.Id).Schedule.State);
    }

    [Fact]
    public async Task LegacyScheduleApprovalAndPacketsDoNotAcquireOccurrenceContext()
    {
        var store = Store();
        var definition = Definition(Employee(store));
        var encoded = CompanyJson.Encode(definition);
        Assert.DoesNotContain("includeOccurrenceTime", encoded);
        var draft = Draft(store, CompanyJson.Decode<CompanyScheduleInput>(encoded));
        var approved = Approve(store, draft);
        var oldDigest = approved.Digest;
        var restarted = Store();
        _now = approved.NextRunAt!.Value;
        var model = new Model();
        Assert.True(
            await new CompanyScheduler(restarted, model).TickSchedulesAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(oldDigest, restarted.ScheduleDetail(approved.Id).Schedule.Digest);
        Assert.Null(model.Last!.Context.ScheduledOccurrence);
        Assert.DoesNotContain("scheduledOccurrence", CompanyJson.Encode(model.Last.Context));
        Assert.DoesNotContain("scheduledOccurrence", model.Last.Instructions);
        Assert.Equal(definition.Prompt, model.Last.Context.Input);
    }

    [Fact]
    public async Task SkippedSlotsKeepTheirOriginalOccurrenceNumberAndNeedExactOptInApproval()
    {
        var store = Store();
        var definition = Definition(Employee(store)) with { Occurrences = 4 };
        var draft = Draft(store, definition);
        var changed = A<CompanySchedule>(
            store,
            C("schedule.save") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Body = CompanyJson.Encode(definition with { IncludeOccurrenceTime = true }),
            }
        );
        Assert.NotEqual(draft.Digest, changed.Digest);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(changed) with { Digest = draft.Digest }));
        var approved = Approve(store, changed);
        _now = approved.NextRunAt!.Value.AddDays(2).AddMinutes(10);
        var model = new Model();
        Assert.True(await new CompanyScheduler(store, model).TickSchedulesAsync(TestContext.Current.CancellationToken));
        var run = Assert.Single(store.ScheduleDetail(approved.Id).Runs);
        Assert.Equal(2, run.Missed);
        Assert.Equal(3, model.Last!.Context.ScheduledOccurrence!.RunNumber);
        Assert.Equal(run.DueAt, model.Last.Context.ScheduledOccurrence.ScheduledFor);
        Assert.Equal(1, model.Calls);
    }
}
