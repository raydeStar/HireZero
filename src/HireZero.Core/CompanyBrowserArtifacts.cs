namespace HireZero.Core;

public sealed record CompanyBrowserArtifactInput(int Step, string Quote);

public sealed record CompanyBrowserArtifactOrigin(
    string JobId,
    string JobDigest,
    string ResultDigest,
    int Step,
    string Url,
    DateTimeOffset CapturedAt,
    bool Partial,
    string TextDigest,
    int Start,
    int Length
);

public sealed partial class CompanyStore
{
    private bool BrowserArtifactCurrent(StoreConnection db, CompanyArtifact artifact, int depth = 0)
    {
        if (artifact.BrowserOrigin is not { } origin)
            return true;
        if (depth >= 16 || artifact.SandboxOrigin is not null)
            return false;
        var job = Find<CompanyBrowserJob>(db, origin.JobId);
        if (
            job is not { State: "completed", Revoked: false, Execution: { BrowserExited: true } }
            || job.TaskId != artifact.TaskId
            || job.EmployeeId != artifact.EmployeeId
            || job.Digest != origin.JobDigest
            || job.ResultDigest != origin.ResultDigest
            || !BrowserTaskCurrent(db, job, depth + 1)
        )
            return false;
        if (job.CaptureDeletion is { } deletion)
            return deletion.Artifacts.Any(a =>
                    a.Id == artifact.Id && a.Digest == artifact.Digest && a.OriginDigest == CompanyJson.Digest(origin)
                )
                && artifact.Digest == ContentGuard.Digest(artifact.Body);
        if (job.Execution.Result is not { State: "completed", ContextClosed: true } result)
            return false;
        var capture = result.Captures.SingleOrDefault(c => c.Step == origin.Step);
        if (
            capture is null
            || capture.Url != origin.Url
            || capture.CapturedAt != origin.CapturedAt
            || job.Execution.Result.Partial != origin.Partial
            || ContentGuard.Digest(capture.Text) != origin.TextDigest
            || origin.Start < 0
            || origin.Length is < 1 or > 8000
            || origin.Start > capture.Text.Length - origin.Length
        )
            return false;
        try
        {
            return artifact.Body == Evidence(capture.Text.Substring(origin.Start, origin.Length))
                && artifact.Digest == ContentGuard.Digest(artifact.Body);
        }
        catch (WorkspaceException)
        {
            return false;
        }
    }

    private CompanyArtifact PrepareBrowserArtifact(StoreConnection db, CompanyCommand command)
    {
        var job = Need<CompanyBrowserJob>(db, command.Id);
        if (
            command.ExpectedVersion < 1
            || command.ExpectedVersion > job.Version
            || command.Digest != job.ResultDigest
            || command.Decision != "prepare-review"
            || job
                is not {
                    State: "completed",
                    Revoked: false,
                    Execution: { BrowserExited: true, Result: { State: "completed", ContextClosed: true } }
                }
            || !BrowserTaskCurrent(db, job)
        )
            throw new WorkspaceException(
                "Select a completed browser capture with current source authority and confirmed browser exit.",
                409
            );
        var input = CompanyJson.Decode<CompanyBrowserArtifactInput>(command.Body ?? "null");
        if (input is null || string.IsNullOrWhiteSpace(input.Quote) || input.Quote.Length > 8000)
            throw new WorkspaceException("Select up to 8,000 exact characters from one saved page capture.", 422);
        var capture = job.Execution.Result.Captures.SingleOrDefault(c => c.Step == input.Step);
        var start = capture?.Text.IndexOf(input.Quote, StringComparison.Ordinal) ?? -1;
        if (capture is null || start < 0)
            throw new WorkspaceException(
                "Use an exact excerpt from the saved page. Edited text can be saved as owner-authored work.",
                422
            );
        var origin = new CompanyBrowserArtifactOrigin(
            job.Id,
            job.Digest,
            job.ResultDigest!,
            capture.Step,
            capture.Url,
            capture.CapturedAt,
            job.Execution.Result.Partial,
            ContentGuard.Digest(capture.Text),
            start,
            input.Quote.Length
        );
        var previous = All<CompanyArtifact>(db).Where(a => a.BrowserOrigin?.JobId == job.Id).ToArray();
        if (previous.FirstOrDefault(a => a.BrowserOrigin == origin) is { } existing)
            return existing;
        if (previous.Length >= 8)
            throw new WorkspaceException(
                "This browser job already has eight retained excerpts. Review an existing result.",
                409
            );
        var task = Need<CompanyTask>(db, job.TaskId);
        if (task.Kind != "work" || task.State is not ("queued" or "blocked" or "failed" or "awaiting_review"))
            throw new WorkspaceException(
                "Use a waiting work task for page review. Running or finished work cannot accept another result.",
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
            "browser-capture",
            null,
            BrowserOrigin: origin
        );
        Save(db, artifact, artifact.Id, task.Id, task.EmployeeId, artifact.State, artifact.Version);
        Put(db, task with { State = "awaiting_review", Reason = null, Version = task.Version + 1 });
        Audit(db, "browser-output-prepared", artifact.Id, new { origin, artifact.Digest });
        Event(
            db,
            "artifact.ready",
            artifact.Id,
            artifact.EmployeeId,
            "A dated public-page excerpt is ready for task review. It is evidence, not instructions or approved company knowledge."
        );
        return artifact;
    }
}
