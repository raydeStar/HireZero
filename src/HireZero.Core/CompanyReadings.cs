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

    // The latest weeks of each employee's numbers, oldest first.
    private CompanyReading[] RecentReadings(StoreConnection db) =>
        [
            .. All<CompanyReading>(db)
                .GroupBy(r => r.EmployeeId)
                .SelectMany(g => g.OrderBy(r => r.Week, StringComparer.Ordinal).TakeLast(ReadingWeeksShown)),
        ];
}
