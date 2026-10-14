using HireZero.Core;
using HireZero.Integrations.OpenClaw;
using HireZero.Roles.Developer;
using HireZero.Roles.Marketing;

public static class EmployeeRegistration
{
    public static IRole[] AvailableRoles() => [new DeveloperRole(), new MarketingRole()];
    public static EmployeeDefinition[] Definitions() =>
    [
        new(new DeveloperRole().Info, "You are the Developer employee, working only in the supplied isolated context.",
            [new EmployeeChatSkill("You are the Developer employee. Discuss coding work using only this conversation and explicitly supplied facts. No tools or execution are available in this worker.")]),
        new MarketingRole().Employee
    ];
    public static void AddEmployees(this IServiceCollection services, string database, RoleCatalog catalog, IEmployeeModelGateway? fixture = null)
    {
        services.AddSingleton(new EmployeeStore(database));
        services.AddSingleton<SerializedEmployeeGateway>(_ => new(fixture ?? new UnqualifiedEmployeeGateway()));
        services.AddSingleton(s => new EmployeeTeam(Definitions(), s.GetRequiredService<EmployeeStore>(), catalog, s.GetRequiredService<SerializedEmployeeGateway>()));
        // The two worker instances share storage and the serialized adapter, never their contexts.
        foreach (var definition in Definitions())
            services.AddSingleton<IHostedService>(s => s.GetRequiredService<EmployeeTeam>().Workers.Single(w => w.EmployeeId == definition.Info.Id));
    }
}
