namespace HireZero.Core;

// One thing waiting on the owner, with what to open to act on it.
public sealed record CompanyNeed(string Kind, string Title, string Detail, string? EmployeeId, string? TaskId);

// The company's day (Phase 2B.5i): what waits on the owner now, and what happened in the day, from the store's own
// records and events. No model, no tools and nothing outside the company; role-agnostic.
public sealed record CompanyToday(string Day, CompanyNeed[] NeedsYou, Dictionary<string, int> Happened, int Events);

public sealed partial class CompanyStore
{
    public const int TodayNeedsLimit = 50;

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
                return new CompanyToday(day, [.. needs.Take(TodayNeedsLimit)], happened, events.Length);
            }
        );
    }

    private static string Clip(string text) =>
        text.ReplaceLineEndings(" ").Trim() is var flat && flat.Length > 160 ? flat[..160] + "…" : flat;
}
