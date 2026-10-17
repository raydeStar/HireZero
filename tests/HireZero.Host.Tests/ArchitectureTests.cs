using System.Reflection;
using System.Text.RegularExpressions;
using HireZero.Core;

// The layering rules of docs/adr/0001-three-layers.md and the module rules of docs/adr/0002 and 0006, checked
// against the built assemblies and the source tree.
public sealed partial class ArchitectureTests
{
    private static readonly string Repository = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../..")
    );

    private static Assembly[] Built(string prefix) =>
        [
            .. Directory
                .EnumerateFiles(AppContext.BaseDirectory, prefix + "*.dll")
                .Where(path => !path.EndsWith(".Tests.dll", StringComparison.Ordinal))
                .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))),
        ];

    private static string[] References(Assembly assembly, string prefix) =>
        [
            .. assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? "")
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)),
        ];

    [Fact]
    public void CoreReferencesNoRoleOrIntegration()
    {
        var core = typeof(CompanyStore).Assembly;
        Assert.Empty(References(core, "HireZero.Roles."));
        Assert.Empty(References(core, "HireZero.Integrations."));
    }

    [Fact]
    public void IntegrationsReferenceNoRole()
    {
        var integrations = Built("HireZero.Integrations.");
        Assert.NotEmpty(integrations);
        foreach (var integration in integrations)
            Assert.True(
                References(integration, "HireZero.Roles.").Length == 0,
                integration.GetName().Name + " references a role."
            );
    }

    [Fact]
    public void RolesReferenceOnlyCore()
    {
        var roles = Built("HireZero.Roles.");
        Assert.NotEmpty(roles);
        foreach (var role in roles)
        {
            var name = role.GetName().Name;
            Assert.True(References(role, "HireZero.Roles.").Length == 0, name + " references another role.");
            Assert.True(
                References(role, "HireZero.Integrations.").Length == 0,
                name + " references an integration directly."
            );
        }
    }

    [Fact]
    public void EveryRoleProjectShipsOneDiscoveredModule()
    {
        var projects = Directory
            .EnumerateDirectories(Path.Combine(Repository, "src"), "HireZero.Roles.*")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var modules = EmployeeModuleCatalog.Discover();
        Assert.Equal(projects.Length, modules.Length);
        Assert.Equal(
            projects,
            modules.Select(m => m.GetType().Assembly.GetName().Name).Order(StringComparer.Ordinal).ToArray()
        );
    }

    // Only the legacy Developer and Recovery profiles may name a role until they retire (docs/ROADMAP.md).
    [Fact]
    public void CompanyProfileNamesNoRole()
    {
        string[] legacy = ["DeveloperWorkspace.cs", "RecoveryWorkspace.cs", "ResetQueuedWork.cs"];
        var offenders = Directory
            .EnumerateFiles(Path.Combine(Repository, "src", "HireZero.Host"), "*.cs")
            .Where(path => !legacy.Contains(Path.GetFileName(path)))
            .Where(path => File.ReadAllText(path).Contains("HireZero.Roles", StringComparison.Ordinal))
            .Select(Path.GetFileName);
        Assert.Empty(offenders);
    }

    // Shared company UI reaches employee UI only through the slot registry in web/src/roles/company.ts.
    [Fact]
    public void CompanyWebUiImportsNoRoleFolder()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(Repository, "web", "src", "company"), "*.tsx")
            .Concat(Directory.EnumerateFiles(Path.Combine(Repository, "web", "src", "company"), "*.ts"))
            .Where(path => RoleFolderImport().IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName);
        Assert.Empty(offenders);
    }

    [GeneratedRegex("from \"\\.\\./roles/(?!company\")")]
    private static partial Regex RoleFolderImport();
}
