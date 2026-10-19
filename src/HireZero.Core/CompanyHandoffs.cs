using Microsoft.Data.Sqlite;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private object AssignHandoff(SqliteConnection db, CompanyCommand command)
    {
        var artifact = Need<CompanyArtifact>(db, command.Id);
        Version(artifact.Version, command.ExpectedVersion);
        var source = Need<CompanyTask>(db, artifact.TaskId);
        if (
            artifact.State != "approved"
            || artifact.Digest != command.Digest
            || source.State is "canceled" or "superseded"
            || artifact.ContextId is { } context && !KnowledgeCurrent(db, Need<CompanyContext>(db, context), source)
        )
            throw new WorkspaceException("Share the exact current approved artifact; source authority changed.");
        var employee = Need<CompanyEmployee>(db, command.EmployeeId);
        if (employee.Id == artifact.EmployeeId)
            throw new WorkspaceException("Choose another employee for this handoff.");
        if (command.Evidence?.Length > 0)
            throw new WorkspaceException("A handoff assignment shares only its reviewed source artifact.");
        var task = NewTask(
            db,
            employee.Id,
            Text(command.ThreadId, 64),
            Text(command.Skill, 80),
            Text(command.Body),
            [artifact.Id],
            [],
            null,
            null,
            null
        );
        if (task.State == "queued")
        {
            task = task with { State = "blocked", Reason = "handoff-awaiting-approval", Version = task.Version + 1 };
            Put(db, task);
        }
        var handoff = new CompanyHandoff(
            Id(),
            artifact.Id,
            employee.Id,
            task.ThreadId,
            artifact.Body,
            artifact.Digest,
            TargetTaskId: task.Id,
            ArtifactVersion: artifact.Version
        );
        Save(db, handoff, handoff.Id, artifact.Id, employee.Id);
        Event(
            db,
            "handoff.assigned",
            handoff.Id,
            employee.Id,
            "Owner shared one approved artifact and assigned scoped work to "
                + employee.Name
                + ". Execution needs separate authority."
        );
        return new { handoff, task };
    }

    private CompanyHandoff RevokeHandoff(SqliteConnection db, CompanyCommand command)
    {
        var handoff = Need<CompanyHandoff>(db, command.Id);
        Version(handoff.Version, command.ExpectedVersion);
        if (handoff.TargetTaskId is null)
            throw new WorkspaceException("This command revokes task-scoped handoffs only.");
        if (handoff.State == "revoked")
            return handoff;
        handoff = handoff with { State = "revoked", Version = handoff.Version + 1 };
        Save(db, handoff, handoff.Id, handoff.ArtifactId, handoff.TargetEmployeeId, handoff.State, handoff.Version);
        var task = Need<CompanyTask>(db, handoff.TargetTaskId);
        if (task.State is not ("completed" or "canceled" or "superseded"))
            Stop(db, task, "canceled", "Owner revoked this task's handoff.");
        Event(
            db,
            "handoff.revoked",
            handoff.Id,
            handoff.TargetEmployeeId,
            "Owner revoked one task-scoped handoff; no replacement work was started."
        );
        return handoff;
    }

    private bool HandoffsCurrent(SqliteConnection db, CompanyTask task, int depth) =>
        All<CompanyHandoff>(db)
            .Where(h => h.TargetTaskId == task.Id)
            .All(h =>
                h.State == "shared"
                && h.TargetEmployeeId == task.EmployeeId
                && h.TargetThreadId == task.ThreadId
                && Find<CompanyArtifact>(db, h.ArtifactId) is { State: "approved" } a
                && a.Digest == h.Digest
                && a.Version == h.ArtifactVersion
                && Need<CompanyTask>(db, a.TaskId).State is not ("canceled" or "superseded")
                && (
                    a.ContextId is null
                    || KnowledgeCurrent(
                        db,
                        Need<CompanyContext>(db, a.ContextId),
                        Need<CompanyTask>(db, a.TaskId),
                        depth + 1
                    )
                )
            );
}
