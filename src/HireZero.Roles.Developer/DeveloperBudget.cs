namespace HireZero.Roles.Developer;

// The developer's name in the budget ledger. Fix rounds and coding starts ask the ledger first; a run already going is never stopped.
public static class DeveloperBudget
{
    public const string Role = "developer";

    public static string? Blocked(BudgetLedger? budgets, string requestId) => budgets?.Blocked(Role, requestId);

    public static string Stop(string why) =>
        "Budget reached: " + why + ". Raise the limit or resume in the cockpit's Budget panel, then approve again.";
}
