using System.Text;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    public string? BrowserRuntimeIdentity { get; init; }
    public Func<bool>? BrowserExecutionAvailable { get; init; }
    private const string BrowserSchema =
        "CREATE TABLE co_browser_jobs(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_tasks(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));";

    private void SaveBrowser(StoreConnection db, CompanyBrowserJob job) =>
        Save(db, job, job.Id, job.TaskId, job.EmployeeId, job.State, job.Version);

    public CompanyBrowserJob BrowserJob(string id)
    {
        using var db = Open();
        return Need<CompanyBrowserJob>(db, id);
    }

    public CompanyBrowserJob[] PendingBrowserRecovery()
    {
        using var db = Open();
        return All<CompanyBrowserJob>(db).Where(j => j.StartedAt is not null && j.SettledAt is null).ToArray();
    }

    public CompanyComputeJobSummary[] ComputeSummaries()
    {
        using var db = Open();
        return All<CompanySandboxJob>(db)
            .Select(j =>
                (
                    j.UpdatedAt,
                    Summary: new CompanyComputeJobSummary(
                        j.Id,
                        j.TaskId,
                        j.Name,
                        "script",
                        j.State,
                        j.Period,
                        j.StartedAt,
                        j.SettledAt,
                        j.AllocatedSeconds,
                        j.StartedAt is not null && j.SettledAt is null ? j.Request.Limits.Seconds : 0,
                        j.Result?.WallMilliseconds,
                        j.Release,
                        j.Request.Tier
                    )
                )
            )
            .Concat(
                All<CompanyBrowserJob>(db)
                    .Select(j =>
                        (
                            j.UpdatedAt,
                            Summary: new CompanyComputeJobSummary(
                                j.Id,
                                j.TaskId,
                                j.Name,
                                "browser",
                                j.State,
                                j.Period,
                                j.StartedAt,
                                j.SettledAt,
                                j.AllocatedSeconds,
                                j.StartedAt is not null && j.SettledAt is null ? j.ReservedSeconds : 0,
                                j.Execution?.WallMilliseconds,
                                j.Release
                            )
                        )
                    )
            )
            .OrderByDescending(j => j.UpdatedAt)
            .ThenBy(j => j.Summary.Id)
            .Take(50)
            .Select(j => j.Summary)
            .ToArray();
    }

    private bool BrowserTaskCurrent(StoreConnection db, CompanyBrowserJob job, int depth = 0) =>
        IsolatedTaskCurrent(
            db,
            job.TaskId,
            job.EmployeeId,
            job.TaskGeneration,
            job.EmployeeGeneration,
            depth,
            job.Plan is not null,
            job.Schedule is not null
        )
        && PlanAuthorityCurrent(db, Need<CompanyTask>(db, job.TaskId), job.Plan, depth)
        && BrowserScheduleSourceCurrent(db, Need<CompanyTask>(db, job.TaskId), job);

    private bool BrowserAuthorityCurrent(StoreConnection db, CompanyBrowserJob job, bool deadline = true) =>
        !job.Revoked
        && job.RuntimeIdentity == BrowserRuntimeIdentity
        && (!deadline || Clock() < job.ExpiresAt)
        && job.ApprovedBy is { } owner
        && Find<CompanyMember>(db, owner) is { State: "active", Role: CompanyRoles.Owner }
        && !ComputePolicy(db).Limits.Paused
        && Need<CompanyTask>(db, job.TaskId).State is not ("completed" or "running")
        && ParentValid(db, Need<CompanyTask>(db, job.TaskId))
        && BrowserTaskCurrent(db, job);

    private object PrepareBrowser(StoreConnection db, CompanyCommand command, bool scheduled = false)
    {
        if (string.IsNullOrWhiteSpace(BrowserRuntimeIdentity) || BrowserExecutionAvailable?.Invoke() != true)
            throw new WorkspaceException(
                "A qualified browser runtime must be ready before preparing website work.",
                409
            );
        var task = Need<CompanyTask>(db, command.Id);
        Version(task.Version, command.ExpectedVersion);
        if (task.Kind != "work" || task.State is "completed" or "running")
            throw new WorkspaceException(
                "Choose an unfinished work task without an active model request for a website review.",
                409
            );
        var employee = Need<CompanyEmployee>(db, task.EmployeeId);
        var plan = CapturePlanAuthority(db, task);
        var schedule = CaptureBrowserSchedule(db, task, scheduled);
        var input = CompanyJson.Decode<CompanyBrowserInput>(command.Body ?? "null");
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
            throw new WorkspaceException("Name the website review and choose its exact itinerary.", 422);
        var name = Text(input.Name, 120).Trim();
        var id = Id();
        var request = new BrowserRequest(
            id,
            CompanyId,
            input.Origins,
            input.Steps,
            input.Limits,
            input.Width,
            input.Height,
            input.Screenshots
        );
        request.Validate();
        var expires = Clock().AddHours(1);
        var digest = CompanyJson.Digest(
            new
            {
                task.Id,
                TaskGeneration = task.Generation,
                Employee = employee.Id,
                EmployeeGeneration = employee.Generation,
                name,
                Runtime = BrowserRuntimeIdentity,
                request,
                plan,
                expires,
                ReservedSeconds = request.Limits.Seconds + 12,
            }
        );
        if (schedule is not null)
            digest = CompanyJson.Digest(new { digest, schedule });
        var job = new CompanyBrowserJob(
            id,
            task.Id,
            employee.Id,
            task.Generation,
            employee.Generation,
            name,
            BrowserRuntimeIdentity,
            request,
            digest,
            "proposed",
            1,
            Clock(),
            expires,
            request.Limits.Seconds + 12,
            Plan: plan,
            Schedule: schedule
        );
        if (!BrowserTaskCurrent(db, job))
            throw new WorkspaceException(
                "Use an active employee's current task with approved context and completed, approved predecessors.",
                409
            );
        if (All<CompanyBrowserJob>(db).Count(j => j.State == "proposed") >= 20)
            throw new WorkspaceException(
                "Review or revoke a pending browser proposal first. At most 20 may wait.",
                429
            );
        SaveBrowser(db, job);
        Event(
            db,
            "browser.proposed",
            id,
            employee.Id,
            "A public website itinerary is ready for owner review. No website has been visited."
        );
        return job;
    }

    private object StartBrowser(StoreConnection db, CompanyCommand command, string? scheduledOwner = null)
    {
        DemandWorkAdmission(db);
        if (BrowserExecutionAvailable?.Invoke() != true)
            throw new WorkspaceException(
                "The browser runtime is not ready for new work. Saved jobs remain recoverable.",
                409
            );
        var job = Need<CompanyBrowserJob>(db, command.Id);
        Version(job.Version, command.ExpectedVersion);
        if (job.State != "proposed" || job.Digest != command.Digest || command.Decision != "approve-once")
            throw new WorkspaceException("Approve these exact websites, steps, screenshots and limits once.", 409);
        if (HasUnsettledCompute(db, job.TaskId))
            throw new WorkspaceException(
                "Finish or recover this task's current script or browser resources before starting another execution.",
                409
            );
        job = job with { ApprovedBy = scheduledOwner ?? CurrentActor.Value!.Subject };
        if (!BrowserAuthorityCurrent(db, job))
            throw new WorkspaceException(
                "Browser authority changed or compute is paused. Review current context and limits.",
                409
            );
        var usage = ComputeUsage(db);
        DemandBillingCompute(db, usage, job.ReservedSeconds);
        if (
            usage.Jobs + usage.CarriedJobs >= usage.Policy.Limits.Jobs
            || usage.AllocatedSeconds + usage.HeldSeconds + usage.CarriedSeconds + job.ReservedSeconds
                > usage.Policy.Limits.Seconds
        )
            throw new WorkspaceException(
                "The shared monthly compute allowance is exhausted or held by unresolved work.",
                429
            );
        job = job with
        {
            State = "running",
            Version = job.Version + 1,
            UpdatedAt = Clock(),
            StartedAt = Clock(),
            Period = UsagePeriod,
            PolicyVersion = usage.Policy.Version,
        };
        SaveBrowser(db, job);
        Event(
            db,
            "browser.authorized",
            job.Id,
            job.EmployeeId,
            "Owner approved this itinerary once. Browser time and cleanup overhead are reserved from shared compute."
        );
        return job;
    }

    private object RevokeBrowser(StoreConnection db, CompanyCommand command)
    {
        var job = Need<CompanyBrowserJob>(db, command.Id);
        // Progress must not turn a cancellation click into a version-conflict puzzle.
        if (command.ExpectedVersion < 1 || command.ExpectedVersion > job.Version)
            throw new WorkspaceException("Use a saved browser review before revoking its authority.", 409);
        if (job.Revoked)
            return job;
        job = job with
        {
            Revoked = true,
            State = job.StartedAt is null ? "revoked" : job.State,
            Version = job.Version + 1,
            UpdatedAt = Clock(),
        };
        SaveBrowser(db, job);
        Event(
            db,
            "browser.revoked",
            job.Id,
            job.EmployeeId,
            "Browser authority was revoked. Unresolved resources keep their allowance until release is confirmed."
        );
        return job;
    }

    public CompanyBrowserJob? TakeBrowserDispatch(string id, string runtimeIdentity)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanyBrowserJob>(db, id);
                if (
                    job.State != "running"
                    || job.DispatchTaken
                    || job.RuntimeIdentity != runtimeIdentity
                    || BrowserExecutionAvailable?.Invoke() != true
                    || !BrowserAuthorityCurrent(db, job)
                )
                    return null;
                job = job with
                {
                    DispatchTaken = true,
                    ExpiresAt = Clock().AddSeconds(job.ReservedSeconds + 180),
                    Version = job.Version + 1,
                    UpdatedAt = Clock(),
                };
                SaveBrowser(db, job);
                return job;
            }
        );
    }

    public bool BrowserAuthorized(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanyBrowserJob>(db, id);
                return job.State == "running"
                    && job.DispatchTaken
                    && BrowserExecutionAvailable?.Invoke() == true
                    && BrowserAuthorityCurrent(db, job);
            }
        );
    }

    public CompanyBrowserJob FenceBrowserRecovery(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanyBrowserJob>(db, id);
                if (job.StartedAt is null || job.SettledAt is not null || job.State != "running")
                    return job;
                job = job with { State = "unknown", Version = job.Version + 1, UpdatedAt = Clock() };
                SaveBrowser(db, job);
                Event(
                    db,
                    "browser.unknown",
                    job.Id,
                    job.EmployeeId,
                    "Browser execution was interrupted. Recovery can read its existing receipt or release resources; it cannot revisit websites."
                );
                return job;
            }
        );
    }

    public CompanyBrowserJob RecordBrowserResult(string id, string runtimeIdentity, BrowserExecution execution)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanyBrowserJob>(db, id);
                if (job.StartedAt is null || job.RuntimeIdentity != runtimeIdentity)
                    throw new WorkspaceException("This receipt does not belong to an approved browser runtime.", 409);
                if (job.SettledAt is not null || job.Execution is { State: not "unknown" and not "not-sent" })
                    return job;
                ValidateBrowserExecution(job, execution);
                if (!job.DispatchTaken && execution.State != "not-sent")
                    throw new WorkspaceException("No dispatch was taken for this browser receipt.", 409);
                // Unknown measurements stay unknown during recovery; never substitute the worker's diagnostic counters.
                if (job.Execution is { } previous)
                    execution = execution with
                    {
                        CreationConfirmed = execution.CreationConfirmed || previous.CreationConfirmed,
                    };
                var state = execution.State;
                if (state == "completed" && (!execution.BrowserExited || execution.Result?.ContextClosed != true))
                    state = "cleanup-incomplete";
                if (execution.Result is not null && !BrowserAuthorityCurrent(db, job, deadline: false))
                {
                    state = "withheld";
                    execution = execution with { Result = null, Reason = "authority-changed" };
                }
                var digest = CompanyJson.Digest(execution);
                if (digest == job.ResultDigest)
                    return job;
                job = job with
                {
                    Execution = execution,
                    ResultDigest = digest,
                    State = state,
                    Version = job.Version + 1,
                    UpdatedAt = Clock(),
                };
                SaveBrowser(db, job);
                Audit(db, "browser-result", job.Id, new { job.ResultDigest, job.State });
                FinishScheduledBrowser(db, job);
                Event(
                    db,
                    "browser.result",
                    job.Id,
                    job.EmployeeId,
                    state == "completed"
                        ? "Website captures are saved for review. Resource release is tracked separately."
                        : "Browser outcome: " + state + ". Review its receipt and resource release status."
                );
                return job;
            }
        );
    }

    private static void ValidateBrowserExecution(CompanyBrowserJob job, BrowserExecution e)
    {
        if (
            e is null
            || e.State is not ("completed" or "failed" or "timed-out" or "canceled" or "unknown" or "not-sent")
            || e.Release is null
            || e.Release.Status
                is not ("not-created" or "not-observed" or "awaiting-release" or "awaiting-reconciliation")
            || e.Release.Confirmed != (e.Release.Status == "not-created")
            || e.Release.Confirmed && (e.State != "not-sent" || e.CreationConfirmed)
            || e.Release.Confirmed && job.Execution is { CreationConfirmed: true }
            || string.IsNullOrWhiteSpace(e.Release.ResourceId)
            || e.Release.ResourceId.Length > 200
            || job.Execution is { } previous && previous.Release.ResourceId != e.Release.ResourceId
            || e.Reason?.Length > 500
            || e.WallMilliseconds is < 0
            || (e.State is "unknown" or "not-sent") != (e.Result is null)
            || e.Result is not null && (e.State != e.Result.State || !e.CreationConfirmed)
            || e.Network is { } n
                && (
                    n.Requests < 0
                    || n.Requests > job.Request.Limits.Requests
                    || n.ReceivedBytes < 0
                    || n.ReceivedBytes > job.Request.Limits.Bytes + 1L
                    || n.Refused < 0
                    || n.Refused > job.Request.Limits.Requests + 1
                )
        )
            throw new WorkspaceException("Browser receipt exceeds the approved execution envelope.", 422);
        e.Result?.Validate(job.Request);
        if (Encoding.UTF8.GetByteCount(CompanyJson.Encode(e)) > BrowserRequest.ResultBytes + 4096)
            throw new WorkspaceException("Browser receipt exceeds its saved output limit.", 422);
    }

    public CompanyBrowserJob RecordBrowserRelease(string id, string runtimeIdentity, SandboxRelease release)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanyBrowserJob>(db, id);
                if (
                    job.RuntimeIdentity != runtimeIdentity
                    || job.Execution is null
                    || release is null
                    || release.ResourceId != job.Execution.Release.ResourceId
                    || release.Status
                        is not (
                            "released"
                            or "not-created"
                            or "release-unconfirmed"
                            or "provisioning-unconfirmed"
                            or "scope-refused"
                        )
                    || release.Confirmed != (release.Status is "released" or "not-created")
                    || release.Status == "not-created"
                        && (
                            job.Execution.State != "not-sent"
                            || !job.Execution.Release.Confirmed
                            || job.Execution.CreationConfirmed
                        )
                )
                    throw new WorkspaceException(
                        "Save the matching browser receipt before recording resource release.",
                        409
                    );
                if (job.SettledAt is not null || job.Release == release)
                    return job;
                job = job with
                {
                    Release = release,
                    Version = job.Version + 1,
                    UpdatedAt = Clock(),
                    AllocatedSeconds = release.Confirmed
                        ? release.Status == "not-created"
                            ? 0
                            : job.ReservedSeconds
                        : null,
                    SettledAt = release.Confirmed ? Clock() : null,
                };
                SaveBrowser(db, job);
                Audit(
                    db,
                    "browser-release",
                    job.Id,
                    new
                    {
                        job.Release,
                        job.AllocatedSeconds,
                        job.SettledAt,
                    }
                );
                FinishScheduledBrowser(db, job);
                Event(
                    db,
                    release.Confirmed ? "browser.released" : "browser.release-attention",
                    job.Id,
                    job.EmployeeId,
                    release.Confirmed
                        ? "Browser resources were released. Allocated time includes startup and cleanup; no monetary charge is inferred."
                        : "Browser release is unconfirmed. Its allowance remains held and the itinerary will not run again."
                );
                return job;
            }
        );
    }
}
