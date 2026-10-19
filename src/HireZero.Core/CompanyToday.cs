namespace HireZero.Core;

// One thing waiting on the owner, with what to open to act on it. Key is stable while the item waits, so the owner can
// set its priority or snooze it (ported from the legacy owner tasks, 2B.5j).
public sealed record CompanyNeed(string Kind, string Title, string Detail, string? EmployeeId, string? TaskId)
{
    public string Key => Kind + ":" + (TaskId ?? ContentGuard.Digest(Title)[..16]);
    public string Priority { get; init; } = "normal";
    public int MarkVersion { get; init; }
}

// The owner's priority and snooze for one waiting item.
public sealed record CompanyOwnerMark(string Id, string Priority, string? SnoozedUntil, int Version = 1);

// One entry of the day's work log: a company event as it was recorded.
public sealed record CompanyLogEntry(string Type, string Text, string? Employee, DateTimeOffset At);

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
                var employees = All<CompanyEmployee>(db).ToDictionary(e => e.Id, e => e.Name);
                string Who(string? id) => id is not null && employees.TryGetValue(id, out var name) ? name : "Company";
                var tasks = All<CompanyTask>(db).ToDictionary(t => t.Id);
                string Skill(string taskId) => tasks.TryGetValue(taskId, out var t) ? t.Skill : "work";
                var needs = new List<CompanyNeed>();

                foreach (var k in All<CompanyKnowledge>(db).Where(k => k.State == "draft"))
                    needs.Add(
                        new(
                            "knowledge",
                            "Department knowledge",
                            "A knowledge draft waits for your approval.",
                            null,
                            null
                        )
                    );
                foreach (var p in All<CompanyPlan>(db).Where(p => p.State == "proposed"))
                    needs.Add(
                        new("plan", Clip(p.Objective), "A proposed plan waits for your approval.", p.CeoId, null)
                    );
                foreach (var m in All<CompanyMeeting>(db).Where(m => m.State == "awaiting_owner"))
                    needs.Add(new("meeting", Clip(m.Title), "A meeting outcome waits for your decision.", null, null));
                foreach (var a in All<CompanyArtifact>(db).Where(a => a.State == "needs_review"))
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
                    var t in tasks.Values.Where(t => t.State == "blocked" && t.Reason == "handoff-awaiting-approval")
                )
                    needs.Add(
                        new(
                            "handoff",
                            Who(t.EmployeeId) + ": " + t.Skill,
                            "Assigned from a handoff; approve it to start.",
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
                            Who(t.EmployeeId) + ": " + t.Skill,
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
                            .OrderByDescending(e => e.At)
                            .Take(TodayLogLimit)
                            .Select(e => new CompanyLogEntry(
                                e.Type,
                                e.Text,
                                e.EmployeeId is null ? null : Who(e.EmployeeId),
                                e.At
                            )),
                    ],
                };
            }
        );
    }

    private static string Clip(string text) =>
        text.ReplaceLineEndings(" ").Trim() is var flat && flat.Length > 160 ? flat[..160] + "…" : flat;
}
