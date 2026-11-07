using HireZero.Core;
using HireZero.Roles.Marketing;

// The Marketing employee's personality (soul.md, ADR 0183 and 0184): a default the role offers, which the owner can
// edit per employee, reaching owner-facing prompts only.
public sealed class MarketingSoulTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-soul-");

    private sealed record Prepared(CompanyMessage Message, CompanyTask Task, CompanyAttempt Attempt);

    private CompanyStore Store() =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-soul-model"
        );

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    [Fact]
    public void TheRoleOffersItsSoulAndDraftInstructionsLeaveItOut()
    {
        var module = new MarketingModule();
        Assert.Contains("You are a colleague, not a tool.", module.Manifest.Soul);
        Assert.DoesNotContain("You are a colleague", module.InstructionsFor(MarketingModule.GoalDraft));
        Assert.DoesNotContain("You are a colleague", module.InstructionsFor(PlatformSkills.Chat));
    }

    [Fact]
    public void MorningRoundsFollowUpOnApprovedWorkOnce()
    {
        Assert.Contains("Follow up like a colleague would.", MarketingShift.Instructions);
        Assert.Contains("never ask about the same piece twice", MarketingShift.Instructions);
    }

    [Fact]
    public void RepliesCarryTheDefaultSoulUntilTheOwnerEditsItWithoutStoppingWork()
    {
        var store = Store();
        var seed = MeasuredQueueChecks.Seed(store);
        string Instructions()
        {
            var prepared = A<Prepared>(
                store,
                C("conversation.prepare") with
                {
                    EmployeeId = seed.EmployeeId,
                    ThreadId = seed.ThreadId,
                    Scope = "employee",
                    Body = "What should I do first today?",
                    CreditAllowance = 1,
                }
            );
            var instructions = store.TextCall(prepared.Attempt.Id).Instructions;
            store.Apply(C("task.cancel") with { Id = prepared.Task.Id, ExpectedVersion = prepared.Task.Version });
            return instructions;
        }

        Assert.Contains("You are a colleague, not a tool.", Instructions());

        var employee = MeasuredQueueChecks.Items<CompanyEmployee>(store, "employees").Single();
        var edited = A<CompanyEmployee>(
            store,
            C("employee.personality") with
            {
                Id = employee.Id,
                ExpectedVersion = employee.Version,
                Body = "Cheerful and brief. Call the owner by their first name.",
            }
        );
        Assert.Equal(employee.Generation, edited.Generation);
        Assert.Equal("active", edited.State);
        var withEdit = Instructions();
        Assert.Contains("Cheerful and brief.", withEdit);
        Assert.DoesNotContain("You are a colleague, not a tool.", withEdit);
        Assert.Contains("never what you may do", withEdit);

        var reset = A<CompanyEmployee>(
            store,
            C("employee.personality") with
            {
                Id = edited.Id,
                ExpectedVersion = edited.Version,
                Body = "  ",
            }
        );
        Assert.Null(reset.Personality);
        Assert.Contains("You are a colleague, not a tool.", Instructions());
    }
}
