namespace HireZero.Core;

// What the company has spent on models, in US dollars, from the usage each authorized job recorded and the reviewed
// price table (docs/adr/0036-api-model-provider.md). A job whose model has no price (Codex, or a route nobody priced)
// is counted, not guessed (owner walkthrough, 2026-10-19: budgets showed tokens only).
public sealed record CompanySpend(decimal Usd, int PricedJobs, int UnpricedJobs, CompanySpendLine[] Employees);

public sealed record CompanySpendLine(string EmployeeId, decimal Usd, int PricedJobs, int UnpricedJobs);

public sealed partial class CompanyStore
{
    public static CompanySpend Spend(IEnumerable<CompanyAttempt> attempts, Func<string, ModelPrice?> price)
    {
        var jobs = attempts
            .Where(a => a.Measured is not null && a.Usage is not null)
            .Select(a => (a.EmployeeId, Usd: price(a.Measured!.Route)?.Cost(a.Usage!)))
            .ToArray();
        return new(
            jobs.Sum(j => j.Usd ?? 0),
            jobs.Count(j => j.Usd is not null),
            jobs.Count(j => j.Usd is null),
            [
                .. jobs.GroupBy(j => j.EmployeeId, StringComparer.Ordinal)
                    .Select(g => new CompanySpendLine(
                        g.Key,
                        g.Sum(j => j.Usd ?? 0),
                        g.Count(j => j.Usd is not null),
                        g.Count(j => j.Usd is null)
                    ))
                    .OrderByDescending(l => l.Usd)
                    .ThenBy(l => l.EmployeeId, StringComparer.Ordinal),
            ]
        );
    }

    // What each budget is for, in words: the company, a department, an employee, or the one plan, meeting or piece of
    // work an allowance was opened for (owner walkthrough, 2026-10-19: rows read only "Plan allowance").
    private Dictionary<string, string> BudgetLabels(StoreConnection db)
    {
        var employees = All<CompanyEmployee>(db).ToDictionary(e => e.Id);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["company-total"] = "Company" };
        foreach (var d in All<CompanyDepartment>(db))
            labels["department:" + d.Id] = d.Name;
        foreach (var e in employees.Values)
            labels["employee:" + e.Id] = e.Name;
        foreach (var t in All<CompanyTask>(db).Where(t => t.Id == t.RootId))
            labels[t.RootId] = employees.TryGetValue(t.EmployeeId, out var e)
                ? e.Name + ": " + WorkName(e.RoleId, t.Skill)
                : WorkName("", t.Skill);
        foreach (var p in All<CompanyPlan>(db))
            labels[p.RootId] = "Plan: " + Clip(p.Objective);
        foreach (var m in All<CompanyMeeting>(db))
            labels[m.RootId] = "Meeting: " + Clip(m.Title);
        return labels;
    }
}
