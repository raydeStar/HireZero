using HireZero.Core;

// Fictional records pass through the real commands and result lifecycle; no provider is called.
internal static class OrchestrationNoticeFixture
{
    internal static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    internal static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    internal static T[] Items<T>(CompanyStore store, string name) => MeasuredQueueChecks.Items<T>(store, name);

    internal static (CompanyPlan Plan, CompanyMeeting Meeting) Seed(CompanyStore store)
    {
        (CompanyEmployee Employee, CompanyThread Thread) Hire(string roleId)
        {
            var department = A<CompanyDepartment>(store, C("department.create") with { Name = "Fictional " + roleId });
            var role = store.Roles.Single(r => r.Id == roleId);
            var facts = A<CompanyKnowledge>(
                store,
                C("knowledge.save") with
                {
                    DepartmentId = department.Id,
                    Body = CompanyJson.Encode(
                        role.OnboardingFields.ToDictionary(f => f, f => "Fictional approved " + f)
                    ),
                }
            );
            store.Apply(
                C("knowledge.review") with
                {
                    Id = facts.Id,
                    ExpectedVersion = facts.Version,
                    Digest = facts.Digest,
                    Decision = "approve",
                }
            );
            var employee = A<CompanyEmployee>(
                store,
                C("employee.hire") with
                {
                    DepartmentId = department.Id,
                    RoleId = roleId,
                    Name = "Fictional " + roleId,
                    Responsibility = "Prepare a bounded internal review",
                }
            );
            return (employee, Items<CompanyThread>(store, "threads").Single(t => t.EmployeeId == employee.Id));
        }
        var ceo = Hire("ceo");
        var marketing = Hire("marketing");
        var plan = A<CompanyPlan>(
            store,
            C("plan.create") with
            {
                EmployeeId = ceo.Employee.Id,
                Body = "Fictional coordinated campaign review",
                Budget = 22000,
                Tasks =
                [
                    new(marketing.Employee.Id, marketing.Thread.Id, "chat", "Review the fictional campaign", [], []),
                ],
            }
        );
        var meeting = A<CompanyMeeting>(
            store,
            C("meeting.create") with
            {
                Name = "Fictional campaign discussion",
                Body = "Discuss a possible campaign; publish nothing.",
                Participants = [ceo.Employee.Id],
                ChairId = ceo.Employee.Id,
                Budget = 22000,
            }
        );
        return (plan, meeting);
    }

    internal static CompanyMeeting FinishDiscussion(CompanyStore store, CompanyMeeting meeting)
    {
        meeting = A<CompanyMeeting>(
            store,
            C("meeting.start") with
            {
                Id = meeting.Id,
                ExpectedVersion = meeting.Version,
            }
        );
        for (var i = 0; i < 2; i++)
        {
            var task = store.Work(meeting.ActiveTask!);
            var attempt = store.Claim(task.EmployeeId, true, task.Id)!;
            store.Capture(attempt.Id);
            if (!store.AuthorizeDispatch(attempt.Id))
                throw new InvalidOperationException("Fictional turn could not be admitted.");
            store.Finish(attempt.Id, new("Fictional discussion turn; follow-up needs separate approval.", 100));
            meeting = Items<CompanyMeeting>(store, "meetings").Single(m => m.Id == meeting.Id);
        }
        return meeting;
    }

    internal static CompanyPlan CompletePlan(CompanyStore store, CompanyPlan plan)
    {
        plan = A<CompanyPlan>(
            store,
            C("plan.approve") with
            {
                Id = plan.Id,
                ExpectedVersion = plan.Version,
                Digest = plan.Digest,
            }
        );
        foreach (var id in plan.TaskIds)
        {
            var task = store.Work(id);
            var artifact = A<CompanyArtifact>(
                store,
                C("artifact.create") with
                {
                    Id = id,
                    ExpectedVersion = task.Version,
                    Body = "Fictional reviewed campaign result.",
                }
            );
            store.Apply(
                C("artifact.review") with
                {
                    Id = artifact.Id,
                    ExpectedVersion = artifact.Version,
                    Digest = artifact.Digest,
                    Decision = "approve",
                }
            );
            store.Apply(C("task.complete") with { Id = id, ExpectedVersion = store.Work(id).Version });
        }
        return Items<CompanyPlan>(store, "plans").Single(p => p.Id == plan.Id);
    }
}
