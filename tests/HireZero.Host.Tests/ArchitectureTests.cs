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

    // The external types an assembly's code uses, read from its metadata (namespace.name).
    private static HashSet<string> TypeReferences(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return
        [
            .. metadata
                .TypeReferences.Select(handle => metadata.GetTypeReference(handle))
                .Select(type => metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name)),
        ];
    }

    // Roles reach the world through tools (docs/adr/0007-tool-registry.md): no role starts a process.
    [Fact]
    public void RolesStartNoProcesses()
    {
        foreach (var role in Built("HireZero.Roles."))
        {
            var used = TypeReferences(role);
            Assert.False(
                used.Contains("System.Diagnostics.Process") || used.Contains("System.Diagnostics.ProcessStartInfo"),
                role.GetName().Name + " starts a process."
            );
        }
    }

    // Core holds the company and its gates; every process belongs to an integration.
    [Fact]
    public void CoreStartsNoProcesses()
    {
        var used = TypeReferences(typeof(CompanyStore).Assembly);
        Assert.False(
            used.Contains("System.Diagnostics.Process") || used.Contains("System.Diagnostics.ProcessStartInfo"),
            "HireZero.Core starts a process."
        );
    }

    // Roles reach services through their integrations' tools: no role opens an HTTP client.
    [Fact]
    public void RolesOpenNoHttpClients()
    {
        foreach (var role in Built("HireZero.Roles."))
        {
            var used = TypeReferences(role);
            Assert.False(
                used.Contains("System.Net.Http.HttpClient") || used.Contains("System.Net.Http.IHttpClientFactory"),
                role.GetName().Name + " opens an HTTP client."
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

    // Core knows no product an integration serves (docs/adr/0009). Each port to an optional integration adds its names
    // here: Microsoft 365 (2B.5d), session steering (2B.5e), Azure DevOps (2B.5f), Jev (2B.5g); tailnet access follows.
    [Fact]
    public void CoreNamesNoIntegrationProduct()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(Repository, "src", "HireZero.Core"), "*.cs")
            .Where(path => IntegrationProduct().IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName);
        Assert.Empty(offenders);
    }

    [GeneratedRegex(
        @"\b(Microsoft365|Microsoft 365|Outlook|Teams|graph\.microsoft\.com|SessionSteering|ISessionDriver|SteerRequest|AzureDevOps|Azure DevOps|dev\.azure\.com|visualstudio\.com|Jev|TypeSafe|OpenRouter|openrouter\.ai)\b"
    )]
    private static partial Regex IntegrationProduct();

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
