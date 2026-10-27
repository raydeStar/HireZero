namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private string ModelRouteFor(CompanyRole role, string skill, string kind) =>
        ModelRouting.For(
            role.ExecutionSkills.Contains(skill),
            kind == "work"
                && role.Manifest?.Skills.SingleOrDefault(s => s.Id == skill)?.ModelProfile
                    == CompanyModelRouting.AnalysisProfile
        );

    private string ModelRouteFor(StoreConnection db, CompanyTask task) =>
        task.RequestedModelRoute
        ?? ModelRouteFor(Role(Need<CompanyEmployee>(db, task.EmployeeId).RoleId), task.Skill, task.Kind);

    private string? ReviewModelChoice(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;
        if (!ModelRouting.AllowsText(route))
            throw new WorkspaceException(
                "The selected text model is unavailable. Choose a current connection and prepare a fresh review.",
                409
            );
        return route;
    }
}
