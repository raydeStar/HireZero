using System.Text;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    public string? SandboxRuntimeIdentity { get; init; }
    public Func<bool>? SandboxExecutionAvailable { get; init; }

    private const string SandboxSchema =
        "CREATE TABLE co_sandbox_jobs(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_tasks(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));"
        + "CREATE TABLE co_compute_policy(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    private CompanyComputePolicy ComputePolicy(StoreConnection db) =>
        Find<CompanyComputePolicy>(db, "monthly") ?? new("monthly", new(), 0, DateTimeOffset.MinValue);

    private CompanyComputeUsage ComputeUsage(StoreConnection db)
    {
        var dispatched = All<CompanySandboxJob>(db)
            .Where(j => j.StartedAt is not null)
            .Select(j => (j.Period, j.SettledAt, j.AllocatedSeconds, ReservedSeconds: j.Request.Limits.Seconds))
            .Concat(
                All<CompanyBrowserJob>(db)
                    .Where(j => j.StartedAt is not null)
                    .Select(j => (j.Period, j.SettledAt, j.AllocatedSeconds, j.ReservedSeconds))
            )
            .ToArray();
        var current = dispatched.Where(j => j.Period == UsagePeriod).ToArray();
        var held = current.Where(j => j.SettledAt is null).ToArray();
        var carried = dispatched.Where(j => j.Period != UsagePeriod && j.SettledAt is null).ToArray();
        return new(
            UsagePeriod,
            PeriodEnd(UsagePeriod),
            ComputePolicy(db),
            current.Length,
            current.Sum(j => j.AllocatedSeconds ?? 0),
            held.Length,
            held.Sum(j => j.ReservedSeconds),
            carried.Length,
            carried.Sum(j => j.ReservedSeconds),
            PaidAllowance(db),
            BillingRequired(db)
        );
    }

    public CompanyComputeUsage ComputeUsage()
    {
        using var db = Open();
        return Transaction(db, () => ComputeUsage(db));
    }

    private object ConfigureCompute(StoreConnection db, CompanyCommand command)
    {
        var policy = ComputePolicy(db);
        Version(policy.Version, command.ExpectedVersion);
        var limits = CompanyJson.Decode<CompanyComputeLimits>(command.Body ?? "null");
        if (limits.Jobs is < 0 or > 1000 || limits.Seconds is < 0 or > 120_000)
            throw new WorkspaceException(
                "Choose 0–1,000 compute jobs and 0–120,000 allocated seconds per UTC month, shared by scripts and browsers.",
                422
            );
        var next = new CompanyComputePolicy("monthly", limits, policy.Version + 1, Clock());
        Save(db, next, next.Id, version: next.Version);
        Event(
            db,
            "compute.limits-changed",
            next.Id,
            null,
            "Owner updated shared compute safety limits. Each script or browser itinerary still needs exact approval."
        );
        return next;
    }

    private void SaveSandbox(StoreConnection db, CompanySandboxJob job) =>
        Save(db, job, job.Id, job.TaskId, job.EmployeeId, job.State, job.Version);

    public CompanySandboxJob SandboxJob(string id)
    {
        using var db = Open();
        return Need<CompanySandboxJob>(db, id);
    }

    public object[] SandboxSummaries()
    {
        using var db = Open();
        return All<CompanySandboxJob>(db)
            .OrderByDescending(j => j.UpdatedAt)
            .Take(50)
            .Select(j =>
                (object)
                    new
                    {
                        j.Id,
                        j.TaskId,
                        j.Name,
                        j.State,
                        j.Period,
                        j.StartedAt,
                        j.SettledAt,
                        j.AllocatedSeconds,
                        heldSeconds = j.StartedAt is not null && j.SettledAt is null ? j.Request.Limits.Seconds : 0,
                        wallMilliseconds = j.Result?.WallMilliseconds,
                        j.Release,
                    }
            )
            .ToArray();
    }

    public CompanySandboxJob[] PendingSandboxRecovery()
    {
        using var db = Open();
        return All<CompanySandboxJob>(db).Where(j => j.StartedAt is not null && j.SettledAt is null).ToArray();
    }

    private bool SandboxTaskCurrent(StoreConnection db, CompanySandboxJob job, int depth = 0)
    {
        if (
            !IsolatedTaskCurrent(
                db,
                job.TaskId,
                job.EmployeeId,
                job.TaskGeneration,
                job.EmployeeGeneration,
                depth,
                job.Plan is not null,
                job.Schedule is not null
            )
        )
            return false;
        var task = Need<CompanyTask>(db, job.TaskId);
        if (
            !PlanAuthorityCurrent(db, task, job.Plan, depth)
            || !SandboxEvidenceCurrent(db, task, job, depth)
            || !ScriptScheduleSourceCurrent(db, task, job)
        )
            return false;
        var employee = Need<CompanyEmployee>(db, job.EmployeeId);
        var companyOnly = Need<CompanyThread>(db, task.ThreadId).Scope == "company";
        return job.Documents.All(snapshot =>
            Find<CompanyDocument>(db, snapshot.DocumentId) is { State: "approved" } d
            && d.Version == snapshot.Version
            && d.Digest == snapshot.Digest
            && d.Text == snapshot.Text
            && (
                d.DepartmentId is null && d.EmployeeId is null
                || !companyOnly
                    && (d.DepartmentId == employee.DepartmentId && d.EmployeeId is null || d.EmployeeId == employee.Id)
            )
        );
    }

    private bool SandboxAuthorityCurrent(StoreConnection db, CompanySandboxJob job, bool enforceDeadline = true) =>
        !job.Revoked
        && job.RuntimeIdentity == SandboxRuntimeIdentity
        && (!enforceDeadline || Clock() < job.ExpiresAt)
        && job.ApprovedBy is { } owner
        && Find<CompanyMember>(db, owner) is { State: "active", Role: CompanyRoles.Owner }
        && !ComputePolicy(db).Limits.Paused
        && SandboxTierCurrent(db, job)
        && Need<CompanyTask>(db, job.TaskId).State is not ("completed" or "running")
        && ParentValid(db, Need<CompanyTask>(db, job.TaskId))
        && SandboxTaskCurrent(db, job);

    private object PrepareSandbox(StoreConnection db, CompanyCommand command, bool scheduled = false)
    {
        if (string.IsNullOrWhiteSpace(SandboxRuntimeIdentity) || SandboxExecutionAvailable?.Invoke() == false)
            throw new WorkspaceException(
                "A qualified isolated script runtime must be configured before preparing a sandbox.",
                409
            );
        var task = Need<CompanyTask>(db, command.Id);
        Version(task.Version, command.ExpectedVersion);
        if (task.State == "running")
            throw new WorkspaceException(
                "Finish or resolve the task's current model request before preparing a script.",
                409
            );
        if (task.State == "completed")
            throw new WorkspaceException(
                "This task is finished. Create a new task before preparing another script.",
                409
            );
        var employee = Need<CompanyEmployee>(db, task.EmployeeId);
        var plan = CapturePlanAuthority(db, task);
        var schedule = CaptureScriptSchedule(db, task, scheduled);
        var input = CompanyJson.Decode<CompanySandboxInput>(command.Body ?? "null");
        var name = Text(input.Name, 120).Trim();
        if (
            name.Length == 0
            || input.Documents is null
            || input.Documents.Length > 8
            || input.Documents.Any(d => d is null || string.IsNullOrWhiteSpace(d.DocumentId))
            || input.Documents.Select(d => d.DocumentId).Distinct().Count() != input.Documents.Length
        )
            throw new WorkspaceException("Name the script and choose at most eight distinct approved documents.", 422);
        var selectedEvidence = input.Evidence ?? [];
        var allowedEvidence = SandboxEvidenceIds(db, task).ToHashSet(StringComparer.Ordinal);
        if (
            input.Documents.Length + selectedEvidence.Length > 8
            || selectedEvidence.Any(e => e is null || !allowedEvidence.Contains(e.ArtifactId))
            || selectedEvidence.Select(e => e.ArtifactId).Distinct().Count() != selectedEvidence.Length
        )
            throw new WorkspaceException(
                "Choose up to eight distinct input files from approved documents and this task's approved evidence.",
                422
            );
        var evidence = selectedEvidence
            .Select(selection =>
            {
                var artifact = Need<CompanyArtifact>(db, selection.ArtifactId);
                return new CompanySandboxEvidence(
                    artifact.Id,
                    artifact.Version,
                    artifact.Digest,
                    selection.Name,
                    artifact.Body
                );
            })
            .ToArray();
        var documents = input
            .Documents.Select(selection =>
            {
                var d = Need<CompanyDocument>(db, selection.DocumentId);
                return new CompanySandboxDocument(d.Id, d.Version, d.Digest, selection.Name, d.Text);
            })
            .ToArray();
        var id = Id();
        var request = new SandboxRequest(
            id,
            CompanyId,
            input.Script,
            documents
                .Select(d => new SandboxFile(d.Name, d.Text))
                .Concat(evidence.Select(e => new SandboxFile(e.Name, e.Text)))
                .ToArray(),
            input.Outputs,
            input.Limits,
            input.Tier
        );
        request.Validate();
        var premium = input.Tier is null ? null : PaidScriptEntitlement(db);
        if (
            input.Tier is not null
            && (!ExtendedSandboxAvailable() || premium is null || request.Limits.Seconds > premium.MaximumSeconds)
        )
            throw new WorkspaceException(
                "Extended scripts require a qualified runtime and a current paid plan covering this exact duration. Review Billing and the script tier.",
                409
            );
        var expires = Clock().AddHours(1);
        var digest = CompanyJson.Digest(
            new
            {
                task.Id,
                TaskGeneration = task.Generation,
                Employee = employee.Id,
                EmployeeGeneration = employee.Generation,
                name,
                Runtime = SandboxRuntimeIdentity,
                request,
                documents,
                plan,
                evidence,
                premium,
                schedule,
                expires,
            }
        );
        var job = new CompanySandboxJob(
            id,
            task.Id,
            employee.Id,
            task.Generation,
            employee.Generation,
            name,
            SandboxRuntimeIdentity,
            request,
            documents,
            digest,
            "proposed",
            1,
            Clock(),
            expires,
            Plan: plan,
            Evidence: evidence,
            Premium: premium,
            Schedule: schedule
        );
        if (!SandboxTaskCurrent(db, job))
            throw new WorkspaceException(
                "Use a current task, approved predecessor results and approved inputs in that task's audience.",
                409
            );
        if (All<CompanySandboxJob>(db).Count(j => j.State == "proposed") >= 20)
            throw new WorkspaceException(
                "Review or revoke a pending script before preparing another. At most 20 proposals may wait.",
                429
            );
        SaveSandbox(db, job);
        Event(
            db,
            "sandbox.proposed",
            job.Id,
            job.EmployeeId,
            "An exact script packet is ready for owner review. Nothing has executed."
        );
        return job;
    }

    private object StartSandbox(StoreConnection db, CompanyCommand command, string? scheduledOwner = null)
    {
        DemandWorkAdmission(db);
        if (SandboxExecutionAvailable?.Invoke() == false)
            throw new WorkspaceException(
                "The isolated script runtime is not ready for new work. Saved jobs can still be recovered.",
                409
            );
        var job = Need<CompanySandboxJob>(db, command.Id);
        Version(job.Version, command.ExpectedVersion);
        if (job.State != "proposed" || job.Digest != command.Digest || command.Decision != "approve-once")
            throw new WorkspaceException("Approve this exact script, documents, outputs and limits once.", 409);
        if (HasUnsettledCompute(db, job.TaskId))
            throw new WorkspaceException(
                "Finish or recover this task's current script or browser resources before starting another execution.",
                409
            );
        job = job with { ApprovedBy = scheduledOwner ?? CurrentActor.Value!.Subject };
        if (!SandboxAuthorityCurrent(db, job))
            throw new WorkspaceException(
                "Script authority changed or compute is paused. Review current inputs and limits.",
                409
            );
        var usage = ComputeUsage(db);
        DemandBillingCompute(db, usage, job.Request.Limits.Seconds);
        if (
            usage.Jobs + usage.CarriedJobs >= usage.Policy.Limits.Jobs
            || usage.AllocatedSeconds + usage.HeldSeconds + usage.CarriedSeconds + job.Request.Limits.Seconds
                > usage.Policy.Limits.Seconds
        )
            throw new WorkspaceException("Monthly sandbox allowance is exhausted or held by unresolved work.", 429);
        job = job with
        {
            State = "running",
            Version = job.Version + 1,
            UpdatedAt = Clock(),
            StartedAt = Clock(),
            Period = UsagePeriod,
            PolicyVersion = usage.Policy.Version,
        };
        SaveSandbox(db, job);
        Event(
            db,
            "sandbox.authorized",
            job.Id,
            job.EmployeeId,
            "Owner approved one exact script; its runtime allowance is now held."
        );
        return job;
    }

    private object RevokeSandbox(StoreConnection db, CompanyCommand command)
    {
        var job = Need<CompanySandboxJob>(db, command.Id);
        // A background claim can advance the receipt between display and click. Revocation only removes
        // authority from this immutable job; it must not lose a race with progress updates.
        if (command.ExpectedVersion < 1 || command.ExpectedVersion > job.Version)
            throw new WorkspaceException("Use a saved script review before revoking its authority.", 409);
        if (job.Revoked)
            return job;
        job = job with
        {
            Revoked = true,
            State = job.StartedAt is null ? "revoked" : job.State,
            Version = job.Version + 1,
            UpdatedAt = Clock(),
        };
        SaveSandbox(db, job);
        Event(
            db,
            "sandbox.revoked",
            job.Id,
            job.EmployeeId,
            "Script authority was revoked. Any unresolved runtime allowance stays held until release is confirmed."
        );
        return job;
    }

    // This transaction is the durable dispatch gate. Retrying an HTTP operation or reopening the database cannot
    // obtain a second execution packet. The butler keeps the key, not merely a hopeful sticky note.
    public CompanySandboxJob? TakeSandboxDispatch(string id, string runtimeIdentity)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanySandboxJob>(db, id);
                if (
                    job.State != "running"
                    || job.DispatchTaken
                    || job.RuntimeIdentity != runtimeIdentity
                    || !SandboxAuthorityCurrent(db, job)
                )
                    return null;
                job = job with
                {
                    DispatchTaken = true,
                    ExpiresAt = Clock().AddSeconds(job.Request.Limits.Seconds + 180),
                    Version = job.Version + 1,
                    UpdatedAt = Clock(),
                };
                SaveSandbox(db, job);
                return job;
            }
        );
    }

    public bool SandboxAuthorized(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanySandboxJob>(db, id);
                return job.State == "running" && job.DispatchTaken && SandboxAuthorityCurrent(db, job);
            }
        );
    }

    public CompanySandboxJob FenceSandboxRecovery(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanySandboxJob>(db, id);
                if (job.StartedAt is null || job.SettledAt is not null)
                    return job;
                if (job.State == "running")
                {
                    job = job with { State = "unknown", Version = job.Version + 1, UpdatedAt = Clock() };
                    SaveSandbox(db, job);
                    Event(
                        db,
                        "sandbox.unknown",
                        job.Id,
                        job.EmployeeId,
                        "Script execution was interrupted. Recovery may read a receipt or release its resource; it cannot execute again."
                    );
                }
                return job;
            }
        );
    }

    public CompanySandboxJob RecordSandboxResult(string id, string runtimeIdentity, SandboxResult result)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanySandboxJob>(db, id);
                if (job.StartedAt is null || job.RuntimeIdentity != runtimeIdentity)
                    throw new WorkspaceException("This receipt does not belong to an authorized script runtime.", 409);
                if (job.Result is { State: not "unknown" } || job.SettledAt is not null)
                    return job;
                ValidateSandboxResult(job, result);
                if (!job.DispatchTaken && result.State != "not-sent")
                    throw new WorkspaceException("No dispatch was taken for this script receipt.", 409);
                var digest = CompanyJson.Digest(result);
                if (job.ResultDigest == digest)
                    return job;
                var expose = SandboxAuthorityCurrent(db, job, enforceDeadline: false);
                var state = result.State;
                if (!expose && state is "completed" or "failed" or "timed-out" or "output-limit")
                {
                    state = "withheld";
                    result = result with { Stdout = "", Stderr = "", Outputs = [], Reason = "authority-changed" };
                }
                job = job with
                {
                    Result = result,
                    ResultDigest = digest,
                    State = state,
                    Version = job.Version + 1,
                    UpdatedAt = Clock(),
                };
                SaveSandbox(db, job);
                Audit(db, "sandbox-result", job.Id, new { job.ResultDigest, job.State });
                FinishScheduledSandbox(db, job);
                Event(
                    db,
                    "sandbox.result",
                    job.Id,
                    job.EmployeeId,
                    state == "completed"
                        ? "Script output is saved for review. Resource release is tracked separately."
                        : "Script outcome: " + state + ". Review its receipt and resource release status."
                );
                return job;
            }
        );
    }

    private static void ValidateSandboxResult(CompanySandboxJob job, SandboxResult r)
    {
        if (
            r is null
            || r.State is not ("completed" or "failed" or "timed-out" or "output-limit" or "unknown" or "not-sent")
            || r.Release is null
            || r.Release.Status
                is not ("not-created" or "not-observed" or "awaiting-release" or "awaiting-reconciliation")
            || r.Release.Confirmed != (r.Release.Status == "not-created")
            || r.Release.Confirmed && (r.State != "not-sent" || r.CreationConfirmed)
            || string.IsNullOrWhiteSpace(r.Release.ResourceId)
            || r.Release.ResourceId.Length > 200
            || r.Reason?.Length > 500
            || job.Result is { } previous && previous.Release.ResourceId != r.Release.ResourceId
            || r.Stdout is null
            || r.Stderr is null
            || r.Outputs is null
            || r.WallMilliseconds is < 0
            || Encoding.UTF8.GetByteCount(r.Stdout) + (long)Encoding.UTF8.GetByteCount(r.Stderr)
                > SandboxRequest.OutputBytes
            || r.Outputs.Length > job.Request.Outputs.Length
            || r.Outputs.Any(f =>
                f is null || f.Text is null || !job.Request.Outputs.Contains(f.Name, StringComparer.Ordinal)
            )
            || r.Outputs.Select(f => f.Name).Distinct().Count() != r.Outputs.Length
            || r.Outputs.Sum(f => (long)Encoding.UTF8.GetByteCount(f.Text)) > SandboxRequest.OutputBytes
            || r.State == "completed"
                && (r.ExitCode != 0 || r.Outputs.Length != job.Request.Outputs.Length || !r.CreationConfirmed)
            || r.State is "unknown" or "not-sent"
                && (r.Stdout.Length != 0 || r.Stderr.Length != 0 || r.Outputs.Length != 0)
        )
            throw new WorkspaceException("Script receipt exceeds the authorized output envelope.", 422);
    }

    public CompanySandboxJob RecordSandboxRelease(string id, string runtimeIdentity, SandboxRelease release)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var job = Need<CompanySandboxJob>(db, id);
                if (
                    job.RuntimeIdentity != runtimeIdentity
                    || job.Result is null
                    || release.ResourceId != job.Result.Release.ResourceId
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
                            job.Result.State != "not-sent"
                            || !job.Result.Release.Confirmed
                            || job.Result.CreationConfirmed
                        )
                )
                    throw new WorkspaceException(
                        "Persist a matching script receipt before recording resource release.",
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
                        ? job.Result.State == "not-sent"
                            ? 0
                            : job.Request.Limits.Seconds
                        : null,
                    SettledAt = release.Confirmed ? Clock() : null,
                };
                SaveSandbox(db, job);
                FinishScheduledSandbox(db, job);
                Audit(
                    db,
                    "sandbox-release",
                    job.Id,
                    new
                    {
                        job.Release,
                        job.AllocatedSeconds,
                        job.SettledAt,
                    }
                );
                Event(
                    db,
                    release.Confirmed ? "sandbox.released" : "sandbox.release-attention",
                    job.Id,
                    job.EmployeeId,
                    release.Confirmed
                        ? "Script resources were released; the conservative runtime allocation is recorded. No monetary charge is inferred."
                        : "Script resource release is unconfirmed. Its runtime allowance remains held; no automatic replay is allowed."
                );
                return job;
            }
        );
    }
}
