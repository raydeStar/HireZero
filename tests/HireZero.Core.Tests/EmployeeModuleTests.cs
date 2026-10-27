using HireZero.Core;

// Manifest validation and the company projection of an employee module.
public sealed class EmployeeModuleTests
{
    private sealed class Module(EmployeeManifest manifest) : IEmployeeModule
    {
        public EmployeeManifest Manifest { get; } = manifest;

        public string InstructionsFor(string skill) => skill == "draft" ? "Draft instructions." : Manifest.Instructions;
    }

    private static EmployeeManifest Manifest(string id = "writer", int order = 100) =>
        new(
            id,
            "Writer " + id,
            "Fictional writer.",
            "1.0.0",
            "Base instructions.",
            [new(PlatformSkills.Chat, "Chat", "Talk."), new("draft", "Draft", "Write a draft.", SkillMode.Measured)],
            new("Writing", "Writer", "Write drafts.", [new("purpose", "Why write?")], "Draft only.")
        )
        {
            DisplayOrder = order,
        };

    [Fact]
    public void OrdersByDisplayOrderThenName()
    {
        var modules = EmployeeModuleCatalog.Validate([
            new Module(Manifest("zeta", 1)),
            new Module(Manifest("beta", 5)),
            new Module(Manifest("alpha", 5)),
        ]);
        Assert.Equal(["zeta", "alpha", "beta"], modules.Select(m => m.Manifest.Id));
    }

    [Fact]
    public void RefusesDuplicateIds()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            EmployeeModuleCatalog.Validate([new Module(Manifest()), new Module(Manifest())])
        );
        Assert.Contains("writer", error.Message);
    }

    public static TheoryData<EmployeeManifest> Invalid =>
        new(
            Manifest() with
            {
                ContractVersion = EmployeeModuleCatalog.ContractVersion + 1,
            },
            Manifest("Not An Id"),
            Manifest() with
            {
                Skills = [],
            },
            Manifest() with
            {
                Skills = [new("draft", "A", "a"), new("draft", "B", "b")],
            },
            Manifest() with
            {
                Onboarding = new("Writing", "Writer", "Write.", [], "Draft only."),
            },
            Manifest() with
            {
                Tools = [new("no-dot", ToolRisk.Read, "Missing system prefix.")],
            },
            Manifest() with
            {
                Capabilities = new() { WorkspaceClaims = true },
            },
            Manifest() with
            {
                Actions = [new("draft", "Collides with a skill", "x", [new("field", "Field")])],
            },
            Manifest() with
            {
                Actions = [new("gather", "No fields", "x", [])],
            },
            Manifest() with
            {
                Actions = [new("gather", "Unbounded", "x", [new("field", "Field", 100000)])],
            },
            Manifest() with
            {
                // Only execution and measured skills can publish.
                Skills = [new("draft", "Draft", "Draft text.") { Publishes = true }],
            }
        );

    [Fact]
    public void MeasuredSkillsMayPublishTheirApprovedArtifact()
    {
        var manifest = Manifest() with
        {
            Skills = [new("draft", "Draft", "Draft text.", SkillMode.Measured) { Publishes = true }],
        };
        EmployeeModuleCatalog.Validate([new Module(manifest)]);
        Assert.Equal(["draft"], CompanyRole.From(new Module(manifest)).PublishingSkills);
    }

    [Fact]
    public void DeliverableProfilesAreBoundedAndOnlyAvailableToMeasuredSkills()
    {
        var skill = new EmployeeSkill("draft", "Draft", "Fictional draft.", SkillMode.Measured)
        {
            MeasuredProfile = CompanyMeasuredPolicy.DeliverableProfile,
        };
        EmployeeModuleCatalog.Validate([new Module(Manifest() with { Skills = [skill] })]);
        Assert.Throws<InvalidOperationException>(() =>
            EmployeeModuleCatalog.Validate([
                new Module(Manifest() with { Skills = [skill with { Mode = SkillMode.Text }] }),
            ])
        );
        Assert.Throws<InvalidOperationException>(() =>
            EmployeeModuleCatalog.Validate([
                new Module(Manifest() with { Skills = [skill with { MeasuredProfile = "unlimited" }] }),
            ])
        );
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void RefusesMalformedManifests(EmployeeManifest manifest) =>
        Assert.Throws<InvalidOperationException>(() => EmployeeModuleCatalog.Validate([new Module(manifest)]));

    [Fact]
    public void AnalysisPurposeIsOptionalAndOnlyValidForMeasuredSkills()
    {
        var skill = new EmployeeSkill("draft", "Draft", "Fictional draft.", SkillMode.Measured);
        Assert.DoesNotContain("modelProfile", CompanyJson.Encode(skill));
        skill = skill with { ModelProfile = CompanyModelRouting.AnalysisProfile };
        EmployeeModuleCatalog.Validate([new Module(Manifest() with { Skills = [skill] })]);
        Assert.Throws<InvalidOperationException>(() =>
            EmployeeModuleCatalog.Validate([
                new Module(Manifest() with { Skills = [skill with { Mode = SkillMode.Text }] }),
            ])
        );
        Assert.Throws<InvalidOperationException>(() =>
            EmployeeModuleCatalog.Validate([
                new Module(Manifest() with { Skills = [skill with { ModelProfile = "invented-tier" }] }),
            ])
        );
        var routing = new CompanyModelRouting();
        Assert.DoesNotContain("analysisModel", CompanyJson.Encode(routing));
        Assert.Equal(routing.TextModel, routing.For(false, analysis: true));
        Assert.Equal(
            routing.CodingModel,
            (routing with { AnalysisModel = "openai/fictional-analysis" }).For(true, true)
        );
        Assert.Throws<WorkspaceException>(() => (routing with { AnalysisModel = "invalid" }).Validate());
    }

    [Fact]
    public void CompanyProjectionCarriesSkillModesAndInstructions()
    {
        var role = CompanyRole.From(new Module(Manifest()));
        Assert.Equal(["chat", "draft"], role.Skills);
        Assert.Equal(["draft"], role.MeasuredSkills);
        Assert.Empty(role.ExecutionSkills);
        Assert.Equal(["purpose"], role.OnboardingFields);
        Assert.Equal("Draft instructions.", role.InstructionsForSkill!("draft"));
        Assert.Equal("Base instructions.", role.InstructionsForSkill!("chat"));
    }
}
