using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Support;
using Microsoft.Data.Sqlite;

// The third employee, Support (docs/adr/0034-support-employee.md): discovered with no edit outside its own folders,
// hired like any other, and working only on redacted text, with legal, security and privacy requests flagged for a
// person. Customers, messages and numbers are fictional; 4111 1111 1111 1111 is the standard test card number.
public sealed class SupportModuleTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("support-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    [Fact]
    public void ContactAndCardDetailsAreRedactedButOrderNumbersKept()
    {
        var r = SupportIntake.Redact(
            "I'm jo.fictional@example.invalid, call +1 (555) 010-2000 or 555.010.3000. Card 4111 1111 1111 1111. Order 123456789, ref 1234567890123456, on 2026-10-19."
        );
        Assert.Equal(
            "I'm [email], call [phone] or [phone]. Card [card]. Order 123456789, ref 1234567890123456, on 2026-10-19.",
            r.Text
        );
        Assert.Equal((1, 2, 1), (r.Emails, r.Phones, r.Cards));
    }

    [Theory]
    [InlineData("The site is down and I'm locked out, please help ASAP", "urgent", false)]
    [InlineData("I was charged twice, I want a refund", "billing", false)]
    [InlineData("My lawyer will file a chargeback", "billing, legal", true)]
    [InlineData("Someone else logged in, I think I was hacked", "security", true)]
    [InlineData("Under GDPR please delete my data", "privacy", true)]
    [InlineData("Thanks, the new feature is lovely", "none", false)]
    public void MessagesAreFlaggedAndSensitiveOnesGoToAPerson(string message, string flags, bool escalate)
    {
        var classified = SupportIntake.Classify(message);
        Assert.Equal(flags, classified.ToString());
        Assert.Equal(escalate, classified.Escalate);
    }

    [Fact]
    public void IntakeSplitsMessagesAndCountsWhatNeedsAPerson()
    {
        var text = SupportIntake.Prepare(
            new Dictionary<string, string>
            {
                ["messages"] =
                    "Where is my order? Reach me at sam@example.invalid\n---\nUnder GDPR, delete my data now.\n  ---  \n\nThe app is lovely.",
            }
        );
        Assert.Contains("Customer messages prepared by the support-intake action: 3", text);
        Assert.Contains("- Redacted: 1 email(s)", text);
        Assert.DoesNotContain("sam@example.invalid", text);
        Assert.Contains("- Flags: privacy - needs a person", text);
        Assert.Contains("Needing a person: 1 of 3.", text);

        Assert.Equal(
            422,
            Assert
                .Throws<WorkspaceException>(() =>
                    SupportIntake.Prepare(new Dictionary<string, string> { ["messages"] = " --- " })
                )
                .Status
        );
        var many = string.Join(
            "\n---\n",
            Enumerable.Range(1, SupportIntake.MaxMessages + 1).Select(i => "Message " + i)
        );
        Assert.Throws<WorkspaceException>(() =>
            SupportIntake.Prepare(new Dictionary<string, string> { ["messages"] = many })
        );
    }

    [Fact]
    public void AReplyBriefIsRedactedAndBoundedBeforeItIsSaved()
    {
        var saved = new SupportModule().ValidateInput(
            SupportModule.Reply,
            JsonSerializer.Serialize(
                new
                {
                    message = "Refund to card 4111111111111111 please, sam@example.invalid",
                    context = "Fictional order 123456789",
                    outcome = "Explain the 30-day return window",
                }
            )
        );
        Assert.DoesNotContain("4111", saved);
        Assert.DoesNotContain("sam@example.invalid", saved);
        Assert.Contains("[card]", saved);
        Assert.Contains("123456789", saved);
        Assert.Throws<WorkspaceException>(() =>
            new SupportModule().ValidateInput(SupportModule.Reply, """{"message":"only"}""")
        );
    }

    // Discovered and hired with no change to the host, Core or any other role: the module only exists in its folders.
    [Fact]
    public async Task SupportIsDiscoveredHiredAndRunsItsAction()
    {
        Assert.Contains(EmployeeModuleCatalog.Discover(), m => m is SupportModule);
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var role = Assert.Single(store.Roles, r => r.Id == "support");
        Assert.Equal([SupportModule.Reply], role.MeasuredSkills.Intersect([SupportModule.Reply]));
        Assert.Empty(new SupportModule().Manifest.Tools);

        var department = CompanyJson.Decode<CompanyDepartment>(
            store.Apply(Cmd("department.create") with { Name = "Customer care" })
        );
        var knowledge = CompanyJson.Decode<CompanyKnowledge>(
            store.Apply(
                Cmd("knowledge.save") with
                {
                    DepartmentId = department.Id,
                    Body = CompanyJson.Encode(
                        role.OnboardingFields.ToDictionary(f => f, f => "Approved fictional " + f)
                    ),
                }
            )
        );
        store.Apply(
            Cmd("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var employee = CompanyJson.Decode<CompanyEmployee>(
            store.Apply(
                Cmd("employee.hire") with
                {
                    DepartmentId = department.Id,
                    RoleId = "support",
                    Name = "Support partner",
                    Responsibility = "Fictional customers",
                }
            )
        );
        var thread = CompanyJson.Decode<CompanyThread>(
            store.Apply(Cmd("thread.create") with { EmployeeId = employee.Id, Name = "Inbox" })
        );
        var input = new Dictionary<string, string>
        {
            ["messages"] =
                "I think I was hacked, call me on +44 20 7946 0000 or jo@example.invalid, card 4111 1111 1111 1111",
        };
        var task = CompanyJson.Decode<CompanyTask>(
            store.Apply(
                Cmd("action.start") with
                {
                    EmployeeId = employee.Id,
                    ThreadId = thread.Id,
                    Skill = SupportIntake.Action,
                    Body = JsonSerializer.Serialize(input),
                }
            )
        );
        // Nothing raw is stored: the module redacts action input before the task is saved (owner walkthrough).
        Assert.DoesNotContain("7946", task.Input);
        Assert.DoesNotContain("jo@example.invalid", task.Input);
        Assert.DoesNotContain("4111", task.Input);
        var registry = new ToolRegistry(store);
        // As the host does, the action runs on the stored input.
        var output = await new SupportModule().RunActionAsync(
            SupportIntake.Action,
            CompanyJson.Decode<Dictionary<string, string>>(store.Work(task.Id).Input),
            registry.For(new SupportModule().Manifest, employee.Id, task.Id).ReadOnly(),
            TestContext.Current.CancellationToken
        );
        var artifact = store.CompleteAction(task.Id, output);

        Assert.Equal("action:support-intake", artifact.Provenance);
        Assert.Contains("[phone]", artifact.Body);
        Assert.Contains("- Redacted: 1 email(s), 1 phone number(s), 1 card number(s)", artifact.Body);
        Assert.Contains("security - needs a person", artifact.Body);
        Assert.Empty(store.ToolCalls());
    }
}
