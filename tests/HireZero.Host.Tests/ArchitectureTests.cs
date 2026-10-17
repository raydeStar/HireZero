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
        Assert.Empty(References(core, "HireZero.Tools."));
    }

    // Other HireZero assemblies an assembly references, besides Core.
    private static string[] Platform(Assembly assembly) =>
        [.. References(assembly, "HireZero.").Where(name => name != "HireZero.Core")];

    // Tool contract assemblies hold descriptors and records only, so they may depend on Core alone.
    [Fact]
    public void ToolContractsReferenceOnlyCore()
    {
        var contracts = Built("HireZero.Tools.");
        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
            Assert.True(Platform(contract).Length == 0, contract.GetName().Name + " references more than Core.");
    }

    // A tool a manifest declares must exist as a contract with the same risk.
    [Fact]
    public void DeclaredToolsMatchTheirContracts()
    {
        var contracts = Built("HireZero.Tools.")
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Select(f => f.GetValue(null))
            .OfType<IToolContract>()
            .ToDictionary(c => c.Id);
        foreach (var module in EmployeeModuleCatalog.Discover())
        foreach (var tool in module.Manifest.Tools)
        {
            Assert.True(
                contracts.TryGetValue(tool.Id, out var contract),
                module.Manifest.Id + " declares unknown tool " + tool.Id
            );
            Assert.Equal(contract.Risk, tool.Risk);
        }
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
    public void RolesReferenceOnlyCoreAndToolContracts()
    {
        var roles = Built("HireZero.Roles.");
        Assert.NotEmpty(roles);
        foreach (var role in roles)
            Assert.True(
                Platform(role).All(name => name.StartsWith("HireZero.Tools.", StringComparison.Ordinal)),
                role.GetName().Name + " references a role or integration: " + string.Join(", ", Platform(role))
            );
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
