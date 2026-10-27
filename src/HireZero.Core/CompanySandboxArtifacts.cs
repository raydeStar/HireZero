namespace HireZero.Core;

public sealed record CompanySandboxArtifactInput(string OutputName, string Quote);

public sealed record CompanySandboxArtifactOrigin(
    string JobId,
    string JobDigest,
    string ResultDigest,
    string OutputName,
    string OutputDigest,
    int Start,
    int Length
);

public sealed partial class CompanyStore
{
    // Script output remains evidence, never an instruction granting authority. Keep its source revocable after reuse.
    private bool SandboxArtifactCurrent(StoreConnection db, CompanyArtifact artifact, int depth = 0)
    {
        if (!BrowserArtifactCurrent(db, artifact, depth))
            return false;
        if (artifact.SandboxOrigin is not { } origin)
            return true;
        if (depth >= 16)
            return false;
        var job = Find<CompanySandboxJob>(db, origin.JobId);
        if (
            job is not { State: "completed", Revoked: false, Result.State: "completed" }
            || job.TaskId != artifact.TaskId
            || job.EmployeeId != artifact.EmployeeId
            || job.Digest != origin.JobDigest
            || job.ResultDigest != origin.ResultDigest
            || !SandboxTaskCurrent(db, job, depth + 1)
        )
            return false;
        var file = job.Result.Outputs.SingleOrDefault(f => f.Name == origin.OutputName);
        if (
            file is null
            || ContentGuard.Digest(file.Text) != origin.OutputDigest
            || origin.Start < 0
            || origin.Length is < 1 or > 12000
            || origin.Start > file.Text.Length - origin.Length
        )
            return false;
        try
        {
            return artifact.Body == Evidence(file.Text.Substring(origin.Start, origin.Length))
                && artifact.Digest == ContentGuard.Digest(artifact.Body);
        }
        catch (WorkspaceException)
        {
            return false;
        }
    }

    private CompanyArtifact PrepareSandboxArtifact(StoreConnection db, CompanyCommand command)
    {
        var job = Need<CompanySandboxJob>(db, command.Id);
        // Cleanup may advance the job revision; the saved result digest and selected text are immutable.
        if (
            command.ExpectedVersion < 1
            || command.ExpectedVersion > job.Version
            || command.Digest != job.ResultDigest
            || command.Decision != "prepare-review"
            || job is not { State: "completed", Revoked: false, Result.State: "completed" }
            || !SandboxTaskCurrent(db, job)
        )
            throw new WorkspaceException(
                "Choose an exact completed script result whose source authority is still current.",
                409
            );
        var input = CompanyJson.Decode<CompanySandboxArtifactInput>(command.Body ?? "null");
        if (input is null)
            throw new WorkspaceException("Select a saved output and its exact review text.", 422);
        var file = job.Result.Outputs.SingleOrDefault(f => f.Name == input.OutputName);
        if (file is null || string.IsNullOrWhiteSpace(input.Quote) || input.Quote.Length > 12000)
            throw new WorkspaceException(
                "Select a saved output and up to 12,000 exact characters for task review. The full output remains available.",
                422
            );
        var start = file.Text.IndexOf(input.Quote, StringComparison.Ordinal);
        if (start < 0)
            throw new WorkspaceException(
                "Use an exact excerpt from the saved output. Edited text can be saved separately as owner-authored work.",
                422
            );
        var origin = new CompanySandboxArtifactOrigin(
            job.Id,
            job.Digest,
            job.ResultDigest!,
            file.Name,
            ContentGuard.Digest(file.Text),
            start,
            input.Quote.Length
        );
        var previous = All<CompanyArtifact>(db).Where(a => a.SandboxOrigin?.JobId == job.Id).ToArray();
        if (previous.FirstOrDefault(a => a.SandboxOrigin == origin) is { } existing)
            return existing;
        if (previous.Length >= 8)
            throw new WorkspaceException(
                "This script already has eight retained output excerpts. Review an existing result.",
                409
            );
        var task = Need<CompanyTask>(db, job.TaskId);
        if (task.Kind != "work" || task.State is not ("queued" or "blocked" or "failed" or "awaiting_review"))
            throw new WorkspaceException(
                "Use a waiting work task for result review. Running, completed or canceled work cannot accept another result.",
                409
            );
        var body = Evidence(input.Quote);
        var artifact = new CompanyArtifact(
            Id(),
            task.Id,
            task.EmployeeId,
            body,
            ContentGuard.Digest(body),
            "needs_review",
            "sandbox-output",
            null,
            SandboxOrigin: origin
        );
        Save(db, artifact, artifact.Id, task.Id, task.EmployeeId, artifact.State, artifact.Version);
        Put(db, task with { State = "awaiting_review", Reason = null, Version = task.Version + 1 });
        Audit(db, "sandbox-output-prepared", artifact.Id, new { origin, artifact.Digest });
        Event(
            db,
            "artifact.ready",
            artifact.Id,
            artifact.EmployeeId,
            "A selected script output is ready for task review. Approval and sharing remain separate decisions."
        );
        return artifact;
    }
}
