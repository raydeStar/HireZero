namespace HireZero.Core;

// One thing waiting on the owner, with what to open to act on it. Key is stable while the item waits, so the owner can
// set its priority or snooze it (ported from the legacy owner tasks, 2B.5j).
public sealed record CompanyNeed(string Kind, string Title, string Detail, string? EmployeeId, string? TaskId)
{
    public string Key => Kind + ":" + (TaskId ?? ContentGuard.Digest(Title)[..16]);
    public string Priority { get; init; } = "normal";
    public int MarkVersion { get; init; }

    // The conversation to open, when the item lives in one (a Connect card).
    public string? ThreadId { get; init; }
    public string? TargetId { get; init; }
}

// The owner's priority and snooze for one waiting item.
public sealed record CompanyOwnerMark(string Id, string Priority, string? SnoozedUntil, int Version = 1);

// One entry of the day's work log: a company event as it was recorded. Events about one piece of work are one entry,
// its latest step, with the day's earlier steps in Earlier, oldest first (owner walkthrough, 2026-10-19).
public sealed record CompanyLogEntry(string Type, string Text, string? Employee, DateTimeOffset At)
{
    // The work's name, as its employee's module names it, when the event belongs to a task.
    public string? Work { get; init; }
    public CompanyLogStep[] Earlier { get; init; } = [];
}

public sealed record CompanyLogStep(string Text, DateTimeOffset At);

// When a task's first and latest events happened.
public sealed record CompanyTaskTimes(DateTimeOffset Started, DateTimeOffset Updated);

// The company's day (Phase 2B.5i): what waits on the owner now, and what happened in the day, from the store's own
// records and events. No model, no tools and nothing outside the company; role-agnostic.
public sealed record CompanyToday(string Day, CompanyNeed[] NeedsYou, Dictionary<string, int> Happened, int Events)
{
    public CompanyLogEntry[] Log { get; init; } = [];
    public int Snoozed { get; init; }
}

public sealed partial class CompanyStore
{
    public const int TodayNeedsLimit = 50;
    public const int TodayLogLimit = 200;
    private static readonly string[] Priorities = ["high", "normal", "low"];

    // owner.mark: Id is a waiting item's key, Decision its priority, Body an optional yyyy-MM-dd day to snooze it until.
    private CompanyOwnerMark MarkNeed(StoreConnection db, CompanyCommand c)
    {
        var id = Text(c.Id, 120);
        if (!Priorities.Contains(c.Decision))
            throw new WorkspaceException("Choose high, normal or low.", 422);
        var until = (c.Body ?? "").Trim();
        if (
            until.Length > 0
            && !DateOnly.TryParseExact(
                until,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _
            )
        )
            throw new WorkspaceException("Snooze until a yyyy-MM-dd day, or not at all.", 422);
        var old = Find<CompanyOwnerMark>(db, id);
        Version(old?.Version ?? 0, c.ExpectedVersion);
        var mark = new CompanyOwnerMark(id, c.Decision!, until.Length > 0 ? until : null, (old?.Version ?? 0) + 1);
        Save(db, mark, mark.Id, version: mark.Version);
        Event(
            db,
            "owner.marked",
            id,
            null,
            "Owner set a waiting item to "
                + mark.Priority
                + (mark.SnoozedUntil is { } d ? ", snoozed until " + d : "")
                + "."
        );
        return mark;
    }

    public CompanyToday Today(string day, DateTimeOffset from, DateTimeOffset to)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var employees = All<CompanyEmployee>(db).ToDictionary(e => e.Id);
                string Who(string? id) =>
                    id is not null && employees.TryGetValue(id, out var employee) ? employee.Name : "Company";
                var tasks = All<CompanyTask>(db).ToDictionary(t => t.Id);
                // The work's name as the employee's module names it, never its id (owner walkthrough, 2026-10-19).
                string Name(CompanyTask t) =>
                    t.Kind == ContextDraftKind
                        ? (t.ContextScope == CompanyScope ? "Company facts draft" : "Context draft")
                    : employees.TryGetValue(t.EmployeeId, out var e) ? WorkName(e.RoleId, t.Skill)
                    : t.Skill;
                string Skill(string taskId) => tasks.TryGetValue(taskId, out var t) ? Name(t) : "work";
                var needs = new List<CompanyNeed>();
                foreach (
                    var memory in All<CompanyMemoryItem>(db)
                        .Where(m =>
                            m.State is "draft" or "needs_review"
                            || m.State == "approved" && MemoryExclusion(db, m) is not null
                        )
                )
                    needs.Add(
                        new(
                            "memory",
                            "Review memory: " + memory.Definition.Title,
                            memory.State == "draft"
                                ? "Review its statement, evidence, audience and review date before approval."
                                : MemoryExclusion(db, memory)!,
                            memory.Definition.EmployeeId,
                            memory.Id
                        )
                    );
                foreach (var schedule in All<CompanySchedule>(db).Where(s => s.State is "blocked" or "draft"))
                    needs.Add(
                        new(
                            "schedule",
                            "Recurring work: " + schedule.Definition.Name,
                            schedule.State == "draft"
                                ? "Review the schedule, scope and remaining allowance before authorizing."
                                : schedule.Reason
                                    ?? "A recurring occurrence needs your review before the schedule can continue.",
                            null,
                            null
                        )
                    );
                foreach (var profile in All<CompanyBusinessProfile>(db).Where(p => p.State == "approved"))
                {
                    var freshness = BusinessFreshness(db, profile);
                    if (freshness.Blocked || freshness.Fields.Any(f => f.Status is "possible" or "unavailable"))
                        needs.Add(
                            new(
                                "business-freshness",
                                "Review business source changes",
                                freshness.Blocked
                                    ? "Work using the business brief is paused. Resolve conflicting or outdated facts."
                                    : "A source changed or could not be checked. Compare the evidence and review the affected facts.",
                                null,
                                null
                            )
                        );
                }
                var work = WorkIndex(db, tasks.Values);
                string? WorkOf(string id) => work.TryGetValue(id, out var task) ? task : null;

                // A knowledge draft is named by its department and, when one hire waits on it, that hire (real-use
                // batch, 2026-10-20): "Marketing: approve Chip's context", never a generic "Department knowledge".
                var departments = All<CompanyDepartment>(db).ToDictionary(d => d.Id);
                foreach (var k in All<CompanyKnowledge>(db).Where(k => k.State == "draft"))
                {
                    var waiting = employees
                        .Values.Where(e => e.State == "onboarding" && e.DepartmentId == k.DepartmentId)
                        .ToArray();
                    var place =
                        k.DepartmentId is { } id && departments.TryGetValue(id, out var department)
                            ? department.Name
                            : "Company";
                    needs.Add(
                        new(
                            "knowledge",
                            place
                                + ": approve "
                                + (
                                    waiting.Length == 1 ? waiting[0].Name + "'s context"
                                    : k.DepartmentId is null ? "company facts"
                                    : "department context"
                                ),
                            waiting.Length == 1
                                ? "A context draft waits for your approval to finish this hire."
                                : "A knowledge draft waits for your approval.",
                            waiting.Length == 1 ? waiting[0].Id : null,
                            null
                        )
                    );
                }
                // An unfinished hire waits on the owner too (UX round 6), unless its department's draft context, listed
                // above, already says so.
                var drafted = All<CompanyKnowledge>(db)
                    .Where(k => k.State == "draft")
                    .Select(k => k.DepartmentId)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (
                    var e in employees.Values.Where(e => e.State == "onboarding" && !drafted.Contains(e.DepartmentId))
                )
                    needs.Add(
                        new(
                            "onboarding",
                            e.Name + ": finish onboarding",
                            "Add the shared department context to finish this hire.",
                            e.Id,
                            null
                        )
                    );
                // An open Connect card waits on the owner wherever they are (docs/adr/0046-connect-cards.md).
                foreach (var r in All<CompanyConnectionRequest>(db).Where(r => r.State == "open"))
                    needs.Add(
                        new CompanyNeed(
                            "connection",
                            "Connect "
                                + r.Name
                                + (
                                    r.EmployeeId is not null ? " · asked by " + Who(r.EmployeeId)
                                    : r.From is not null ? " · asked by " + r.From
                                    : ""
                                ),
                            r.Reason,
                            r.EmployeeId,
                            null
                        )
                        {
                            ThreadId = r.ThreadId,
                        }
                    );
                foreach (var p in All<CompanyPlan>(db).Where(p => p.State == "proposed"))
                    needs.Add(
                        new CompanyNeed(
                            "plan",
                            Clip(p.Objective),
                            "A proposed plan waits for your approval.",
                            p.CeoId,
                            null
                        )
                        {
                            TargetId = p.Id,
                        }
                    );
                foreach (var m in All<CompanyMeeting>(db).Where(m => m.State is "outcome_proposed" or "awaiting_owner"))
                    needs.Add(
                        new CompanyNeed(
                            "meeting",
                            Clip(m.Title),
                            m.State == "outcome_proposed"
                                ? "The discussion is complete. Review the turns and propose minutes."
                                : "Meeting minutes wait for your approval.",
                            null,
                            null
                        )
                        {
                            TargetId = m.Id,
                        }
                    );
                // Preparation claims the task, but starts no model request. The owner still holds the starting bell.
                foreach (var a in All<CompanyAttempt>(db).Where(a => a.Measured is { State: "proposed" }))
                {
                    if (!tasks.TryGetValue(a.TaskId, out var task) || task.State != "running")
                        continue;
                    needs.Add(
                        new CompanyNeed(
                            "authorization",
                            Who(task.EmployeeId)
                                + ": "
                                + (task.Kind == "conversation" ? "authorize a reply" : "authorize " + Name(task)),
                            Clock() >= a.Measured!.ExpiresAt
                                ? "This preparation expired without running. Open it to cancel the unused proposal."
                                : "Review the prepared context, then authorize it to start. Nothing has run yet.",
                            task.EmployeeId,
                            task.Id
                        )
                        {
                            ThreadId = task.ThreadId,
                        }
                    );
                }
                foreach (
                    var a in All<CompanyArtifact>(db)
                        .Where(a =>
                            a.State == "needs_review"
                            && (!tasks.TryGetValue(a.TaskId, out var task) || task.MeetingId is null)
                        )
                )
                    needs.Add(
                        new(
                            "review",
                            Who(a.EmployeeId) + ": " + Skill(a.TaskId),
                            a.Provenance.StartsWith("action:", StringComparison.Ordinal)
                                ? "Gathered evidence waits for your review."
                                : "A result waits for your review.",
                            a.EmployeeId,
                            a.TaskId
                        )
                    );
                foreach (
                    var t in tasks.Values.Where(t =>
                        t.State == "blocked" && t.Reason is "handoff-awaiting-approval" or "revision-awaiting-approval"
                    )
                )
                    needs.Add(
                        new(
                            t.Revision is null ? "handoff" : "revision",
                            Who(t.EmployeeId) + ": " + Name(t),
                            t.Revision is null
                                ? "Assigned from a handoff; approve it to start."
                                : "Your requested changes are saved. Review the revision before authorizing a new run.",
                            t.EmployeeId,
                            t.Id
                        )
                    );
                foreach (var g in All<CompanyExecutionGrant>(db).Where(g => g.State is "proposed" or "unknown"))
                    needs.Add(
                        new(
                            "execution",
                            Who(g.EmployeeId) + ": " + Skill(g.TaskId),
                            g.State == "proposed"
                                ? "A run in an approved checkout waits for your approval."
                                : "A run's outcome is unknown; review the checkout before anything else.",
                            g.EmployeeId,
                            g.TaskId
                        )
                    );
                foreach (
                    var p in All<CompanyPublication>(db).Where(p => p.State is "proposed" or "approved" or "unknown")
                )
                    needs.Add(
                        new(
                            "publication",
                            Clip(p.Summary),
                            p.State switch
                            {
                                "proposed" => "Approve or decline this publication.",
                                "approved" => "Approved and not published yet.",
                                _ => "Its outcome is unknown; reconcile it by reading, it is never retried.",
                            },
                            p.EmployeeId,
                            p.TaskId
                        )
                    );
                foreach (var t in tasks.Values.Where(t => t.State == "unknown"))
                    needs.Add(
                        new(
                            "unknown",
                            Who(t.EmployeeId) + ": " + Name(t),
                            "Its outcome is unknown; it is never replayed.",
                            t.EmployeeId,
                            t.Id
                        )
                    );

                var events = Rows(
                        db,
                        "SELECT payload FROM co_events WHERE company_id=$0 ORDER BY seq",
                        r => CompanyJson.Decode<CompanyEvent>(r.GetString(0)),
                        CompanyId
                    )
                    .Where(e => e.At >= from && e.At < to)
                    .ToArray();
                var happened = events
                    .GroupBy(e => e.Type, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                // The owner's marks: snoozed items wait out of sight until their day; the rest sort by priority.
                var marks = All<CompanyOwnerMark>(db).ToDictionary(m => m.Id);
                var shown = needs
                    .Select(n =>
                        marks.TryGetValue(n.Key, out var m)
                            ? (Need: n with { Priority = m.Priority, MarkVersion = m.Version }, Until: m.SnoozedUntil)
                            : (Need: n, Until: (string?)null)
                    )
                    .ToArray();
                var visible = shown
                    .Where(x => x.Until is null || string.CompareOrdinal(x.Until, day) <= 0)
                    .Select(x => x.Need)
                    .OrderBy(n => Array.IndexOf(Priorities, n.Priority))
                    .Take(TodayNeedsLimit)
                    .ToArray();
                return new CompanyToday(day, visible, happened, events.Length)
                {
                    Snoozed =
                        shown.Length - shown.Count(x => x.Until is null || string.CompareOrdinal(x.Until, day) <= 0),
                    Log =
                    [
                        .. events
                            .Select((e, i) => (Event: e, Work: WorkOf(e.Id), Index: i))
                            .GroupBy(x => x.Work ?? "event:" + x.Index, StringComparer.Ordinal)
                            .Select(g =>
                            {
                                var steps = g.OrderBy(x => x.Event.At).ThenBy(x => x.Index).ToArray();
                                var latest = steps[^1];
                                return new CompanyLogEntry(
                                    latest.Event.Type,
                                    latest.Event.Text,
                                    latest.Event.EmployeeId is null ? null : Who(latest.Event.EmployeeId),
                                    latest.Event.At
                                )
                                {
                                    Work = latest.Work is { } id && tasks.TryGetValue(id, out var t) ? Name(t) : null,
                                    Earlier =
                                    [
                                        .. steps[..^1].Select(x => new CompanyLogStep(x.Event.Text, x.Event.At)),
                                    ],
                                };
                            })
                            .OrderByDescending(e => e.At)
                            .Take(TodayLogLimit),
                    ],
                };
            }
        );
    }

    // The task an event is about, by event id: the task itself, or the artifact, publication, run or reply it produced.
    private Dictionary<string, string> WorkIndex(StoreConnection db, IEnumerable<CompanyTask> tasks)
    {
        var work = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in tasks)
            work[t.Id] = t.Id;
        foreach (var a in All<CompanyArtifact>(db))
            work[a.Id] = a.TaskId;
        foreach (var p in All<CompanyPublication>(db))
            work[p.Id] = p.TaskId;
        foreach (var g in All<CompanyExecutionGrant>(db))
            work[g.Id] = g.TaskId;
        foreach (var attempt in All<CompanyAttempt>(db))
            work[attempt.Id] = attempt.TaskId;
        return work;
    }

    // When each task's first and latest events happened (UX round 8): the work board and cockpit show how long ago work
    // started or changed, from the event log, since tasks carry no times of their own.
    private Dictionary<string, CompanyTaskTimes> TaskTimes(StoreConnection db)
    {
        var work = WorkIndex(db, All<CompanyTask>(db));
        var times = new Dictionary<string, CompanyTaskTimes>(StringComparer.Ordinal);
        foreach (
            var e in Rows(
                db,
                "SELECT payload FROM co_events WHERE company_id=$0 ORDER BY seq",
                r => CompanyJson.Decode<CompanyEvent>(r.GetString(0)),
                CompanyId
            )
        )
            if (work.TryGetValue(e.Id, out var task))
                times[task] = times.TryGetValue(task, out var known) ? known with { Updated = e.At } : new(e.At, e.At);
        return times;
    }

    private static string Clip(string text) =>
        text.ReplaceLineEndings(" ").Trim() is var flat && flat.Length > 160 ? flat[..160] + "…" : flat;

    private string WorkName(string roleId, string skill) =>
        _roles.FirstOrDefault(r => r.Id == roleId)?.WorkName(skill) ?? skill;
}
