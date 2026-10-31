using System.Globalization;

namespace HireZero.Core;

public sealed record CompanyScheduleInput(
    string Name,
    string Kind,
    string Cadence,
    string TimeZone,
    string StartDate,
    string LocalTime,
    int Occurrences,
    string? EmployeeId = null,
    string? Prompt = null,
    decimal CreditAllowance = 0,
    string? ProfileId = null,
    string[]? SourceIds = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? ScriptJobId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? Skill = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string[]? Evidence = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? BrowserJobId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? ModelRoute = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    )]
        bool IncludeOccurrenceTime = false,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? ReadContextPolicy = null
);

public sealed record CompanyScheduledOccurrence(
    DateTimeOffset ScheduledFor,
    string TimeZone,
    string Cadence,
    int RunNumber,
    int TotalRuns
);

public sealed record CompanyScheduleReviewContext(
    string CompanyFacts,
    string DepartmentFacts,
    string Notes,
    string Responsibility,
    string Instructions,
    string SourceUrls,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? SelectedResults = null
);

public sealed record CompanyScheduleAuthority(
    string ContextDigest,
    string? RuntimeIdentity,
    string? Route,
    CompanyMeasuredPolicy? Policy,
    CompanyScheduleReviewContext Context,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyScheduledScript? Script = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyScheduledBrowser? Browser = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyModelTerms? Terms = null
);

public sealed record CompanySchedule(
    string Id,
    CompanyScheduleInput Definition,
    CompanyScheduleAuthority Authority,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    int Consumed = 0,
    DateTimeOffset? NextRunAt = null,
    string? ApprovedBy = null,
    string? ThreadId = null,
    string? Reason = null
)
{
    // A read-time projection, never a new approval or a change to the saved schedule.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public bool? ReviewCurrent { get; init; }
}

public sealed record CompanyScheduleRun(
    string Id,
    string ScheduleId,
    string ScheduleDigest,
    int Slot,
    DateTimeOffset DueAt,
    DateTimeOffset StartedAt,
    string State,
    int Missed = 0,
    string? TaskId = null,
    string? AttemptId = null,
    string? RefreshId = null,
    string? Reason = null,
    DateTimeOffset? CompletedAt = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? SandboxId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? BrowserId = null
);

public sealed record CompanyScheduleDetail(
    CompanySchedule Schedule,
    CompanyScheduleRun[] Runs,
    DateTimeOffset[] Upcoming
);

public static class CompanyScheduleCalendar
{
    public static string CanonicalZone(string id)
    {
        var zone = Zone(id.Trim());
        return zone.Id == "UTC" ? "UTC"
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana
            : zone.Id;
    }

    public static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new WorkspaceException("Choose a recognized time zone, such as America/Denver or UTC.", 422);
        }
    }

    // The index-th Monday-to-Friday date on or after the start (docs/adr/0172-shift-rhythm-lessons-and-measured-changes.md).
    private static DateOnly Weekday(DateOnly start, int index)
    {
        var day = start;
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            day = day.AddDays(1);
        for (var counted = 0; counted < index; )
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                counted++;
        }
        return day;
    }

    public static DateTimeOffset Slot(CompanyScheduleInput input, int index)
    {
        if (
            input.Cadence is not ("daily" or "weekly" or "weekdays")
            || !DateOnly.TryParseExact(
                input.StartDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
            || !TimeOnly.TryParseExact(
                input.LocalTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time
            )
        )
            throw new WorkspaceException(
                "Choose a daily, weekday or weekly cadence, a start date and a local time.",
                422
            );
        if (index is < 0 or > 31)
            throw new WorkspaceException("Schedule occurrence is outside its bounded range.", 422);
        var zone = Zone(input.TimeZone);
        var day = input.Cadence switch
        {
            "weekly" => date.AddDays(index * 7),
            "weekdays" => Weekday(date, index),
            _ => date.AddDays(index),
        };
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        // Spring-forward gaps move to the first valid minute; fall-back repeats get one, earlier UTC occurrence.
        for (var minute = 0; zone.IsInvalidTime(local) && minute < 180; minute++)
            local = local.AddMinutes(1);
        if (zone.IsInvalidTime(local))
            throw new WorkspaceException("This time zone has an unsupported clock transition.", 422);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}

public sealed partial class CompanyStore
{
    private const string ScheduleSchema =
        "CREATE TABLE co_schedules(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));"
        + "CREATE TABLE co_schedule_runs(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),UNIQUE(company_id,parent_id,revision),FOREIGN KEY(company_id,parent_id) REFERENCES co_schedules(company_id,id));";

    public CompanySchedule[] Schedules()
    {
        using var db = Open();
        return Transaction(db, () => All<CompanySchedule>(db).Select(s => ScheduleReviewStatus(db, s)).ToArray());
    }

    public CompanyScheduleDetail ScheduleDetail(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var schedule = Need<CompanySchedule>(db, id);
                return new CompanyScheduleDetail(
                    ScheduleReviewStatus(db, schedule),
                    ScheduleRuns(db, id).OrderByDescending(r => r.Slot).Take(31).ToArray(),
                    Enumerable
                        .Range(schedule.Consumed, Math.Min(3, schedule.Definition.Occurrences - schedule.Consumed))
                        .Select(i => CompanyScheduleCalendar.Slot(schedule.Definition, i))
                        .ToArray()
                );
            }
        );
    }

    private CompanyScheduleRun[] ScheduleRuns(StoreConnection db, string id) =>
        Rows(
            db,
            "SELECT payload FROM co_schedule_runs WHERE company_id=$0 AND parent_id=$1 ORDER BY revision",
            r => CompanyJson.Decode<CompanyScheduleRun>(r.GetString(0)),
            CompanyId,
            id
        );

    private CompanySchedule ScheduleReviewStatus(StoreConnection db, CompanySchedule schedule) =>
        schedule with
        {
            ReviewCurrent =
                schedule.State is "completed" or "canceled" ? null
                : schedule.State == "active" ? ScheduleAuthorityCurrent(db, schedule)
                : ScheduleScopeCurrent(db, schedule),
        };

    private void SaveSchedule(StoreConnection db, CompanySchedule s) =>
        Save(db, s, s.Id, employee: s.Definition.EmployeeId, state: s.State, version: s.Version);

    private void SaveScheduleRun(StoreConnection db, CompanyScheduleRun r) =>
        Save(db, r, r.Id, r.ScheduleId, state: r.State, version: r.Slot);

    private static string ScheduleSkill(CompanyRole role, CompanyScheduleInput input)
    {
        var skill = input.Skill ?? PlatformSkills.Chat;
        if (!role.Skills.Contains(skill) || skill != PlatformSkills.Chat && !role.MeasuredSkills.Contains(skill))
            throw new WorkspaceException(
                "Choose an internal measured skill offered by this employee. Execution and actions need separate approval.",
                422
            );
        return skill;
    }

    private CompanyScheduleAuthority ScheduleAuthority(
        StoreConnection db,
        CompanyScheduleInput input,
        bool existing = false,
        CompanyMeasuredPolicy? approved = null
    )
    {
        if (input.ReadContextPolicy is not null && input.Kind != "employee-task")
            throw new WorkspaceException("Automatic context reads belong to an approved employee shift.", 422);
        if (input.Kind == "source-refresh")
        {
            var profile = Need<CompanyBusinessProfile>(db, input.ProfileId);
            if (profile.State != "approved")
                throw new WorkspaceException("Choose the current approved brief for scheduled source checks.", 409);
            ConfirmedBusiness(db, profile.BusinessVersion);
            var sources = input.SourceIds ?? [];
            var cited = profile.Fields.SelectMany(f => f.Citations).Select(c => c.SourceId).ToHashSet();
            if (
                sources.Length is < 1 or > 3
                || sources.Distinct().Count() != sources.Length
                || sources.Any(id => !cited.Contains(id))
            )
                throw new WorkspaceException("Choose one to three sources cited by this approved brief.", 422);
            if (Need<CompanyBusinessIdentity>(db, BusinessIdentityId).Website is null)
                throw new WorkspaceException("Confirm a business website before scheduling its source checks.", 409);
            return new(
                CompanyJson.Digest(
                    new
                    {
                        profile.Id,
                        profile.Version,
                        profile.Digest,
                        profile.BusinessVersion,
                        sources,
                    }
                ),
                null,
                null,
                null,
                new(
                    string.Join("\n", profile.Fields.Select(f => f.Key + ": " + f.Value + " [" + f.Verification + "]")),
                    "",
                    "",
                    "",
                    "Read only the selected cited pages; compare evidence without overwriting approved facts.",
                    string.Join("\n", sources.Select(id => Need<CompanyBusinessSource>(db, id).Url))
                )
            );
        }
        if (input.Kind is not ("employee-task" or "script-task" or "browser-task"))
            throw new WorkspaceException(
                "Choose a private employee deliverable, saved script, website review or source refresh.",
                422
            );
        var employee = Need<CompanyEmployee>(db, input.EmployeeId);
        var role = Role(employee.RoleId);
        var skill = ScheduleSkill(role, input);
        if (
            input.ReadContextPolicy is not null
            && (
                input.Kind != "employee-task"
                || role.Manifest?.Skills.SingleOrDefault(s => s.Id == skill)?.ReadContextPolicy
                    != input.ReadContextPolicy
            )
        )
            throw new WorkspaceException("This employee does not support the reviewed automatic read plan.", 422);
        var kind = skill == PlatformSkills.Chat ? "conversation" : "work";
        if (employee.State != "active")
            throw new WorkspaceException("Choose an active employee for this recurring deliverable.", 409);
        if (input.Kind == "employee-task" && MeasuredRuntimeIdentity is null)
            throw new WorkspaceException("A pinned measured model is required for recurring employee work.", 409);
        var department = Need<CompanyDepartment>(db, employee.DepartmentId);
        var knowledge = Need<CompanyKnowledge>(db, department.Knowledge);
        if (knowledge.State != "approved")
            throw new WorkspaceException("Approve the employee's department context before scheduling work.", 409);
        var companyKnowledge = All<CompanyKnowledge>(db)
            .LastOrDefault(k => k.DepartmentId is null && k.State == "approved");
        if (companyKnowledge is null)
            throw new WorkspaceException("Approve company facts before scheduling employee work.", 409);
        BusinessProvenance(db, companyKnowledge.Id); // Includes explicit owner freshness pauses.
        var evidence = input.Evidence ?? [];
        Evidence(db, evidence);
        var selectedResults = evidence.Select(id => Need<CompanyArtifact>(db, id)).ToArray();
        var policy = CompanyMeasuredPolicy.Kept(
            CompanyMeasuredPolicy.For(kind, role.Manifest?.Skills.SingleOrDefault(s => s.Id == skill)?.MeasuredProfile),
            approved
        );
        var evidenceText =
            selectedResults.Length == 0
                ? null
                : string.Join("\n\n", selectedResults.Select(a => a.Id + " · revision " + a.Version + "\n" + a.Body));
        if (
            evidenceText is not null
            && System.Text.Encoding.UTF8.GetByteCount(evidenceText) > policy.ApplicationBytes / 2
        )
            throw new WorkspaceException("Choose fewer or shorter approved results for this recurring request.", 422);
        var fingerprint = CompanyJson.Digest(
            new
            {
                Employee = employee.Id,
                employee.Generation,
                employee.RoleId,
                employee.Responsibility,
                Department = department.Id,
                Knowledge = knowledge.Id,
                knowledge.Digest,
                CompanyKnowledge = companyKnowledge.Id,
                CompanyDigest = companyKnowledge.Digest,
                Notes = Find<CompanyNotes>(db, employee.Id),
                Instructions = role.InstructionsForSkill?.Invoke(skill) ?? role.Instructions,
                RetrievalScope = "approved-documents-and-memory-v1",
            }
        );
        if (input.ReadContextPolicy is not null)
            fingerprint = CompanyJson.Digest(
                new { context = fingerprint, readContextPolicy = input.ReadContextPolicy }
            );
        if (selectedResults.Length > 0)
            fingerprint = CompanyJson.Digest(
                new
                {
                    context = fingerprint,
                    evidence = selectedResults
                        .Select(a => new
                        {
                            a.Id,
                            a.Version,
                            a.Digest,
                        })
                        .ToArray(),
                }
            );
        return new(
            fingerprint,
            input.Kind == "script-task" ? SandboxRuntimeIdentity
                : input.Kind == "browser-task" ? BrowserRuntimeIdentity
                : MeasuredRuntimeIdentity,
            input.Kind != "employee-task"
                ? null
                : ReviewModelChoice(input.ModelRoute) ?? ModelRouteFor(role, skill, kind),
            input.Kind != "employee-task" ? null : policy,
            new(
                companyKnowledge.Body,
                knowledge.Body,
                Find<CompanyNotes>(db, employee.Id)?.Body ?? "",
                employee.Responsibility,
                role.InstructionsForSkill?.Invoke(skill) ?? role.Instructions,
                "",
                evidenceText
            ),
            input.Kind == "script-task" ? ScheduledScriptTemplate(db, input, existing) : null,
            input.Kind == "browser-task" ? ScheduledBrowserTemplate(db, input, existing) : null,
            input.Kind == "employee-task"
                ? MeasuredTerms(ReviewModelChoice(input.ModelRoute) ?? ModelRouteFor(role, skill, kind))
                : null
        );
    }

    private bool ScheduleAuthorityCurrent(StoreConnection db, CompanySchedule schedule)
    {
        if (
            schedule.ApprovedBy is null
            || Find<CompanyMember>(db, schedule.ApprovedBy) is not { State: "active", Role: CompanyRoles.Owner }
        )
            return false;
        return ScheduleScopeCurrent(db, schedule);
    }

    private bool ScheduleScopeCurrent(StoreConnection db, CompanySchedule schedule)
    {
        try
        {
            return CompanyJson.Digest(
                    ScheduleAuthority(db, schedule.Definition, existing: true, schedule.Authority.Policy)
                ) == CompanyJson.Digest(schedule.Authority);
        }
        catch (WorkspaceException)
        {
            return false;
        }
    }

    private object SaveScheduleDraft(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyScheduleInput>(command.Body ?? "null");
        var previous = command.Id is null ? null : Need<CompanySchedule>(db, command.Id);
        Version(previous?.Version ?? 0, command.ExpectedVersion);
        if (previous is not null && previous.State != "draft")
            throw new WorkspaceException("Create a new schedule draft to change approved instructions or limits.", 409);
        if (input.Occurrences is < 1 or > 31)
            throw new WorkspaceException("Authorize between one and 31 occurrences.", 422);
        var first = CompanyScheduleCalendar.Slot(input, 0);
        if (first < Clock().AddMinutes(1) || first > Clock().AddDays(90))
            throw new WorkspaceException(
                "Choose a first run at least one minute away and within the next 90 days.",
                422
            );
        input = input with
        {
            Name = Text(input.Name, 120).Trim(),
            TimeZone = CompanyScheduleCalendar.CanonicalZone(Text(input.TimeZone, 100)),
            Prompt = input.Kind == "employee-task" ? Text(input.Prompt, 2000).Trim() : null,
            EmployeeId = input.Kind is "employee-task" or "script-task" or "browser-task" ? input.EmployeeId : null,
            ProfileId = input.Kind == "source-refresh" ? input.ProfileId : null,
            SourceIds = input.Kind == "source-refresh" ? input.SourceIds : null,
            CreditAllowance = input.Kind == "employee-task" ? input.CreditAllowance : 0,
            ScriptJobId = input.Kind == "script-task" ? input.ScriptJobId : null,
            BrowserJobId = input.Kind == "browser-task" ? input.BrowserJobId : null,
            Skill = input.Kind == "employee-task" && input.Skill != PlatformSkills.Chat ? input.Skill : null,
            Evidence = input.Kind == "employee-task" && input.Evidence is { Length: > 0 } ? input.Evidence : null,
            ModelRoute = input.Kind == "employee-task" ? ReviewModelChoice(input.ModelRoute) : null,
            IncludeOccurrenceTime = input.Kind == "employee-task" && input.IncludeOccurrenceTime,
        };
        if (
            input.Name.Length == 0
            || input.Kind == "employee-task"
                && (string.IsNullOrEmpty(input.Prompt) || input.CreditAllowance is <= 0 or > 1000)
        )
            throw new WorkspaceException(
                "Name the schedule and provide its instructions and a positive per-run credit allowance, at most 1,000.",
                422
            );
        if (previous is null && All<CompanySchedule>(db).Length >= 100)
            throw new WorkspaceException("This company has reached its retained schedule limit of 100.", 429);
        if (input.Kind == "employee-task")
        {
            var role = Role(Need<CompanyEmployee>(db, input.EmployeeId).RoleId);
            var skill = ScheduleSkill(role, input);
            input = input with
            {
                Prompt = Text(role.ValidateInput?.Invoke(skill, input.Prompt!) ?? input.Prompt, 2000).Trim(),
            };
        }
        var authority = ScheduleAuthority(db, input);
        var schedule = new CompanySchedule(
            previous?.Id ?? Id(),
            input,
            authority,
            CompanyJson.Digest(new { input, authority }),
            "draft",
            (previous?.Version ?? 0) + 1,
            Clock(),
            NextRunAt: first
        );
        SaveSchedule(db, schedule);
        Event(
            db,
            "schedule.drafted",
            schedule.Id,
            input.EmployeeId,
            "Recurring work is ready for exact owner review; nothing is scheduled yet."
        );
        return schedule;
    }

    private object ReviewSchedule(StoreConnection db, CompanyCommand command)
    {
        var schedule = Need<CompanySchedule>(db, command.Id);
        Version(schedule.Version, command.ExpectedVersion);
        if (schedule.Digest != command.Digest)
            throw new WorkspaceException("Review the exact schedule and its remaining allowance.", 409);
        if (command.Decision is "pause" or "cancel")
        {
            if (schedule.State is "completed" or "canceled")
                throw new WorkspaceException("This schedule has already ended.", 409);
            schedule = schedule with
            {
                State = command.Decision == "pause" ? "paused" : "canceled",
                Reason = command.Decision == "pause" ? "Owner paused this schedule." : "Owner ended this schedule.",
            };
            SaveSchedule(db, schedule);
            foreach (var run in ScheduleRuns(db, schedule.Id).Where(r => r.State is "claimed" or "running"))
            {
                if (
                    run.TaskId is { } taskId
                    && Need<CompanyTask>(db, taskId) is { } task
                    && task.State is not ("completed" or "canceled" or "superseded")
                )
                    Stop(db, task, "canceled", "Owner stopped the recurring schedule.");
                SaveScheduleRun(
                    db,
                    run with
                    {
                        State = "stopped",
                        Reason = "Owner stopped the schedule. In-flight usage may still settle.",
                        CompletedAt = Clock(),
                    }
                );
            }
        }
        else
        {
            var expected =
                schedule.Definition.Kind == "employee-task" ? "authorize-recurring-soft-limits"
                : schedule.Definition.Kind == "script-task" ? "authorize-recurring-scripts"
                : schedule.Definition.Kind == "browser-task" ? "authorize-recurring-browsers"
                : "authorize-recurring-source-reads";
            if (
                command.Decision != expected
                || schedule.State is not ("draft" or "paused" or "blocked")
                || schedule.Consumed >= schedule.Definition.Occurrences
            )
                throw new WorkspaceException(
                    "Approve or resume the exact remaining occurrences and acknowledge their limits.",
                    409
                );
            if (
                CompanyJson.Digest(
                    ScheduleAuthority(db, schedule.Definition, existing: true, schedule.Authority.Policy)
                ) != CompanyJson.Digest(schedule.Authority)
            )
                throw new WorkspaceException(
                    "The employee, approved context or runtime changed. Create and review a new schedule.",
                    409
                );
            if (
                ScheduleRuns(db, schedule.Id)
                    .Any(r =>
                        r.SandboxId is { } sandboxId && Find<CompanySandboxJob>(db, sandboxId) is { SettledAt: null }
                        || r.BrowserId is { } browserId && Find<CompanyBrowserJob>(db, browserId) is { SettledAt: null }
                        || r.AttemptId is { } id
                            && Find<CompanyAttempt>(db, id) is { } a
                            && (ProviderHeld(a) || a.Phase is "unknown" or "claimed" or "reserved" or "dispatching")
                    )
            )
                throw new WorkspaceException(
                    "Resolve the earlier provider outcome before resuming this schedule.",
                    409
                );
            if (All<CompanySchedule>(db).Count(s => s.Id != schedule.Id && s.State == "active") >= 10)
                throw new WorkspaceException("Keep at most ten schedules active in this company.", 429);
            var thread = schedule.ThreadId;
            if (schedule.Definition.Kind is "employee-task" or "script-task" or "browser-task" && thread is null)
            {
                thread = Id();
                Save(
                    db,
                    new CompanyThread(
                        thread,
                        schedule.Definition.EmployeeId!,
                        "Recurring: " + schedule.Definition.Name
                    ),
                    thread,
                    employee: schedule.Definition.EmployeeId
                );
                if (schedule.Definition.Kind == "employee-task")
                {
                    var budgetId = "schedule:" + schedule.Id;
                    Save(
                        db,
                        new CompanyBudget(
                            budgetId,
                            (long)schedule.Definition.Occurrences * schedule.Authority.Policy!.PlanningTokens
                        ),
                        budgetId
                    );
                }
            }
            schedule = schedule with
            {
                State = "active",
                Reason = null,
                ApprovedBy = CurrentActor.Value?.Subject ?? _owner,
                ThreadId = thread,
            };
        }
        schedule = schedule with { Version = schedule.Version + 1, UpdatedAt = Clock() };
        SaveSchedule(db, schedule);
        Event(
            db,
            "schedule." + schedule.State,
            schedule.Id,
            schedule.Definition.EmployeeId,
            "Recurring schedule " + schedule.State + ": " + schedule.Definition.Name
        );
        return schedule;
    }

    public CompanyScheduleRun? ClaimDueSchedule()
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                if (UsagePolicy(db).WorkPaused)
                    return null;
                var schedule = All<CompanySchedule>(db)
                    .Where(s =>
                        s.State == "active"
                        && s.NextRunAt <= Clock()
                        && !ScheduleRuns(db, s.Id).Any(r => r.State is "claimed" or "running")
                    )
                    .OrderBy(s => s.NextRunAt)
                    .FirstOrDefault();
                if (schedule is null)
                    return null;
                if (!ScheduleAuthorityCurrent(db, schedule))
                {
                    SaveSchedule(
                        db,
                        schedule with
                        {
                            State = "blocked",
                            Reason =
                                "Owner authority, employee, approved context or runtime changed. Review a new schedule.",
                            Version = schedule.Version + 1,
                            UpdatedAt = Clock(),
                        }
                    );
                    Event(
                        db,
                        "schedule.blocked",
                        schedule.Id,
                        schedule.Definition.EmployeeId,
                        "Recurring work paused because its approved authority changed."
                    );
                    return null;
                }
                if (ScheduleRuns(db, schedule.Id).Any(r => r.State is "claimed" or "running"))
                    return null;
                var slot = schedule.Consumed;
                while (
                    slot + 1 < schedule.Definition.Occurrences
                    && CompanyScheduleCalendar.Slot(schedule.Definition, slot + 1) <= Clock()
                )
                    slot++;
                var due = CompanyScheduleCalendar.Slot(schedule.Definition, slot);
                var expired = Clock() - due > TimeSpan.FromHours(1);
                var run = new CompanyScheduleRun(
                    Guid.NewGuid().ToString("D"),
                    schedule.Id,
                    schedule.Digest,
                    slot,
                    due,
                    Clock(),
                    expired ? "missed" : "claimed",
                    slot - schedule.Consumed + (expired ? 1 : 0),
                    Reason: expired
                        ? "The workspace missed this slot by more than an hour; no catch-up work was started."
                        : null,
                    CompletedAt: expired ? Clock() : null
                );
                SaveScheduleRun(db, run);
                var consumed = slot + 1;
                schedule = schedule with
                {
                    Consumed = consumed,
                    NextRunAt =
                        consumed < schedule.Definition.Occurrences
                            ? CompanyScheduleCalendar.Slot(schedule.Definition, consumed)
                            : null,
                    State = expired && consumed == schedule.Definition.Occurrences ? "completed" : "active",
                    Version = schedule.Version + 1,
                    UpdatedAt = Clock(),
                };
                SaveSchedule(db, schedule);
                Event(
                    db,
                    expired ? "schedule.missed" : "schedule.due",
                    run.Id,
                    schedule.Definition.EmployeeId,
                    expired
                        ? "Missed recurring slots were recorded without catch-up execution."
                        : "One approved recurring occurrence was claimed."
                );
                return run;
            }
        );
    }

    private bool ScheduleRunCurrent(StoreConnection db, CompanyTask task)
    {
        if (task.ScheduleRunId is null)
            return true;
        var run = Find<CompanyScheduleRun>(db, task.ScheduleRunId);
        var schedule = run is null ? null : Find<CompanySchedule>(db, run.ScheduleId);
        return run is { State: "claimed" or "running" }
            && schedule is { State: "active" }
            && run.ScheduleDigest == schedule.Digest
            && ScheduleAuthorityCurrent(db, schedule);
    }

    private bool SourceScheduleCurrent(StoreConnection db, string runId)
    {
        var run = Find<CompanyScheduleRun>(db, runId);
        var schedule = run is null ? null : Find<CompanySchedule>(db, run.ScheduleId);
        return run is { State: "running" }
            && schedule is { State: "active" }
            && schedule.Definition.Kind == "source-refresh"
            && run.ScheduleDigest == schedule.Digest
            && ScheduleAuthorityCurrent(db, schedule);
    }

    private CompanyScheduledOccurrence? ScheduledOccurrence(StoreConnection db, CompanyTask task)
    {
        if (task.ScheduleRunId is null)
            return null;
        var run = Need<CompanyScheduleRun>(db, task.ScheduleRunId);
        var definition = Need<CompanySchedule>(db, run.ScheduleId).Definition;
        if (!definition.IncludeOccurrenceTime || definition.Kind != "employee-task")
            return null;
        // The calendar gets a seat at the table; it must never pretend to be fresh business evidence.
        return new(
            TimeZoneInfo.ConvertTime(run.DueAt, CompanyScheduleCalendar.Zone(definition.TimeZone)),
            definition.TimeZone,
            definition.Cadence,
            run.Slot + 1,
            definition.Occurrences
        );
    }

    public CompanyAttempt StartScheduledTask(string runId, CompanyFreshSignals? signals = null)
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
                    || schedule.State != "active"
                    || schedule.Definition.Kind != "employee-task"
                    || schedule.Digest != run.ScheduleDigest
                    || !ScheduleAuthorityCurrent(db, schedule)
                )
                    throw new WorkspaceException("This scheduled occurrence no longer has current authority.", 409);
                if (
                    signals?.Policy != schedule.Definition.ReadContextPolicy
                    || signals is not null && signals.Summary.Length > 6000
                )
                    throw new WorkspaceException("Collect only the exact read plan approved for this occurrence.", 409);
                var task = NewTask(
                    db,
                    schedule.Definition.EmployeeId!,
                    schedule.ThreadId!,
                    schedule.Definition.Skill ?? PlatformSkills.Chat,
                    schedule.Definition.Prompt!,
                    schedule.Definition.Evidence ?? [],
                    [],
                    null,
                    null,
                    null,
                    schedule.Definition.Skill is null or PlatformSkills.Chat ? "conversation" : "work"
                ) with
                {
                    ScheduleRunId = run.Id,
                    RequestedModelRoute = schedule.Definition.ModelRoute,
                    Watermark = 0,
                };
                Put(db, task);
                var attempt = PrepareMeasured(
                    db,
                    new(
                        run.Id,
                        "measured.prepare",
                        task.Id,
                        task.Version,
                        CreditAllowance: schedule.Definition.CreditAllowance
                    ),
                    signals
                );
                attempt = StartMeasured(
                    db,
                    new(
                        run.Id,
                        "measured.run",
                        attempt.Id,
                        attempt.Version,
                        Digest: attempt.Measured!.Digest,
                        Decision: "accept-soft-limits"
                    )
                );
                SaveScheduleRun(db, run with { State = "running", TaskId = task.Id, AttemptId = attempt.Id });
                Event(
                    db,
                    "schedule.dispatched",
                    run.Id,
                    task.EmployeeId,
                    "Exact recurring approval consumed one private model request. No external action was authorized."
                );
                return attempt;
            }
        );
    }

    public (CompanyCommand Command, string Subject) ScheduledSourceCommand(string runId)
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
                    || schedule.State != "active"
                    || schedule.Definition.Kind != "source-refresh"
                    || !ScheduleAuthorityCurrent(db, schedule)
                )
                    throw new WorkspaceException("This source-check occurrence no longer has current authority.", 409);
                var profile = Need<CompanyBusinessProfile>(db, schedule.Definition.ProfileId);
                SaveScheduleRun(db, run with { State = "running" });
                return (
                    new CompanyCommand(
                        run.Id,
                        "business.refresh.start",
                        profile.Id,
                        profile.Version,
                        Digest: profile.Digest,
                        Decision: "refresh-approved-sources",
                        Evidence: schedule.Definition.SourceIds,
                        Scope: "scheduled-source"
                    ),
                    schedule.ApprovedBy!
                );
            }
        );
    }

    public void FinishScheduleRun(string runId, string? refreshId = null, string? error = null)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var run = Need<CompanyScheduleRun>(db, runId);
                if (run.State is not ("claimed" or "running"))
                    return true;
                var schedule = Need<CompanySchedule>(db, run.ScheduleId);
                var attempt = run.AttemptId is null ? null : Need<CompanyAttempt>(db, run.AttemptId);
                var refresh = refreshId is null ? null : Need<CompanySourceRefresh>(db, refreshId);
                var success = error is null && (attempt?.Phase == "committed" || refresh?.State == "completed");
                var unknown =
                    attempt is { Phase: "unknown" }
                    || attempt is not null && ProviderHeld(attempt)
                    || refresh?.State == "unknown";
                var reason = success
                    ? null
                    : Text(
                        error
                            ?? attempt?.Error
                            ?? (
                                refresh is null
                                    ? "The occurrence did not finish. Review its saved status before resuming."
                                    : "Source refresh " + refresh.State + "; review its evidence before resuming."
                            ),
                        500
                    );
                run = run with
                {
                    State =
                        success ? "completed"
                        : unknown ? "unknown"
                        : "failed",
                    RefreshId = refreshId,
                    Reason = reason,
                    CompletedAt = Clock(),
                };
                SaveScheduleRun(db, run);
                if (schedule.State == "active")
                {
                    schedule = schedule with
                    {
                        State =
                            !success ? "blocked"
                            : schedule.NextRunAt is null ? "completed"
                            : "active",
                        Reason = reason,
                        Version = schedule.Version + 1,
                        UpdatedAt = Clock(),
                    };
                    SaveSchedule(db, schedule);
                }
                Event(
                    db,
                    "schedule.result",
                    run.Id,
                    schedule.Definition.EmployeeId,
                    "Recurring work " + run.State + ": " + schedule.Definition.Name
                );
                return true;
            }
        );
    }

    private void RecoverSchedules()
    {
        using var db = Open();
        foreach (var run in All<CompanyScheduleRun>(db).Where(r => r.State is "claimed" or "running"))
        {
            if (run.SandboxId is { } sandboxId)
            {
                // The original engine's host service owns recovery. Never turn a saved dispatch into a new run.
                RecoverScheduledScript(sandboxId);
                continue;
            }
            if (run.BrowserId is { } browserId)
            {
                RecoverScheduledBrowser(browserId);
                continue;
            }
            string? refreshId = null;
            var receipt = Operation(run.Id);
            if (receipt is not null)
                refreshId = CompanyJson.Decode<CompanySourceRefresh>(receipt).Id;
            // Only recorded terminal results can close an interrupted occurrence. Never dispatch it again.
            FinishScheduleRun(
                run.Id,
                refreshId,
                run.AttemptId is null && refreshId is null
                    ? "Host interrupted before a complete result was recorded. This occurrence was not replayed."
                    : null
            );
        }
    }
}
