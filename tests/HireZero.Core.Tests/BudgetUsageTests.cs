using HireZero.Core;
using Microsoft.Data.Sqlite;

// Usage a provider did not report is never recorded as zero without saying so (docs/adr/0008-model-routes.md).
public sealed class BudgetUsageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("budget-usage-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    private static CodingRun Run(string id, long? tokens)
    {
        var start = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        CodingEvent[] events = tokens is { } used
            ? [new(BudgetLedger.CodingUsage, "Used.", "{\"tokens\":" + used + "}", start.ToString("O"))]
            : [];
        return new(
            id,
            "completed",
            "C:\\work",
            "sol",
            "high",
            "sol",
            null,
            null,
            events,
            start.ToString("O"),
            start.AddMinutes(3).ToString("O")
        );
    }

    [Fact]
    public void ModelCallsWithoutUsageCountTheirEstimateAndAreFlagged()
    {
        var ledger = new BudgetLedger(Path.Combine(_root, "budget.sqlite"));
        UsageMeter.Ledger = ledger;
        try
        {
            using (UsageMeter.For("writer", "task-1"))
            {
                UsageMeter.Model("openai/test", 1200, 2, estimate: 9999);
                UsageMeter.Model("openai/test", null, 2, estimate: 1500);
            }
        }
        finally
        {
            UsageMeter.Ledger = null;
        }
        Assert.Equal(new BudgetUse(2700, 0, 0, 2, 1), ledger.TaskTotal("task-1"));
    }

    [Fact]
    public void CodingRunsWithoutUsageAreFlaggedAndStillCountTheirRun()
    {
        var ledger = new BudgetLedger(Path.Combine(_root, "budget.sqlite"));
        ledger.RecordCoding(Run("reported", 5000), "developer", "task-2");
        ledger.RecordCoding(Run("silent", null), "developer", "task-2");
        Assert.Equal(new BudgetUse(5000, 2, 6, 0, 1), ledger.TaskTotal("task-2"));
    }

    // A ledger created before the unreported column existed gains it in place, keeping its rows.
    [Fact]
    public void ExistingLedgersAreMigratedInPlace()
    {
        var path = Path.Combine(_root, "old.sqlite");
        using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            db.Open();
            using var create = db.CreateCommand();
            create.CommandText = """
                CREATE TABLE budget_usage(id TEXT PRIMARY KEY, at TEXT NOT NULL, month TEXT NOT NULL, role TEXT NOT NULL, task TEXT,
                  kind TEXT NOT NULL, model TEXT, tokens INTEGER NOT NULL, runs INTEGER NOT NULL, seconds INTEGER NOT NULL);
                INSERT INTO budget_usage VALUES('model:old','2026-10-01T00:00:00Z','2026-10','writer','task-3','model','m',400,0,1);
                """;
            create.ExecuteNonQuery();
        }
        var ledger = new BudgetLedger(path);
        Assert.Equal(new BudgetUse(400, 0, 0, 1, 0), ledger.TaskTotal("task-3"));
        ledger.Record("model:new", "writer", "task-3", "model", "m", 100, 0, 1, unreported: true);
        Assert.Equal(new BudgetUse(500, 0, 0, 2, 1), ledger.TaskTotal("task-3"));
    }
}
