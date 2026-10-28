namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private CompanyArtifact ReviewArtifact(StoreConnection db, CompanyCommand c)
    {
        var artifact = Need<CompanyArtifact>(db, c.Id);
        Version(artifact.Version, c.ExpectedVersion);
        if (artifact.Digest != c.Digest)
            throw new WorkspaceException("Review the exact artifact digest.");
        if (c.Decision is not ("approve" or "changes" or "revoke"))
            throw new WorkspaceException("Choose approve, changes or revoke.", 422);
        var task = Need<CompanyTask>(db, artifact.TaskId);
        if (task.State is "canceled" or "superseded")
            throw new WorkspaceException("This work was canceled or superseded.");
        if (artifact.ContextId is not null && !KnowledgeCurrent(db, Need<CompanyContext>(db, artifact.ContextId), task))
            throw new WorkspaceException("Knowledge used by this artifact was revoked.");
        if (c.Decision == "approve" && !SandboxArtifactCurrent(db, artifact))
            throw new WorkspaceException(
                "The result or its source documents are no longer current. They cannot be approved for reuse.",
                409
            );
        artifact = artifact with
        {
            State =
                c.Decision == "approve" ? "approved"
                : c.Decision == "revoke" ? "revoked"
                : "changes_requested",
            ReviewFeedback =
                c.Decision == "changes" && c.Body is not null ? Text(c.Body, 4000) : artifact.ReviewFeedback,
            Version = artifact.Version + 1,
        };
        Save(db, artifact, artifact.Id, artifact.TaskId, artifact.EmployeeId, artifact.State, artifact.Version);
        Event(db, "artifact.reviewed", artifact.Id, artifact.EmployeeId, "Artifact review: " + artifact.State);
        return artifact;
    }

    private CompanyTask RequestArtifactRevision(StoreConnection db, CompanyCommand command)
    {
        var artifact = Need<CompanyArtifact>(db, command.Id);
        var source = Need<CompanyTask>(db, artifact.TaskId);
        var employee = Need<CompanyEmployee>(db, source.EmployeeId);
        var feedback = Text(command.Body, 4000);
        if (
            source.Kind != "work"
            || source.State != "awaiting_review"
            || source.PlanId is not null
            || source.MeetingId is not null
            || source.ScheduleRunId is not null
            || source.Resource is not null
            || source.Dependencies.Length != 0
            || !Role(employee.RoleId).MeasuredSkills.Contains(source.Skill)
            || artifact.State is not ("needs_review" or "changes_requested")
        )
            throw new WorkspaceException(
                "Prepare a revision of an independent internal draft. Plan, recurring and repository work keep their separate review workflows.",
                409
            );
        if (artifact.Body.Length > 12000)
            throw new WorkspaceException(
                "This draft is too large for a bounded revision. Start a separate task with the relevant excerpt.",
                422
            );
        artifact = ReviewArtifact(db, command with { Decision = "changes", Body = feedback });
        var revision = NewTask(
            db,
            source.EmployeeId,
            source.ThreadId,
            source.Skill,
            source.Input,
            source.Evidence,
            [],
            null,
            null,
            null
        ) with
        {
            State = "blocked",
            Reason = "revision-awaiting-approval",
            RequestedModelRoute = source.RequestedModelRoute ?? source.ModelRoute,
            Revision = new(source.Id, artifact.Id, artifact.Version, artifact.Digest, feedback),
            GoalWebsiteLookupId = source.GoalWebsiteLookupId,
        };
        // Keep every owner's note, but never turn a family tree into an unbounded prompt.
        _ = EarlierRevisionFeedback(db, revision);
        Put(db, revision);
        // Retain the first draft and receipt. The butler replaces the assignment, not the evidence.
        Put(
            db,
            source with
            {
                State = "superseded",
                Reason = "revision-requested",
                Version = source.Version + 1,
                Generation = source.Generation + 1,
            }
        );
        Event(
            db,
            "artifact.revision-requested",
            artifact.Id,
            source.EmployeeId,
            "Feedback saved with a separate revision task. No model request is authorized."
        );
        return revision;
    }

    private CompanyTaskRevision[]? EarlierRevisionFeedback(StoreConnection db, CompanyTask task)
    {
        if (task.Revision is not { } current)
            return null;
        var earlier = new List<CompanyTaskRevision>();
        var seen = new HashSet<string> { task.Id };
        var source = Need<CompanyTask>(db, current.SourceTaskId);
        while (true)
        {
            if (
                !seen.Add(source.Id)
                || seen.Count > 16
                || source.EmployeeId != task.EmployeeId
                || source.ThreadId != task.ThreadId
            )
                throw new WorkspaceException(
                    "The revision history cannot fit a valid bounded chain. Start a separate task with owner-consolidated requirements.",
                    409
                );
            if (source.Revision is not { } previous)
                break;
            earlier.Add(previous);
            source = Need<CompanyTask>(db, previous.SourceTaskId);
        }
        earlier.Reverse();
        return earlier.Count == 0 ? null : earlier.ToArray();
    }

    private bool RevisionCurrent(StoreConnection db, CompanyContext context, CompanyTask task, int depth)
    {
        if (task.Revision is not { } revision)
            return context.Revision is null;
        if (
            context.Revision?.Request != revision
            || Find<CompanyArtifact>(db, revision.ArtifactId) is not { State: "changes_requested" } artifact
            || artifact.Version != revision.ArtifactVersion
            || artifact.Digest != revision.ArtifactDigest
            || artifact.Body != context.Revision.OriginalDraft
            || artifact.ReviewFeedback != revision.Feedback
            || Find<CompanyTask>(db, revision.SourceTaskId)
                is not { State: "superseded", Reason: "revision-requested" } source
            || source.Id != artifact.TaskId
            || source.EmployeeId != task.EmployeeId
            || source.ThreadId != task.ThreadId
        )
            return false;
        // Legacy snapshots keep their original bytes; new snapshots bind the complete feedback history.
        if (
            context.Revision.PriorFeedback is { } prior
            && !prior.SequenceEqual(EarlierRevisionFeedback(db, task) ?? [])
        )
            return false;
        return artifact.ContextId is null
            || KnowledgeCurrent(db, Need<CompanyContext>(db, artifact.ContextId), source, depth + 1);
    }
}
