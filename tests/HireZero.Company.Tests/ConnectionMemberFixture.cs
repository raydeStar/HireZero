using HireZero.Core;

// Compiled test host only: change a real fictional membership, never forge the browser's session response.
static class ConnectionMemberFixture
{
    public static void Map(WebApplication app)
    {
        const string primary = "owner@example.invalid",
            backup = "fixture-owner@example.invalid";
        var store = app.Services.GetRequiredService<CompanyStore>();
        store.Apply(MeasuredQueueChecks.C("member.invite") with { Body = backup, Decision = CompanyRoles.Owner });
        if (store.SignIn(backup, backup, true, "Fictional second owner") is null)
            throw new InvalidOperationException("The fixture's second owner is absent; no butler may hold every key.");
        var request = store.RequestFromAssistant(
            "google-ads",
            "Fictional reviewer",
            "Review the approved advertising connection.",
            null,
            null
        );
        var seeded = MeasuredQueueChecks.Seed(store);
        var action = CompanyJson.Decode<CompanyTask>(
            store.Apply(
                MeasuredQueueChecks.C("action.start") with
                {
                    EmployeeId = seeded.EmployeeId,
                    ThreadId = seeded.ThreadId,
                    Skill = "analytics-read",
                    Body = "{\"days\":\"7\"}",
                }
            )
        );
        store.FailAction(action.Id, "The fictional analytics connection needs owner setup.", "google-analytics");
        var review =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_MEMBER_JOURNEY") == "1"
                ? SeedReview(store, seeded)
                : null;
        app.MapPost(
            "/__fixture/connection-role/{role}",
            (string role) =>
            {
                if (!CompanyRoles.All.Contains(role))
                    return Results.BadRequest();
                var member = store.Members().Single(member => member.Id == primary);
                store.Apply(
                    MeasuredQueueChecks.C("member.role") with
                    {
                        Id = primary,
                        ExpectedVersion = member.Version,
                        Decision = role,
                    },
                    backup,
                    out _
                );
                return Results.Ok(
                    new
                    {
                        role,
                        request.Id,
                        seeded.EmployeeId,
                        seeded.ThreadId,
                        review,
                    }
                );
            }
        );
    }

    private static object SeedReview(CompanyStore store, CompanyTask task)
    {
        T Apply<T>(CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));
        var artifact = Apply<CompanyArtifact>(
            MeasuredQueueChecks.C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = "Fictional member review result: paper kits include a printed folding guide.",
            }
        );
        var department = Apply<CompanyDepartment>(
            MeasuredQueueChecks.C("department.create") with
            {
                Name = "Fictional coordination",
            }
        );
        var facts = Apply<CompanyKnowledge>(
            MeasuredQueueChecks.C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(
                    store
                        .Roles.Single(role => role.Id == "ceo")
                        .OnboardingFields.ToDictionary(field => field, field => "Fictional approved " + field)
                ),
            }
        );
        store.Apply(
            MeasuredQueueChecks.C("knowledge.review") with
            {
                Id = facts.Id,
                ExpectedVersion = facts.Version,
                Digest = facts.Digest,
                Decision = "approve",
            }
        );
        var ceo = Apply<CompanyEmployee>(
            MeasuredQueueChecks.C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "ceo",
                Name = "Fictional coordinator",
                Responsibility = "Review fictional internal work",
            }
        );
        var plan = Apply<CompanyPlan>(
            MeasuredQueueChecks.C("plan.create") with
            {
                EmployeeId = ceo.Id,
                Body = "Fictional member planning record",
                Budget = 22000,
                Tasks = [new(task.EmployeeId, task.ThreadId, "chat", "Review the fictional paper kit", [], [])],
            }
        );
        var meeting = Apply<CompanyMeeting>(
            MeasuredQueueChecks.C("meeting.create") with
            {
                ChairId = ceo.Id,
                Participants = [ceo.Id, task.EmployeeId],
                Evidence = [],
                Budget = 30000,
                Name = "Fictional member meeting",
                Body = "Review paper kit priorities",
            }
        );
        return new
        {
            taskId = task.Id,
            artifactId = artifact.Id,
            planId = plan.Id,
            meetingId = meeting.Id,
        };
    }
}
