namespace HireZero.Core;

public static class CompanyTokenLimits
{
    public const int Input = 4000,
        Output = 1000,
        Total = Input + Output;
}

// Server ceilings on the company, department and employee budgets, which are also their defaults until the owner sets
// lower ones. Raised (2026-10-17) so one default fix run of 200,000 tokens fits an employee's budget.
public static class CompanyBudgetCeilings
{
    public const long Company = 1_000_000,
        Department = 500_000,
        Employee = 250_000;
}
