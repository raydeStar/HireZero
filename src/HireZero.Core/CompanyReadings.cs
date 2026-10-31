using System.Globalization;

namespace HireZero.Core;

// One week's numbers on an employee's scorecard, recorded by a member (docs/adr/0170-goal-ladder-and-weekly-numbers.md).
// Core bounds and keeps them; what each number means belongs to the role, whose department context names them.
public sealed record CompanyMeasure(string Name, decimal Value);

public sealed record CompanyReading(
    string Id,
    string EmployeeId,
    string Week,
    CompanyMeasure[] Values,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    int Version = 1
);

public sealed partial class CompanyStore
{
    public const int ReadingMeasures = 8;
    public const int ReadingWeeksShown = 26;

    // IF NOT EXISTS keeps older-schema test databases, which drop only the tables they know, able to step forward.
    private const string ReadingSchema =
        "CREATE TABLE IF NOT EXISTS co_readings(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));";

    // reading.record: Name is the week's Monday (yyyy-MM-dd) and Body a JSON array of {name, value}. A week holds one
    // reading per employee; recording it again replaces its numbers as the next version.
    private object RecordReading(StoreConnection db, CompanyCommand c)
    {
        var employee = Need<CompanyEmployee>(db, Text(c.EmployeeId, 64));
        if (employee.State != "active")
            throw new WorkspaceException("Record numbers for an active employee.", 409);
        if (
            !DateOnly.TryParseExact(
                c.Name,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var week
            )
            || week.DayOfWeek != DayOfWeek.Monday
        )
            throw new WorkspaceException("Name the week by its Monday.", 422);
        var today = DateOnly.FromDateTime(Clock().UtcDateTime);
        // A week may be recorded once it has started (a day's grace for time zones ahead of UTC), back two years.
        if (week > today.AddDays(1) || week < today.AddYears(-2))
            throw new WorkspaceException("Record a week that has started, within the last two years.", 422);
        CompanyMeasure[] values;
        try
        {
            values = CompanyJson.Decode<CompanyMeasure[]>(c.Body ?? "null") ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            throw new WorkspaceException("Send the week's numbers as a list of names and values.", 422);
        }
        if (values.Any(v => v is null))
            throw new WorkspaceException("Send the week's numbers as a list of names and values.", 422);
        values = [.. values.Select(v => v with { Name = Text(v.Name, 60).Trim() })];
        if (values.Length is 0 or > ReadingMeasures)
            throw new WorkspaceException($"Record between 1 and {ReadingMeasures} numbers for a week.", 422);
        if (values.Any(v => v.Name.Length == 0))
            throw new WorkspaceException("Name each number.", 422);
        if (values.Select(v => v.Name.ToLowerInvariant()).Distinct().Count() != values.Length)
            throw new WorkspaceException("Name each number once.", 422);
        if (values.Any(v => v.Value is < 0 or > 1_000_000_000_000m || decimal.Round(v.Value, 2) != v.Value))
            throw new WorkspaceException("Use numbers from 0 up to a trillion, with at most two decimals.", 422);
        var prior = All<CompanyReading>(db)
            .FirstOrDefault(r => r.EmployeeId == employee.Id && r.Week == week.ToString("yyyy-MM-dd"));
        Version(prior?.Version ?? 0, c.ExpectedVersion);
        var reading = new CompanyReading(
            prior?.Id ?? Id(),
            employee.Id,
            week.ToString("yyyy-MM-dd"),
            values,
            CurrentActor.Value?.Subject ?? CompanyActor.System,
            Clock(),
            (prior?.Version ?? 0) + 1
        );
        Save(db, reading, reading.Id, employee: employee.Id, state: "recorded", version: reading.Version);
        Event(
            db,
            "reading.recorded",
            reading.Id,
            employee.Id,
            $"{employee.Name}'s numbers for the week of {reading.Week} {(prior is null ? "recorded" : "updated")}"
        );
        return reading;
    }

    public const int ShiftWeeks = 8;
    public const int ShiftRecentWork = 12;

    // A shift round's numbers: the employee's latest weeks, oldest first, or none when nothing is recorded.
    private CompanyReading[]? ShiftScorecard(StoreConnection db, string employeeId)
    {
        var weeks = All<CompanyReading>(db)
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.Week, StringComparer.Ordinal)
            .TakeLast(ShiftWeeks)
            .ToArray();
        return weeks.Length == 0 ? null : weeks;
    }

    // Where the employee's recent requests stand, newest first. Conversations, context drafts and the current request
    // are left out; so are result bodies, which the owner reviews on their own.
    private CompanyRecentWork[] RecentWork(StoreConnection db, string employeeId, string currentTask)
    {
        var times = TaskTimes(db);
        var artifacts = All<CompanyArtifact>(db);
        var threads = All<CompanyThread>(db).ToDictionary(t => t.Id);
        var role = Role(Need<CompanyEmployee>(db, employeeId).RoleId);
        return
        [
            .. All<CompanyTask>(db)
                .Where(t =>
                    t.EmployeeId == employeeId
                    && t.Id != currentTask
                    && t.Kind is not ("conversation" or ContextDraftKind)
                    && t.State is not "superseded"
                )
                .Reverse()
                .Take(ShiftRecentWork)
                .Select(t =>
                {
                    var latest = artifacts.LastOrDefault(a =>
                        a.TaskId == t.Id && a.State is not ("superseded" or "revoked")
                    );
                    var status = t.State switch
                    {
                        "completed" => "done",
                        "canceled" => "canceled",
                        "failed" => "failed",
                        _ when latest?.State == "approved" => "approved, not yet marked done",
                        _ when latest?.State == "needs_review" => "draft waiting for the owner's review",
                        _ => "in progress",
                    };
                    var title = threads.TryGetValue(t.ThreadId, out var thread) ? thread.Title : "";
                    return new CompanyRecentWork(
                        Text(string.IsNullOrWhiteSpace(title) ? t.Input : title, 120),
                        role.WorkName(t.Skill),
                        status,
                        times.TryGetValue(t.Id, out var time) ? time.Updated.ToString("yyyy-MM-dd") : ""
                    );
                }),
        ];
    }

    public const int ShiftLessonCount = 8;

    // Current, owner-approved decisions and feedback addressed to this employee, newest first, or none
    // (docs/adr/0172-shift-rhythm-lessons-and-measured-changes.md). Keyword retrieval may miss them; a round should not.
    private CompanyLesson[]? ShiftLessons(StoreConnection db, string employeeId)
    {
        var lessons = All<CompanyMemoryItem>(db)
            .Where(m =>
                m.State == "approved"
                && m.Definition.EmployeeId == employeeId
                && m.Definition.Kind is "feedback" or "decision"
                && MemoryExclusion(db, m) is null
            )
            .OrderByDescending(m => m.ApprovedAt)
            .Take(ShiftLessonCount)
            .Select(m => new CompanyLesson(
                m.Definition.Title,
                m.Definition.Statement,
                m.Definition.Kind,
                m.ApprovedAt?.ToString("yyyy-MM-dd") ?? ""
            ))
            .ToArray();
        return lessons.Length == 0 ? null : lessons;
    }

    // The latest weeks of each employee's numbers, oldest first.
    private CompanyReading[] RecentReadings(StoreConnection db) =>
        [
            .. All<CompanyReading>(db)
                .GroupBy(r => r.EmployeeId)
                .SelectMany(g => g.OrderBy(r => r.Week, StringComparer.Ordinal).TakeLast(ReadingWeeksShown)),
        ];
}
