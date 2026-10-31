using System.Reflection;
using System.Text.RegularExpressions;

namespace HireZero.Core;

// The employee module contract (docs/adr/0002-employee-module-contract.md). Each kind of employee is one
// IEmployeeModule in its own HireZero.Roles.* assembly, named by an [assembly: EmployeeModule(...)] attribute and
// discovered at startup (docs/adr/0006-compiled-in-module-discovery.md).
public interface IEmployeeModule
{
    EmployeeManifest Manifest { get; }

    // Validates structured skill input, and action input (a JSON object of the action's fields), before it is saved,
    // and returns its normalized form. Throw WorkspaceException to refuse it.
    string ValidateInput(string skill, string input) => input;

    // The system instructions for one skill; the manifest's base instructions unless a skill needs its own.
    string InstructionsFor(string skill) => Manifest.Instructions;

    // Only an explicitly reviewed schedule may request this versioned, bounded read plan.
    Task<string> GatherContextAsync(string skill, IToolbox tools, DateOnly today, CancellationToken token) =>
        throw new WorkspaceException("This employee has no automatic read plan.", 422);

    // Runs a declared action with a read-only toolbox and returns its output (at most 12,000 characters), which the
    // owner reviews as an artifact before any skill may use it as evidence.
    Task<string> RunActionAsync(
        string action,
        IReadOnlyDictionary<string, string> input,
        IToolbox tools,
        CancellationToken token
    ) => throw new WorkspaceException("This employee has no such action.", 404);

    // The current choices for an action field that loads them (ActionField.LoadsChoices), read with a read-only
    // toolbox when the owner opens the form (docs/adr/0037-loaded-action-choices.md). They help the owner pick; the
    // action still checks the value it is given.
    Task<ActionChoice[]> ActionChoicesAsync(string action, string field, IToolbox tools, CancellationToken token) =>
        throw new WorkspaceException("This field has no choices to load.", 404);

    // One step of reviewed execution or publication (docs/adr/0012-company-publication.md). The id is the task for
    // Prepare, the execution grant for Run, the grant or (for a skill that publishes an approved artifact) the artifact
    // for Propose, and the publication for Publish and Reconcile. Each step is single use in the store, so a retried
    // request is refused rather than repeated.
    Task<object> ExecuteAsync(
        ExecutionStep step,
        string id,
        CompanyExecutionServices services,
        CancellationToken token
    ) => throw new WorkspaceException("This employee has no reviewed execution.", 404);
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class EmployeeModuleAttribute(Type moduleType) : Attribute
{
    public Type ModuleType { get; } = moduleType;
}

public sealed record EmployeeManifest(
    string Id,
    string Name,
    string Summary,
    string ModuleVersion,
    string Instructions,
    EmployeeSkill[] Skills,
    EmployeeOnboarding Onboarding
)
{
    // The contract version this manifest targets; the catalog refuses versions it does not support.
    public int ContractVersion { get; init; } = EmployeeModuleCatalog.ContractVersion;
    public EmployeeTool[] Tools { get; init; } = [];
    public EmployeeKpi[] Kpis { get; init; } = [];
    public EmployeeCapabilities Capabilities { get; init; } = new();
    public string[] UiSlots { get; init; } = [];

    // Read-only, tool-backed work the owner can start, such as gathering evidence (docs/adr/0011-employee-actions.md).
    public EmployeeAction[] Actions { get; init; } = [];

    // Position in hiring lists; the first employee is the suggested first hire. Ties sort by name.
    public int DisplayOrder { get; init; } = 100;

    public bool Has(string skill) => Skills.Any(s => s.Id == skill);

    public string[] SkillIds(SkillMode? mode = null) =>
        [.. Skills.Where(s => mode is null || s.Mode == mode).Select(s => s.Id)];
}

public enum SkillMode
{
    // Ordinary model text work, dispatched only through the company's approval gates.
    Text,

    // Text work admitted to the measured one-job queue with exact owner review of the packet.
    Measured,

    // Work that needs an owner-approved execution grant (for example writing to a checkout).
    Execution,
}

public sealed record EmployeeSkill(string Id, string Name, string Description, SkillMode Mode = SkillMode.Text)
{
    // Opt in to a platform-bounded response profile; modules cannot supply arbitrary runtime limits.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string? MeasuredProfile { get; init; }

    // A role declares the work's purpose; the owner selects the model. The role cannot order a pricier bottle.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string? ModelProfile { get; init; }

    // A shift round (docs/adr/0171-an-owner-approved-shift.md): its requests also carry the employee's recorded weekly
    // numbers and where its recent work stands, so each round can move unfinished work forward.
    public bool ShiftContext { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public string? ReadContextPolicy { get; init; }

    // A skill whose reviewed result the module can publish: an execution's changes or a measured skill's approved
    // artifact (docs/adr/0012-company-publication.md, docs/adr/0015-publishing-an-approved-artifact.md).
    public bool Publishes { get; init; }
}

// Skills the company itself orchestrates. An employee opts in by declaring them.
public static class PlatformSkills
{
    public const string Chat = "chat";
    public const string MeetingTurn = "meeting-turn";
    public const string MeetingChair = "meeting-chair";
    public const string CompanyPlan = "company-plan";
}

public enum ToolRisk
{
    // Reads data the owner already shared with the company.
    Read,

    // Changes local state such as a checkout or a draft store.
    Write,

    // Acts outside the company: sends, publishes, pushes, merges.
    External,

    // Commits money or credits.
    Spend,
}

// A capability the employee needs. Declared in Phase 1; enforced by the Phase 2 tool registry.
public sealed record EmployeeTool(string Id, ToolRisk Risk, string Purpose);

public sealed record EmployeeKpi(string Id, string Name, string Unit, string Description);

public sealed record EmployeeAction(string Id, string Name, string Description, ActionField[] Fields)
{
    // The action's reviewed result names one exact external step the owner may publish
    // (docs/adr/0017-publishing-a-step-from-an-action.md). Other gathered evidence is never published.
    public bool Publishes { get; init; }
}

public sealed record ActionField(string Id, string Label, int MaxLength = 200)
{
    // A field with a fixed set of values is offered as a choice and refused with any other value.
    public string[]? Choices { get; init; }

    // The module lists this field's current values when the form opens, such as an ads account's campaigns.
    public bool LoadsChoices { get; init; }
}

// One loaded choice: the value the action receives and the label the owner sees.
public sealed record ActionChoice(string Value, string Label);

public sealed record EmployeeOnboarding(
    string Department,
    string DefaultName,
    string Responsibility,
    OnboardingField[] Fields,
    string Boundaries
);

public sealed record OnboardingField(string Id, string Help)
{
    // An optional field may be left out or blank when department context is saved; drafts still ask for it.
    public bool Optional { get; init; }
}

public sealed record EmployeeCapabilities
{
    // May claim an owner-approved workspace (checkout) for execution skills.
    public bool WorkspaceClaims { get; init; }

    // Sees the company-wide overview (departments and colleagues) rather than only its own context.
    public bool CompanyOverview { get; init; }
}

public static partial class EmployeeModuleCatalog
{
    public const int ContractVersion = 1;
    public const string AssemblyPrefix = "HireZero.Roles.";

    // Loads every HireZero.Roles.* assembly in the application's own directory and returns its modules in display
    // order. Only assemblies shipped with this build are considered; nothing is loaded from data roots.
    public static IEmployeeModule[] Discover(string? directory = null)
    {
        var root = directory ?? AppContext.BaseDirectory;
        var assemblies = Directory
            .EnumerateFiles(root, AssemblyPrefix + "*.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path)));
        return FromAssemblies(assemblies);
    }

    public static IEmployeeModule[] FromAssemblies(IEnumerable<Assembly> assemblies)
    {
        var modules = assemblies
            .SelectMany(assembly => assembly.GetCustomAttributes<EmployeeModuleAttribute>())
            .Select(attribute => Create(attribute.ModuleType))
            .ToArray();
        return Validate(modules);
    }

    public static IEmployeeModule[] Validate(IEmployeeModule[] modules)
    {
        foreach (var module in modules)
            Validate(module.Manifest);
        var duplicate = modules.GroupBy(m => m.Manifest.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException("Two employee modules declare the id '" + duplicate.Key + "'.");
        return [.. modules.OrderBy(m => m.Manifest.DisplayOrder).ThenBy(m => m.Manifest.Name, StringComparer.Ordinal)];
    }

    private static IEmployeeModule Create(Type type)
    {
        if (!typeof(IEmployeeModule).IsAssignableFrom(type) || type.IsAbstract)
            throw new InvalidOperationException(type.FullName + " is not a concrete IEmployeeModule.");
        return (IEmployeeModule)(
            Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(type.FullName + " could not be created.")
        );
    }

    private static void Validate(EmployeeManifest manifest)
    {
        void Require(bool condition, string problem)
        {
            if (!condition)
                throw new InvalidOperationException("Employee module '" + manifest.Id + "': " + problem);
        }
        Require(Identifier().IsMatch(manifest.Id), "the id must be lowercase letters, digits and hyphens.");
        Require(
            manifest.ContractVersion == ContractVersion,
            "targets contract version " + manifest.ContractVersion + "; this build supports " + ContractVersion + "."
        );
        Require(!string.IsNullOrWhiteSpace(manifest.Name), "a name is required.");
        Require(!string.IsNullOrWhiteSpace(manifest.Instructions), "instructions are required.");
        Require(manifest.Skills.Length > 0, "at least one skill is required.");
        Require(manifest.Skills.All(s => Identifier().IsMatch(s.Id)), "skill ids must be lowercase identifiers.");
        Require(manifest.Skills.Select(s => s.Id).Distinct().Count() == manifest.Skills.Length, "skill ids repeat.");
        Require(
            manifest.Skills.All(s =>
                s.MeasuredProfile is null
                || s.Mode == SkillMode.Measured && s.MeasuredProfile == CompanyMeasuredPolicy.DeliverableProfile
            ),
            "only measured skills may select the bounded deliverable response profile."
        );
        Require(manifest.Onboarding.Fields.Length > 0, "at least one onboarding field is required.");
        Require(
            manifest.Skills.All(s =>
                s.ModelProfile is null
                || s.Mode == SkillMode.Measured && s.ModelProfile == CompanyModelRouting.AnalysisProfile
            ),
            "only measured skills may select the owner-configured analysis model profile."
        );
        Require(
            manifest.Onboarding.Fields.All(f => Identifier().IsMatch(f.Id) && !string.IsNullOrWhiteSpace(f.Help)),
            "onboarding fields need lowercase ids and help text."
        );
        Require(
            manifest.Onboarding.Fields.Select(f => f.Id).Distinct().Count() == manifest.Onboarding.Fields.Length,
            "onboarding field ids repeat."
        );
        Require(manifest.Tools.All(t => ToolIdentifier().IsMatch(t.Id)), "tool ids must look like 'system.action'.");
        Require(manifest.Kpis.All(k => Identifier().IsMatch(k.Id)), "KPI ids must be lowercase identifiers.");
        Require(
            manifest.Actions.All(a => Identifier().IsMatch(a.Id) && !manifest.Has(a.Id)),
            "action ids must be lowercase identifiers distinct from skill ids."
        );
        Require(manifest.Actions.Select(a => a.Id).Distinct().Count() == manifest.Actions.Length, "action ids repeat.");
        Require(
            manifest.Actions.All(a =>
                a.Fields.Length > 0
                && a.Fields.All(f => Identifier().IsMatch(f.Id) && f.MaxLength is > 0 and <= 4000)
                && a.Fields.Select(f => f.Id).Distinct().Count() == a.Fields.Length
            ),
            "action fields need distinct lowercase ids and a bound of at most 4000 characters."
        );
        Require(
            manifest.Skills.All(s => !s.Publishes || s.Mode is SkillMode.Execution or SkillMode.Measured),
            "only execution and measured skills can publish."
        );
        Require(
            !manifest.Capabilities.WorkspaceClaims || manifest.Skills.Any(s => s.Mode == SkillMode.Execution),
            "workspace claims need an execution skill."
        );
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$")]
    private static partial Regex Identifier();

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$")]
    private static partial Regex ToolIdentifier();
}

public enum ExecutionStep
{
    Prepare,
    Run,
    Propose,
    Publish,
    Reconcile,
}

// What a module's reviewed execution may use: the store, the tool registry (audited, with the publication verifier)
// and, when the host has a qualified one, a coding runner factory.
public sealed record CompanyExecutionServices(CompanyStore Store, ToolRegistry Tools, ICompanyCodingFactory? Coding);
