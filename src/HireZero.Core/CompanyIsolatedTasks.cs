namespace HireZero.Core;

public sealed record CompanyPlanArtifactRef(string Id, int Version, string Digest);

public sealed record CompanyPlanDependency(string TaskId, CompanyPlanArtifactRef[] Artifacts);

public sealed record CompanyPlanAuthority(string Id, string Digest, CompanyPlanDependency[] Dependencies);

public sealed partial class CompanyStore
{
    private bool HasUnsettledCompute(StoreConnection db, string taskId) =>
        All<CompanySandboxJob>(db)
            .Any(job => job.TaskId == taskId && job.StartedAt is not null && job.SettledAt is null)
        || All<CompanyBrowserJob>(db)
            .Any(job => job.TaskId == taskId && job.StartedAt is not null && job.SettledAt is null);

    private bool IsolatedTaskCurrent(
        StoreConnection db,
        string taskId,
        string employeeId,
        int taskGeneration,
        int employeeGeneration,
        int depth = 0,
        bool allowPlan = false,
        bool allowSchedule = false
    )
    {
        var task = Need<CompanyTask>(db, taskId);
        var employee = Need<CompanyEmployee>(db, employeeId);
        if (
            depth >= 16
            || task.EmployeeId != employee.Id
            || task.Generation != taskGeneration
            || employee.Generation != employeeGeneration
            || employee.State != "active"
            || task.State is "canceled" or "superseded" or "unknown"
            || task.PlanId is not null && !allowPlan
            || task.MeetingId is not null
            || task.ScheduleRunId is not null && !allowSchedule
            || task.KnowledgeId is null
            || Find<CompanyKnowledge>(db, task.KnowledgeId)?.State != "approved"
            || task.CompanyKnowledgeId is not null
                && Find<CompanyKnowledge>(db, task.CompanyKnowledgeId)?.State != "approved"
            || !HandoffsCurrent(db, task, depth + 1)
        )
            return false;
        // Referenced work remains revocable even though only explicitly selected documents enter the container.
        if (
            !task.Evidence.All(id =>
                Find<CompanyArtifact>(db, id) is { State: "approved" } a
                && SandboxArtifactCurrent(db, a, depth + 1)
                && (
                    a.ContextId is null
                    || KnowledgeCurrent(
                        db,
                        Need<CompanyContext>(db, a.ContextId),
                        Need<CompanyTask>(db, a.TaskId),
                        depth + 1
                    )
                )
            )
        )
            return false;
        try
        {
            if (task.CompanyKnowledgeId is not null)
                BusinessProvenance(db, task.CompanyKnowledgeId);
        }
        catch (WorkspaceException)
        {
            return false;
        }
        return true;
    }

    private CompanyArtifact[] TaskArtifacts(StoreConnection db, string taskId) =>
        All<CompanyArtifact>(db).Where(a => a.TaskId == taskId).ToArray();

    private bool ApprovedTaskArtifactCurrent(StoreConnection db, CompanyArtifact artifact, int depth = 0) =>
        depth < 16
        && artifact.State == "approved"
        && SandboxArtifactCurrent(db, artifact, depth + 1)
        && (
            artifact.ContextId is null
            || KnowledgeCurrent(
                db,
                Need<CompanyContext>(db, artifact.ContextId),
                Need<CompanyTask>(db, artifact.TaskId),
                depth + 1
            )
        );

    private CompanyPlanAuthority? CapturePlanAuthority(StoreConnection db, CompanyTask task)
    {
        if (task.PlanId is null)
            return null;
        var plan = Need<CompanyPlan>(db, task.PlanId);
        if (!ParentValid(db, task) || !plan.TaskIds.Contains(task.Id))
            throw new WorkspaceException(
                "The parent plan must be approved, current and within its execution window.",
                409
            );
        var dependencies = task
            .Dependencies.Select(id =>
            {
                var previous = Need<CompanyTask>(db, id);
                var approved = TaskArtifacts(db, id).Where(a => a.State == "approved").ToArray();
                if (
                    previous.PlanId != plan.Id
                    || previous.State != "completed"
                    || approved.Length == 0
                    || approved.Any(a => !ApprovedTaskArtifactCurrent(db, a))
                )
                    throw new WorkspaceException(
                        "Complete and approve each predecessor's result before preparing this task execution.",
                        409
                    );
                return new CompanyPlanDependency(
                    id,
                    approved.Select(a => new CompanyPlanArtifactRef(a.Id, a.Version, a.Digest)).ToArray()
                );
            })
            .ToArray();
        return new(plan.Id, plan.Digest, dependencies);
    }

    private bool PlanAuthorityCurrent(StoreConnection db, CompanyTask task, CompanyPlanAuthority? saved, int depth)
    {
        if (task.PlanId is null)
            return saved is null;
        if (
            depth >= 16
            || saved is null
            || task.PlanId != saved.Id
            || Find<CompanyPlan>(db, saved.Id) is not { } plan
            || plan.State is not ("executing" or "completed")
            || plan.Digest != saved.Digest
            || !plan.TaskIds.Contains(task.Id)
            || !PlanSourceCurrent(db, plan)
            || plan.Source is { } source
                && !ApprovedTaskArtifactCurrent(db, Need<CompanyArtifact>(db, source.ArtifactId), depth + 1)
            || !task.Dependencies.SequenceEqual(saved.Dependencies.Select(d => d.TaskId))
        )
            return false;
        // Completion retains reviewed history; fresh execution separately requires the live parent deadline.
        return saved.Dependencies.All(dependency =>
            Find<CompanyTask>(db, dependency.TaskId) is { State: "completed" } previous
            && previous.PlanId == plan.Id
            && dependency.Artifacts.Length > 0
            && dependency.Artifacts.All(reference =>
                Find<CompanyArtifact>(db, reference.Id) is { State: "approved" } artifact
                && artifact.TaskId == previous.Id
                && artifact.Version == reference.Version
                && artifact.Digest == reference.Digest
                && ApprovedTaskArtifactCurrent(db, artifact, depth + 1)
            )
        );
    }
}
