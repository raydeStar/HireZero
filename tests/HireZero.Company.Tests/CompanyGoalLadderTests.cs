using HireZero.Core;
using Microsoft.Data.Sqlite;

// The goal ladder and weekly numbers (docs/adr/0170-goal-ladder-and-weekly-numbers.md).
public sealed class CompanyGoalLadderTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-goal-ladder-");

    // A Friday, so the week from Monday 2026-10-26 has started and the week from 2026-11-02 has not.
    private CompanyStore Store() =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-ladder-runtime"
        )
        {
            Clock = () => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
        };

    private sealed record Prepared(CompanyMessage Message, CompanyTask Task, CompanyAttempt Attempt);

    private static CompanyCommand C(string action) => MeasuredQueueChecks.C(action);

    private static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    private static T[] Items<T>(CompanyStore store, string key) => MeasuredQueueChecks.Items<T>(store, key);

    private static CompanyCommand Goal() =>
        C("goal.prepare") with
        {
            RoleId = "marketing",
            Skill = "marketing-goal-draft",
            CreditAllowance = 1,
            Decision = "use-details-and-prepare",
            Body = CompanyJson.Encode(
                new CompanyGoalStartInput(
                    "Fictional paper studio",
                    "We sell paper craft kits.",
                    "More money from our paper kits.",
                    "Fictional marketer",
                    "Marketing",
                    "Prepare internal marketing drafts.",
                    new()
                    {
                        ["purpose"] = "Useful marketing",
                        ["audience"] = "Unknown",
                        ["positioning"] = "Use approved facts",
                        ["voice"] = "Clear",
                        ["claims"] = "No invented claims",
                        ["constraints"] = "Internal drafts only",
                    },
                    "More money from our paper kits. Draft one useful asset."
                )
            ),
        };

    // The first goal, run and finished, so its specialist is free for the next request.
    private static Prepared FirstGoal(CompanyStore store)
    {
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
        store.Finish(first.Attempt.Id, new ModelReply("Fictional draft.", 20, "fictional-ladder-result", new(10, 10)));
        return first;
    }

    [Fact]
    public void AnApprovedLadderIsOptionalDepartmentContextThatTheLadderDraftReads()
    {
        var store = Store();
        var first = FirstGoal(store);
        var department = Assert.Single(Items<CompanyDepartment>(store, "departments"));
        var context = CompanyJson.Decode<Dictionary<string, string>>(
            Items<CompanyKnowledge>(store, "knowledge").Single(k => k.Id == department.Knowledge).Body
        );
        Assert.DoesNotContain("goal", context.Keys);

        // Ladder fields may be left out, but a value must be text, and required fields stay required.
        var notText = CompanyJson.Encode(
            new Dictionary<string, object>(context.ToDictionary(p => p.Key, object (p) => p.Value)) { ["goal"] = 3 }
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("knowledge.save") with { DepartmentId = department.Id, Body = notText })
        );
        var withoutVoice = new Dictionary<string, string>(context);
        withoutVoice.Remove("voice");
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("knowledge.save") with
                {
                    DepartmentId = department.Id,
                    Body = CompanyJson.Encode(withoutVoice),
                }
            )
        );
        var ladder = new Dictionary<string, string>(context)
        {
            ["goal"] = "New paying kit customers a week.",
            ["start"] = "Order a starter kit on the website.",
            ["steps"] = "- Visit: website visits a week\n- Order: starter kits ordered a week",
            ["focus"] = "Order: the shop page hides the starter kit.\nLeave alone for now: paid ads.",
        };
        var saved = A<CompanyKnowledge>(
            store,
            C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(ladder),
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = saved.Id,
                ExpectedVersion = saved.Version,
                Digest = saved.Digest,
                Decision = "approve",
            }
        );

        // The specialist works out a new ladder through the one-consent goal path, with the ladder's own instructions.
        var brief = Items<CompanyKnowledge>(store, "knowledge")
            .Last(k => k.DepartmentId is null && k.State == "approved");
        const string goal = "Work out my goal ladder: what to grow, and where to start";
        var input = CompanyJson.Decode<CompanyGoalStartInput>(Goal().Body!);
        var draft = A<Prepared>(
            store,
            Goal() with
            {
                Skill = "marketing-goal-ladder",
                Id = brief.Id,
                ExpectedVersion = brief.Version,
                Digest = brief.Digest,
                EmployeeId = first.Task.EmployeeId,
                Body = CompanyJson.Encode(
                    input with
                    {
                        Goal = goal,
                        Prompt = goal + ".\nReturn the five goal-ladder sections.",
                    }
                ),
            }
        );
        var call = store.TextCall(draft.Attempt.Id);
        Assert.Contains("'## Goal', '## First step', '## Steps', '## Focus', '## Notes for you'", call.Instructions);
        Assert.Contains("New paying kit customers a week.", call.Context.DepartmentKnowledge);
        Assert.Equal(goal, Items<CompanyThread>(store, "threads").Single(t => t.Id == draft.Task.ThreadId).Title);
        Assert.Single(Items<CompanyEmployee>(store, "employees"));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void WeeklyNumbersAreBoundedAndAWeekIsRecordedAgainAsItsNextVersion()
    {
        var store = Store();
        var employee = FirstGoal(store).Task.EmployeeId;
        var record = C("reading.record") with
        {
            EmployeeId = employee,
            Name = "2026-10-26",
            Body = CompanyJson.Encode(new[] { new CompanyMeasure("Visit", 1200), new CompanyMeasure("Order", 12) }),
        };
        var result = store.Apply(record);
        Assert.Equal(result, store.Apply(record));
        var reading = CompanyJson.Decode<CompanyReading>(result);
        Assert.Equal(("2026-10-26", 1, 2), (reading.Week, reading.Version, reading.Values.Length));

        // A week is replaced only from its current version.
        var update = record with
        {
            RequestId = Guid.NewGuid().ToString(),
            Body = CompanyJson.Encode(new[] { new CompanyMeasure("Visit", 1250.5m) }),
        };
        Assert.Throws<WorkspaceException>(() => store.Apply(update));
        var updated = A<CompanyReading>(store, update with { ExpectedVersion = 1 });
        Assert.Equal((reading.Id, 2, 1250.5m), (updated.Id, updated.Version, Assert.Single(updated.Values).Value));
        Assert.Equal(
            CompanyJson.Encode(updated),
            CompanyJson.Encode(Assert.Single(Items<CompanyReading>(store, "readings")))
        );

        static string Body(params CompanyMeasure[] values) => CompanyJson.Encode(values);
        static CompanyMeasure M(string name, decimal value) => new(name, value);
        foreach (
            var bad in new[]
            {
                record with
                {
                    Name = "2026-10-27",
                },
                record with
                {
                    Name = "2026-11-02",
                },
                record with
                {
                    Name = "2024-10-21",
                },
                record with
                {
                    Body = Body(M("Visit", -1)),
                },
                record with
                {
                    Body = Body(M("Visit", 1.234m)),
                },
                record with
                {
                    Body = Body(M("Visit", 1), M("visit", 2)),
                },
                record with
                {
                    Body = Body([.. Enumerable.Range(0, 9).Select(i => new CompanyMeasure("Step " + i, i))]),
                },
                record with
                {
                    Body = Body(M(" ", 1)),
                },
                record with
                {
                    Body = "[]",
                },
                record with
                {
                    Body = "{\"Visit\":1}",
                },
            }
        )
            Assert.Throws<WorkspaceException>(() =>
                store.Apply(bad with { RequestId = Guid.NewGuid().ToString(), ExpectedVersion = 2 })
            );
        Assert.Equal(CompanyRoles.Approver, CompanyAccess.Required("reading.record"));
        Assert.True(store.VerifyAudit().Intact);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
