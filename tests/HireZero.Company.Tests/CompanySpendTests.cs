using HireZero.Core;

// Model spend in US dollars (owner walkthrough, 2026-10-19): priced jobs are summed per employee from recorded usage;
// a job on a model with no price is counted, never guessed; a job that recorded no usage is not a job that spent.
public sealed class CompanySpendTests
{
    private static CompanyAttempt Job(string employee, string route, long input, long output) =>
        new(Guid.NewGuid().ToString("N"), "task", employee, "measured", 1, 1, null)
        {
            Usage = new(input, output),
            Measured = CompanyJson.Decode<CompanyMeasuredJob>("{\"route\":\"" + route + "\"}"),
        };

    [Fact]
    public void SpendIsPricedUsagePerEmployeeAndUnpricedJobsAreOnlyCounted()
    {
        var priced = new ModelPrice(3m, 15m);
        var spend = CompanyStore.Spend(
            [
                Job("writer", "anthropic/fictional-model", 1_000_000, 100_000),
                Job("writer", "anthropic/fictional-model", 200_000, 0),
                Job("coder", "codex/fictional-runtime", 5_000, 5_000),
                new("unused", "task", "coder", "measured", 1, 1, null),
            ],
            route => route.StartsWith("anthropic/", StringComparison.Ordinal) ? priced : null
        );
        Assert.Equal((5.10m, 2, 1), (spend.Usd, spend.PricedJobs, spend.UnpricedJobs));
        Assert.Equal(
            [("writer", 5.10m, 2, 0), ("coder", 0m, 0, 1)],
            spend.Employees.Select(l => (l.EmployeeId, l.Usd, l.PricedJobs, l.UnpricedJobs))
        );
        Assert.Equal(1.5m, priced.Cost(new(0, 100_000)));
    }
}
