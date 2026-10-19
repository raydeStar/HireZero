using HireZero.Core;

// The employee modules this build ships, discovered once from the application directory (docs/adr/0006).
public static class HostEmployees
{
    private static readonly Lazy<IEmployeeModule[]> Discovered = new(() => EmployeeModuleCatalog.Discover());

    public static IEmployeeModule[] Modules => Discovered.Value;
}
