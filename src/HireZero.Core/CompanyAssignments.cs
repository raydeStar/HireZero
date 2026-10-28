namespace HireZero.Core;

public sealed partial class CompanyStore
{
    // One transaction creates a tracked task and its reviewable packet. Preparation is not permission to run it.
    private object PrepareAssignment(StoreConnection db, CompanyCommand command, string? websiteLookupId = null)
    {
        if (command.Id is null || command.Digest is null || command.ExpectedVersion <= 0)
            throw new WorkspaceException("Select the exact approved company brief for this assignment.", 422);
        var knowledge = All<CompanyKnowledge>(db).LastOrDefault(k => k.DepartmentId is null && k.State == "approved");
        if (
            knowledge is null
            || knowledge.Id != command.Id
            || knowledge.Digest != command.Digest
            || knowledge.Version != command.ExpectedVersion
        )
            throw new WorkspaceException(
                "The approved company brief changed. Refresh and review it before preparing this assignment.",
                409
            );
        var employee = Need<CompanyEmployee>(db, command.EmployeeId);
        var thread = Need<CompanyThread>(db, command.ThreadId);
        if (thread.EmployeeId != employee.Id || thread.Scope != "employee")
            throw new WorkspaceException("Choose this employee's private conversation for the assignment.", 404);
        if (!Role(employee.RoleId).MeasuredSkills.Contains(command.Skill))
            throw new WorkspaceException("Choose a measured internal skill offered by this employee.", 403);
        var body = Text(command.Body);
        var message = Message(db, thread, "owner", body);
        var task = NewTask(
            db,
            employee.Id,
            thread.Id,
            command.Skill!,
            body,
            command.Evidence ?? [],
            [],
            null,
            null,
            null
        );
        if (websiteLookupId is not null)
        {
            task = task with { GoalWebsiteLookupId = websiteLookupId };
            Put(db, task);
        }
        var attempt = PrepareMeasured(db, command with { Id = task.Id, ExpectedVersion = task.Version });
        Event(
            db,
            "assignment.prepared",
            task.Id,
            employee.Id,
            "An assignment is ready for exact context review. Its result will need review before completion; no model request has started."
        );
        return new
        {
            message,
            task = Need<CompanyTask>(db, task.Id),
            attempt,
        };
    }
}
