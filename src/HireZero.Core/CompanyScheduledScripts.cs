namespace HireZero.Core;

public sealed record CompanyScheduledScript(
    string SourceJobId,
    string SourceDigest,
    string Name,
    string Script,
    CompanySandboxSelection[] Documents,
    string[] Outputs,
    SandboxLimits Limits,
    string? Tier,
    CompanySandboxPremium? Premium
);

public sealed record CompanyScriptScheduleOrigin(string ScheduleId, string Digest, string RunId);

public sealed record CompanyScriptTemplateSummary(string Id, string Name, string EmployeeId, int Seconds, string? Tier);

public sealed partial class CompanyStore
{
    public CompanyScriptTemplateSummary[] ScriptScheduleTemplates()
    {
        using var db = Open();
        return All<CompanySandboxJob>(db)
            .Where(j =>
                !j.Revoked
                && j.State is "proposed" or "completed"
                && (j.Evidence?.Length ?? 0) == 0
                && j.Schedule is null
                && j.Plan is null
            )
            .OrderByDescending(j => j.UpdatedAt)
            .Take(50)
            .Select(j => new CompanyScriptTemplateSummary(
                j.Id,
                j.Name,
                j.EmployeeId,
                j.Request.Limits.Seconds,
                j.Request.Tier
            ))
            .ToArray();
    }

    private CompanyScheduledScript ScheduledScriptTemplate(
        StoreConnection db,
        CompanyScheduleInput input,
        bool existing
    )
    {
        var source = Need<CompanySandboxJob>(db, input.ScriptJobId);
        if (
            !existing && (source.Revoked || source.State is not ("proposed" or "completed"))
            || source.EmployeeId != input.EmployeeId
            || source.Schedule is not null
            || source.Plan is not null
            || (source.Evidence?.Length ?? 0) != 0
        )
            throw new WorkspaceException(
                "Choose this employee's saved standalone script with approved document inputs. Task-result inputs cannot be refreshed by a schedule.",
                409
            );
        if (
            source.RuntimeIdentity != SandboxRuntimeIdentity
            || SandboxExecutionAvailable?.Invoke() == false
            || !SandboxTierCurrent(db, source)
        )
            throw new WorkspaceException(
                "The saved script's runtime or paid tier is unavailable. Prepare a current script review before scheduling it.",
                409
            );
        source.Request.Validate();
        // Recurrence has its own exact grant. Discarding the original one-off proposal does not cancel it;
        // pause or end the schedule itself. Script text, selections and limits never change on that proposal.
        var employee = Need<CompanyEmployee>(db, source.EmployeeId);
        foreach (var selected in source.Documents)
            LatestScheduledDocument(db, selected.DocumentId, employee);
        return new(
            source.Id,
            source.Digest,
            source.Name,
            source.Request.Script,
            source.Documents.Select(d => new CompanySandboxSelection(d.DocumentId, d.Name)).ToArray(),
            source.Request.Outputs,
            source.Request.Limits,
            source.Request.Tier,
            source.Premium
        );
    }

    private CompanyDocument LatestScheduledDocument(StoreConnection db, string rootId, CompanyEmployee employee)
    {
        Need<CompanyDocument>(db, rootId);
        var documents = StoredDocuments(db);
        var lineage = new HashSet<string>(StringComparer.Ordinal) { rootId };
        bool changed;
        do
        {
            changed = false;
            foreach (var document in documents)
                if (document.ReplacesId is { } previous && lineage.Contains(previous))
                    changed |= lineage.Add(document.Id);
        } while (changed);
        var approved = documents.Where(d => lineage.Contains(d.Id) && d.State == "approved").ToArray();
        if (approved.Length != 1)
            throw new WorkspaceException(
                "Each selected input needs one unambiguous approved revision. Review its replacements before resuming.",
                409
            );
        var current = approved[0];
        if (
            !(
                current.DepartmentId is null && current.EmployeeId is null
                || current.DepartmentId == employee.DepartmentId && current.EmployeeId is null
                || current.EmployeeId == employee.Id
            )
        )
            throw new WorkspaceException("The approved replacement is outside this employee's document audience.", 409);
        return current;
    }

    private bool ScheduledScriptTask(StoreConnection db, CompanyTask task) =>
        task.ScheduleRunId is { } runId
        && Find<CompanyScheduleRun>(db, runId) is { } run
        && Find<CompanySchedule>(db, run.ScheduleId)?.Definition.Kind == "script-task";

    private CompanyScriptScheduleOrigin? CaptureScriptSchedule(StoreConnection db, CompanyTask task, bool scheduled)
    {
        if (task.ScheduleRunId is null)
            return null;
        if (!scheduled || !ScheduledScriptTask(db, task) || !ScheduleRunCurrent(db, task))
            throw new WorkspaceException(
                "Recurring script work is dispatched only by its exact approved schedule.",
                409
            );
        var run = Need<CompanyScheduleRun>(db, task.ScheduleRunId);
        if (run.TaskId != task.Id || run.SandboxId is not null)
            throw new WorkspaceException("This recurring occurrence already has its one script packet.", 409);
        return new(run.ScheduleId, run.ScheduleDigest, run.Id);
    }

    private bool ScriptScheduleSourceCurrent(StoreConnection db, CompanyTask task, CompanySandboxJob job)
    {
        if (task.ScheduleRunId is null)
            return job.Schedule is null;
        if (job.Schedule is not { } saved || saved.RunId != task.ScheduleRunId)
            return false;
        var run = Find<CompanyScheduleRun>(db, saved.RunId);
        var schedule = Find<CompanySchedule>(db, saved.ScheduleId);
        // Ending future errands does not erase a report already delivered under valid authority.
        return run is not null
            && schedule?.Definition.Kind == "script-task"
            && run.ScheduleId == schedule.Id
            && run.ScheduleDigest == saved.Digest
            && schedule.Digest == saved.Digest
            && run.TaskId == task.Id
            && (run.SandboxId is null || run.SandboxId == job.Id);
    }

    public CompanySandboxJob StartScheduledSandbox(string runId)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var run = Need<CompanyScheduleRun>(db, runId);
                var schedule = Need<CompanySchedule>(db, run.ScheduleId);
                if (
                    run.State != "claimed"
                    || run.SandboxId is not null
                    || schedule.State != "active"
                    || schedule.Definition.Kind != "script-task"
                    || run.ScheduleDigest != schedule.Digest
                    || !ScheduleAuthorityCurrent(db, schedule)
                    || schedule.Authority.Script is not { } script
                )
                    throw new WorkspaceException(
                        "This recurring script occurrence no longer has current authority.",
                        409
                    );
                var task = NewTask(
                    db,
                    schedule.Definition.EmployeeId!,
                    schedule.ThreadId!,
                    PlatformSkills.Chat,
                    "Recurring script report: " + schedule.Definition.Name,
                    [],
                    [],
                    null,
                    null,
                    null
                ) with
                {
                    ScheduleRunId = run.Id,
                    Watermark = 0,
                    ModelRoute = null,
                    Runtime = null,
                };
                Put(db, task);
                run = run with { State = "running", TaskId = task.Id };
                SaveScheduleRun(db, run);
                var job = (CompanySandboxJob)PrepareSandbox(
                    db,
                    new(
                        run.Id,
                        "sandbox.prepare",
                        task.Id,
                        task.Version,
                        Body: CompanyJson.Encode(
                            new CompanySandboxInput(
                                script.Name,
                                script.Script,
                                script
                                    .Documents.Select(d => new CompanySandboxSelection(
                                        LatestScheduledDocument(
                                            db,
                                            d.DocumentId,
                                            Need<CompanyEmployee>(db, task.EmployeeId)
                                        ).Id,
                                        d.Name
                                    ))
                                    .ToArray(),
                                script.Outputs,
                                script.Limits,
                                Tier: script.Tier
                            )
                        )
                    ),
                    scheduled: true
                );
                run = run with { SandboxId = job.Id };
                SaveScheduleRun(db, run);
                job = (CompanySandboxJob)StartSandbox(
                    db,
                    new(run.Id, "sandbox.run", job.Id, job.Version, Digest: job.Digest, Decision: "approve-once"),
                    schedule.ApprovedBy
                );
                Event(
                    db,
                    "schedule.dispatched",
                    run.Id,
                    task.EmployeeId,
                    "Recurring approval consumed one exact script packet using the latest approved versions of its selected documents."
                );
                return job;
            }
        );
    }

    private void FinishScheduledSandbox(StoreConnection db, CompanySandboxJob job)
    {
        if (job.Schedule is not { } origin)
            return;
        var run = Need<CompanyScheduleRun>(db, origin.RunId);
        if (run.State != "running" || run.SandboxId != job.Id)
            return;
        if (job.SettledAt is null)
        {
            if (job.State != "running")
                SaveScheduleRun(
                    db,
                    run with
                    {
                        Reason =
                            "This script needs resource recovery before another occurrence can start. Open its saved work; it will not be replayed.",
                    }
                );
            return;
        }
        var schedule = Need<CompanySchedule>(db, origin.ScheduleId);
        var success = job.State == "completed";
        var reason = success
            ? null
            : "Script outcome: " + job.State + ". Review its saved work before authorizing future occurrences.";
        SaveScheduleRun(
            db,
            run with
            {
                State =
                    success ? "completed"
                    : job.State == "unknown" ? "unknown"
                    : "failed",
                Reason = reason,
                CompletedAt = Clock(),
            }
        );
        if (schedule.State == "active")
            SaveSchedule(
                db,
                schedule with
                {
                    State =
                        !success ? "blocked"
                        : schedule.NextRunAt is null ? "completed"
                        : "active",
                    Reason = reason,
                    Version = schedule.Version + 1,
                    UpdatedAt = Clock(),
                }
            );
        var task = Need<CompanyTask>(db, job.TaskId);
        if (success && task.State is "queued" or "blocked")
            Put(
                db,
                task with
                {
                    State = "awaiting_review",
                    Reason = "The recurring script result is saved. Select an output for review.",
                    Version = task.Version + 1,
                }
            );
        Event(
            db,
            "schedule.result",
            run.Id,
            job.EmployeeId,
            success
                ? "Recurring script output is ready for owner review: " + schedule.Definition.Name
                : "Recurring script stopped: " + schedule.Definition.Name + ". " + reason
        );
    }

    private void RecoverScheduledScript(string id)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                FinishScheduledSandbox(db, Need<CompanySandboxJob>(db, id));
                return true;
            }
        );
    }
}
