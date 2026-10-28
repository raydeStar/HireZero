namespace HireZero.Core;

public sealed record CompanyGoalStartInput(
    string BusinessName,
    string BusinessDescription,
    string Goal,
    string EmployeeName,
    string DepartmentName,
    string Responsibility,
    Dictionary<string, string> Values,
    string Prompt
);

public sealed partial class CompanyStore
{
    private object PrepareGoal(StoreConnection db, CompanyCommand command)
    {
        if (command.Decision != "use-details-and-prepare")
            throw new WorkspaceException(
                "Confirm the supplied details and suggested setup before preparing work.",
                422
            );
        var input = CompanyJson.Decode<CompanyGoalStartInput>(command.Body ?? "null");
        var goal = Text(input.Goal, 600).Trim();
        if (goal.Length == 0)
            throw new WorkspaceException("Describe the result you want.", 422);
        if (!Text(input.Prompt).Contains(goal, StringComparison.Ordinal))
            throw new WorkspaceException("The prepared request must keep the owner's goal.", 422);
        var role = Role(command.RoleId);
        if (!role.MeasuredSkills.Contains(command.Skill))
            throw new WorkspaceException("Choose an internal deliverable offered by this specialist.", 403);

        var knowledge = All<CompanyKnowledge>(db).LastOrDefault(k => k.DepartmentId is null && k.State == "approved");
        if (knowledge is null && command.Id is null)
        {
            // Only owner-entered facts are confirmed here. The butler never approves his own research.
            if (
                Find<CompanyBusinessIdentity>(db, BusinessIdentityId) is not null
                || All<CompanyKnowledge>(db).Any(k => k.DepartmentId is null)
            )
                throw new WorkspaceException(
                    "Finish reviewing the existing business brief before starting this work.",
                    409
                );
            var name = Text(input.BusinessName, 100).Trim();
            var description = Text(input.BusinessDescription, 600).Trim();
            if (name.Length == 0 || description.Length == 0)
                throw new WorkspaceException("Name your business and briefly describe what it does.", 422);
            var identity = (CompanyBusinessIdentity)IdentifyBusiness(
                db,
                new(
                    command.RequestId,
                    "business.identify",
                    Body: CompanyJson.Encode(new CompanyBusinessInput(name, null, goal))
                )
            );
            identity = (CompanyBusinessIdentity)ConfirmBusiness(
                db,
                new(command.RequestId, "business.confirm", ExpectedVersion: identity.Version, Digest: identity.Digest)
            );
            var fields = BusinessProfileFields
                .Select(key => new CompanyProfileField(
                    key,
                    key == "business" ? description
                        : key == "goals" ? goal
                        : "Unknown",
                    key is "business" or "goals" ? "owner" : "unknown",
                    []
                ))
                .ToArray();
            var profile = (CompanyBusinessProfile)SaveBusinessProfile(
                db,
                new(
                    command.RequestId,
                    "business.profile.save",
                    Body: CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields))
                )
            );
            profile = (CompanyBusinessProfile)ReviewBusinessProfile(
                db,
                new(
                    command.RequestId,
                    "business.profile.review",
                    profile.Id,
                    profile.Version,
                    Digest: profile.Digest,
                    Decision: "approve"
                )
            );
            knowledge = Need<CompanyKnowledge>(db, profile.KnowledgeId);
        }
        else if (
            knowledge is null
            || knowledge.Id != command.Id
            || knowledge.Version != command.ExpectedVersion
            || knowledge.Digest != command.Digest
        )
            throw new WorkspaceException("Your business brief changed. Refresh before preparing this work.", 409);

        CompanyEmployee employee;
        if (command.EmployeeId is { } employeeId)
        {
            employee = Need<CompanyEmployee>(db, employeeId);
            if (employee.RoleId != role.Id || employee.State != "active")
                throw new WorkspaceException("Choose an active specialist for this work.", 409);
        }
        else
        {
            if (All<CompanyEmployee>(db).Any(e => e.RoleId == role.Id && e.State != "retired"))
                throw new WorkspaceException("A specialist already exists. Refresh to continue with them.", 409);
            var department = (CompanyDepartment)Apply(
                db,
                new(command.RequestId, "department.create", Name: input.DepartmentName)
            );
            employee = (CompanyEmployee)Apply(
                db,
                new(
                    command.RequestId,
                    "employee.hire",
                    DepartmentId: department.Id,
                    RoleId: role.Id,
                    Name: input.EmployeeName,
                    Responsibility: input.Responsibility
                )
            );
            var shared = (CompanyKnowledge)Apply(
                db,
                new(
                    command.RequestId,
                    "knowledge.save",
                    DepartmentId: department.Id,
                    Body: CompanyJson.Encode(input.Values)
                )
            );
            Apply(
                db,
                new(
                    command.RequestId,
                    "knowledge.review",
                    shared.Id,
                    shared.Version,
                    Digest: shared.Digest,
                    Decision: "approve"
                )
            );
        }
        if (
            All<CompanyAttempt>(db)
                .Any(a => a.EmployeeId == employee.Id && a.Phase is "claimed" or "reserved" or "dispatching")
        )
            throw new WorkspaceException(
                "This specialist already has work awaiting authorization or completion. Open the current request first.",
                409
            );
        var thread = command.EmployeeId is null
            ? All<CompanyThread>(db).First(t => t.EmployeeId == employee.Id && t.Scope == "employee")
            : (CompanyThread)Apply(
                db,
                new(
                    command.RequestId,
                    "thread.create",
                    EmployeeId: employee.Id,
                    Name: goal.Length <= 80 ? goal : goal[..80]
                )
            );
        var result = PrepareAssignment(
            db,
            command with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Body = input.Prompt,
            }
        );
        Event(
            db,
            "goal.prepared",
            employee.Id,
            employee.Id,
            "Owner details and specialist setup saved together; one draft awaits its run review."
        );
        return result;
    }
}
