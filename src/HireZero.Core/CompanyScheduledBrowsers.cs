namespace HireZero.Core;

public sealed record CompanyScheduledBrowser(string SourceJobId, string SourceDigest, CompanyBrowserInput Itinerary);

public sealed record CompanyBrowserScheduleOrigin(string ScheduleId, string Digest, string RunId);

public sealed record CompanyBrowserTemplateSummary(string Id, string Name, string EmployeeId, int Seconds);

public sealed partial class CompanyStore
{
    public CompanyBrowserTemplateSummary[] BrowserScheduleTemplates()
    {
        using var db = Open();
        return All<CompanyBrowserJob>(db)
            .Where(j => !j.Revoked && j.State is "proposed" or "completed" && j.Schedule is null && j.Plan is null)
            .OrderByDescending(j => j.UpdatedAt)
            .Take(50)
            .Select(j => new CompanyBrowserTemplateSummary(j.Id, j.Name, j.EmployeeId, j.Request.Limits.Seconds))
            .ToArray();
    }

    private CompanyScheduledBrowser ScheduledBrowserTemplate(
        StoreConnection db,
        CompanyScheduleInput input,
        bool existing
    )
    {
        var source = Need<CompanyBrowserJob>(db, input.BrowserJobId);
        if (
            !existing && (source.Revoked || source.State is not ("proposed" or "completed"))
            || source.EmployeeId != input.EmployeeId
            || source.Schedule is not null
            || source.Plan is not null
        )
            throw new WorkspaceException(
                "Choose this employee's saved standalone website review before scheduling it.",
                409
            );
        if (source.RuntimeIdentity != BrowserRuntimeIdentity || BrowserExecutionAvailable?.Invoke() != true)
            throw new WorkspaceException(
                "The saved website review's runtime is unavailable. Restore it before scheduling new visits.",
                409
            );
        source.Request.Validate();
        // A recurring invitation is separate from the original errand; pause the schedule to stop future visits.
        return new(
            source.Id,
            source.Digest,
            new(
                source.Name,
                source.Request.Origins,
                source.Request.Steps,
                source.Request.Limits,
                source.Request.Width,
                source.Request.Height,
                source.Request.Screenshots
            )
        );
    }

    private bool ScheduledBrowserTask(StoreConnection db, CompanyTask task) =>
        task.ScheduleRunId is { } runId
        && Find<CompanyScheduleRun>(db, runId) is { } run
        && Find<CompanySchedule>(db, run.ScheduleId)?.Definition.Kind == "browser-task";

    private CompanyBrowserScheduleOrigin? CaptureBrowserSchedule(StoreConnection db, CompanyTask task, bool scheduled)
    {
        if (task.ScheduleRunId is null)
            return null;
        if (!scheduled || !ScheduledBrowserTask(db, task) || !ScheduleRunCurrent(db, task))
            throw new WorkspaceException(
                "Recurring website visits are dispatched only by their exact approved schedule.",
                409
            );
        var run = Need<CompanyScheduleRun>(db, task.ScheduleRunId);
        if (run.TaskId != task.Id || run.BrowserId is not null)
            throw new WorkspaceException("This recurring occurrence already has its one website itinerary.", 409);
        return new(run.ScheduleId, run.ScheduleDigest, run.Id);
    }

    private bool BrowserScheduleSourceCurrent(StoreConnection db, CompanyTask task, CompanyBrowserJob job)
    {
        if (task.ScheduleRunId is null)
            return job.Schedule is null;
        if (job.Schedule is not { } saved || saved.RunId != task.ScheduleRunId)
            return false;
        var run = Find<CompanyScheduleRun>(db, saved.RunId);
        var schedule = Find<CompanySchedule>(db, saved.ScheduleId);
        // Historical captures keep their provenance after future visits end.
        return run is not null
            && schedule?.Definition.Kind == "browser-task"
            && run.ScheduleId == schedule.Id
            && run.ScheduleDigest == saved.Digest
            && schedule.Digest == saved.Digest
            && run.TaskId == task.Id
            && (run.BrowserId is null || run.BrowserId == job.Id);
    }

    public CompanyBrowserJob StartScheduledBrowser(string runId)
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
                    || run.BrowserId is not null
                    || schedule.State != "active"
                    || schedule.Definition.Kind != "browser-task"
                    || run.ScheduleDigest != schedule.Digest
                    || !ScheduleAuthorityCurrent(db, schedule)
                    || schedule.Authority.Browser is not { } browser
                )
                    throw new WorkspaceException("This recurring website review no longer has current authority.", 409);
                var task = NewTask(
                    db,
                    schedule.Definition.EmployeeId!,
                    schedule.ThreadId!,
                    PlatformSkills.Chat,
                    "Recurring website review: " + schedule.Definition.Name,
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
                var job = (CompanyBrowserJob)PrepareBrowser(
                    db,
                    new(run.Id, "browser.prepare", task.Id, task.Version, Body: CompanyJson.Encode(browser.Itinerary)),
                    scheduled: true
                );
                run = run with { BrowserId = job.Id };
                SaveScheduleRun(db, run);
                job = (CompanyBrowserJob)StartBrowser(
                    db,
                    new(run.Id, "browser.run", job.Id, job.Version, Digest: job.Digest, Decision: "approve-once"),
                    schedule.ApprovedBy
                );
                Event(
                    db,
                    "schedule.dispatched",
                    run.Id,
                    task.EmployeeId,
                    "Recurring approval consumed one exact public website itinerary. Captures will need owner review."
                );
                return job;
            }
        );
    }

    private void FinishScheduledBrowser(StoreConnection db, CompanyBrowserJob job)
    {
        if (job.Schedule is not { } origin)
            return;
        var run = Need<CompanyScheduleRun>(db, origin.RunId);
        if (run.State != "running" || run.BrowserId != job.Id)
            return;
        if (job.SettledAt is null)
        {
            if (job.State != "running")
                SaveScheduleRun(
                    db,
                    run with
                    {
                        Reason =
                            "This website review needs resource recovery before another occurrence can start. Open its saved work; it will not be replayed.",
                    }
                );
            return;
        }
        var schedule = Need<CompanySchedule>(db, origin.ScheduleId);
        var success = job.State == "completed";
        var reason = success
            ? null
            : "Website review outcome: " + job.State + ". Review its saved work before authorizing future occurrences.";
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
                    Reason = "The recurring website captures are saved. Select page text for review.",
                    Version = task.Version + 1,
                }
            );
        Event(
            db,
            "schedule.result",
            run.Id,
            job.EmployeeId,
            success
                ? "Recurring website captures are ready for owner review: " + schedule.Definition.Name
                : "Recurring website review stopped: " + schedule.Definition.Name + ". " + reason
        );
    }

    private void RecoverScheduledBrowser(string id)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                FinishScheduledBrowser(db, Need<CompanyBrowserJob>(db, id));
                return true;
            }
        );
    }
}
