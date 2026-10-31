using HireZero.Core;
using HireZero.Roles.Marketing;
using HireZero.Tools.Analytics;

public sealed partial class CompanyScheduleTests
{
    private static CompanyTask ProposalTask(CompanyStore store, string employee, string prompt) =>
        A<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = employee,
                ThreadId = MeasuredQueueChecks
                    .Items<CompanyThread>(store, "threads")
                    .First(t => t.EmployeeId == employee)
                    .Id,
                Skill = "marketing-goal-draft",
                Body = prompt,
            }
        );

    private static CompanyArtifact ProposalSource(CompanyStore store, string employee)
    {
        var task = ProposalTask(store, employee, "Suggest internal work for the fictional paper studio.");
        return A<CompanyArtifact>(
            store,
            C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body =
                    "## Jobs for today\n### 1. Write a gift guide\nWhy: Help new customers.\n### 2. Draft a launch email\nThe studio sells online.",
            }
        );
    }

    private static CompanyCommand Feedback(CompanyArtifact source, string kind = "veto") =>
        C("proposal.feedback") with
        {
            Id = source.Id,
            Digest = source.Digest,
            Decision = kind,
            Name = kind == "veto" ? "Write a gift guide" : "The studio sells online.",
            Body = kind == "veto" ? null : "The studio serves local customers only; it does not sell online.",
        };

    [Fact]
    public async Task VetoSurvivesNewerLessonsAndReopeningWithoutChangingTheShiftApproval()
    {
        var store = Store();
        var employee = Employee(store);
        var shift = Approve(store, Draft(store, Definition(employee) with { Skill = "marketing-morning" }));
        var source = ProposalSource(store, employee);
        var command = Feedback(source);
        var veto = A<CompanyMemoryItem>(store, command);
        Assert.Equal("approved", veto.State);
        Assert.Null(veto.Origin);
        Assert.Equal(veto, A<CompanyMemoryItem>(store, command));
        for (var i = 0; i < 12; i++)
        {
            var lesson = A<CompanyMemoryItem>(
                store,
                C("memory.save") with
                {
                    Body = CompanyJson.Encode(
                        new CompanyMemoryInput(
                            "feedback",
                            "New lesson " + i,
                            "A separate fictional lesson " + i,
                            "Owner approved",
                            EmployeeId: employee
                        )
                    ),
                }
            );
            store.Apply(
                C("memory.review") with
                {
                    Id = lesson.Id,
                    ExpectedVersion = lesson.Version,
                    Digest = lesson.Digest,
                    Decision = "approve",
                }
            );
        }
        store = Store();
        Assert.True(store.ScheduleDetail(shift.Id).Schedule.ReviewCurrent);
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(store, model);
        _now = shift.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(veto.Id, Assert.Single(model.Last!.Context.OwnerDirections!).Id);
        Assert.Contains(CompanyStore.OwnerDirectionInstructions, model.Last.Instructions);
        Assert.Equal(8, model.Last.Context.Lessons!.Length);
        Assert.Equal(
            source.Body,
            MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts").Single(a => a.Id == source.Id).Body
        );
    }

    [Fact]
    public void VetoBlocksTheSameJobAndUndoDoesNotDispatchAnything()
    {
        var store = Store();
        var employee = Employee(store);
        var veto = A<CompanyMemoryItem>(store, Feedback(ProposalSource(store, employee)));
        var task = ProposalTask(store, employee, "Write a gift guide.\nMake an internal draft.");
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("measured.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = task.Version,
                    CreditAllowance = 1,
                }
            )
        );
        Assert.Empty(MeasuredQueueChecks.Items<System.Text.Json.JsonElement>(store, "measured"));
        store.Apply(
            C("memory.review") with
            {
                Id = veto.Id,
                ExpectedVersion = veto.Version,
                Digest = veto.Digest,
                Decision = "revoke",
            }
        );
        Assert.Empty(MeasuredQueueChecks.Items<System.Text.Json.JsonElement>(store, "measured"));
        Assert.Equal("queued", store.Work(task.Id).State);
        var prepared = A<CompanyAttempt>(
            store,
            C("measured.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                CreditAllowance = 1,
            }
        );
        Assert.Null(store.TextCall(prepared.Id).Context.OwnerDirections);
    }

    [Fact]
    public void CorrectionInvalidatesAnUnsentPacketAndRequiresTheExactSource()
    {
        var store = Store();
        var employee = Employee(store);
        var source = ProposalSource(store, employee);
        var task = ProposalTask(store, employee, "Draft a launch email.");
        var prepared = A<CompanyAttempt>(
            store,
            C("measured.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                CreditAllowance = 1,
            }
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(Feedback(source, "correction") with { Digest = "changed" })
        );
        Assert.Throws<WorkspaceException>(() => Store("another-company").Apply(Feedback(source)));
        var correction = A<CompanyMemoryItem>(store, Feedback(source, "correction"));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("measured.run") with
                {
                    Id = prepared.Id,
                    ExpectedVersion = prepared.Version,
                    Digest = prepared.Measured!.Digest,
                    Decision = "accept-soft-limits",
                }
            )
        );
        var updated = A<CompanyMemoryItem>(
            store,
            Feedback(source, "correction") with
            {
                Body = "The studio serves local shops only.",
            }
        );
        Assert.Equal(correction.Id, updated.Replacement!.Id);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("memory.save") with { Body = CompanyJson.Encode(updated.Definition) })
        );
    }

    [Fact]
    public async Task OnlyAnOptedInShiftReadsSourcesAndSealsThemIntoTheModelPacket()
    {
        var store = Store();
        var employee = Employee(store);
        var legacy = Approve(store, Draft(store, Definition(employee) with { Skill = "marketing-morning" }));
        var gathered = Approve(
            store,
            Draft(
                store,
                Definition(employee) with
                {
                    Skill = "marketing-morning",
                    ReadContextPolicy = MarketingSignals.Policy,
                }
            )
        );
        Assert.Throws<WorkspaceException>(() =>
            Draft(store, Definition(employee) with { ReadContextPolicy = MarketingSignals.Policy })
        );
        var reads = 0;
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(store, model)
        {
            GatherScheduledContext = (id, skill, date, token) =>
            {
                reads++;
                Assert.Equal(employee, id);
                return Task.FromResult("Fictional site counts: visitors=40, signups=unknown.");
            },
        };
        _now = legacy.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, reads);
        Assert.Null(model.Last!.Context.FreshSignals);
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reads);
        Assert.Equal(MarketingSignals.Policy, model.Last!.Context.FreshSignals!.Policy);
        Assert.Contains("signups=unknown", model.Last.Context.FreshSignals.Summary);
        Assert.Contains(CompanyStore.FreshSignalInstructions, model.Last.Instructions);
        Assert.Equal(2, model.Calls);
    }

    [Fact]
    public async Task ASourceFailureBecomesUnknownAndAPauseDuringReadingPreventsDispatch()
    {
        var store = Store();
        var employee = Employee(store);
        var shift = Approve(
            store,
            Draft(
                store,
                Definition(employee) with
                {
                    Skill = "marketing-morning",
                    ReadContextPolicy = MarketingSignals.Policy,
                }
            )
        );
        var model = new Model(policy: CompanyMeasuredPolicy.Deliverable);
        using var scheduler = new CompanyScheduler(store, model)
        {
            GatherScheduledContext = (_, _, _, _) =>
                throw new InvalidOperationException("fictional unavailable source"),
        };
        _now = shift.NextRunAt!.Value;
        Assert.True(await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("unknown", model.Last!.Context.FreshSignals!.Summary);
        scheduler.GatherScheduledContext = (_, _, _, _) =>
        {
            var current = store.ScheduleDetail(shift.Id).Schedule;
            store.Apply(Review(current, "pause"));
            return Task.FromResult("Read finished after owner pause.");
        };
        _now = store.ScheduleDetail(shift.Id).Schedule.NextRunAt!.Value;
        await scheduler.TickSchedulesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task MarketingReadsOnlyBoundedDailyAndLandingPageDataAndKeepsMissingCountsUnknown()
    {
        var toolbox = new ProposalTools();
        var result = await MarketingSignals.GatherAsync(
            toolbox,
            new(2026, 10, 31),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(["analytics.daily", "analytics.traffic"], toolbox.Calls);
        Assert.Contains("visitors=17", result);
        Assert.Contains("signups=unknown", result);
        Assert.Contains("Landing-page traffic unavailable", result);
    }

    private sealed class ProposalTools : IToolbox
    {
        public List<string> Calls = [];

        public Task<TOutput> InvokeAsync<TInput, TOutput>(
            ToolContract<TInput, TOutput> contract,
            TInput input,
            CancellationToken token = default,
            ToolAuthority? authority = null
        )
        {
            Assert.Equal(ToolRisk.Read, contract.Risk);
            Calls.Add(contract.Id);
            if (input is DailyRequest daily)
            {
                Assert.Equal(new DateOnly(2026, 10, 17), daily.Start);
                Assert.Equal(new DateOnly(2026, 10, 30), daily.End);
                return Task.FromResult(
                    (TOutput)
                        (object)
                            new DailyReport(
                                "fictional counter",
                                daily.Start,
                                daily.End,
                                [new(daily.End, 17, 20, null, null)]
                            )
                );
            }
            var traffic = Assert.IsType<TrafficRequest>(input);
            Assert.Equal(5, traffic.Limit);
            Assert.Equal("landing-page", traffic.Breakdown);
            throw new InvalidOperationException("fictional source unavailable");
        }
    }
}
