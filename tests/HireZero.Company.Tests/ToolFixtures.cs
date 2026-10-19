using HireZero.Core;
using HireZero.Integrations.Git;
using HireZero.Roles.Developer;

// The Developer's toolbox as the company profile builds it: git tools, audited by the company store.
public static class ToolFixtures
{
    public static IToolbox Developer(CompanyStore store, GitCheckoutInspector inspector) =>
        new ToolRegistry(store).AddGitTools(inspector).For(new DeveloperModule().Manifest);
}
