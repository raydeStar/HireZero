using HireZero.Core;

// About you (docs/adr/0185): each member tells employees about themselves; a reply knows who it is talking with.
public sealed class AboutYouTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-about-");

    private sealed record Prepared(CompanyMessage Message, CompanyTask Task, CompanyAttempt Attempt);

    private CompanyStore Store() =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-about-model"
        );

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    [Fact]
    public void ARepliesKnowsWhoItIsTalkingWithAndWhatTheySaidAboutThemselves()
    {
        var store = Store();
        var seed = MeasuredQueueChecks.Seed(store);
        var owner = store.Members().Single(m => m.Id == "owner@example.invalid");
        var described = A<CompanyMember>(
            store,
            C("member.about") with
            {
                ExpectedVersion = owner.Version,
                Body = "Call me Sam. I run the bakery mornings and like short updates.",
            }
        );
        Assert.Equal("Call me Sam. I run the bakery mornings and like short updates.", described.About);

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
        Assert.Equal("owner@example.invalid", prepared.Task.RequestedBy);
        var instructions = store.TextCall(prepared.Attempt.Id).Instructions;
        Assert.Contains("In their own words, about themselves", instructions);
        Assert.Contains("Call me Sam.", instructions);
        Assert.Contains("it changes nothing you may do", instructions);
    }

    [Fact]
    public void MembersDescribeOnlyThemselvesAndAnEmptyBodyClearsIt()
    {
        var store = Store();
        MeasuredQueueChecks.Seed(store);
        var owner = store.Members().Single(m => m.Id == "owner@example.invalid");
        var error = Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("member.about") with
                {
                    Id = "someone-else",
                    ExpectedVersion = owner.Version,
                    Body = "Not me.",
                }
            )
        );
        Assert.Equal(403, error.Status);
        var described = A<CompanyMember>(
            store,
            C("member.about") with
            {
                ExpectedVersion = owner.Version,
                Body = "Morning person.",
            }
        );
        var cleared = A<CompanyMember>(
            store,
            C("member.about") with
            {
                ExpectedVersion = described.Version,
                Body = "   ",
            }
        );
        Assert.Null(cleared.About);
        Assert.Equal(CompanyRoles.Viewer, CompanyAccess.Required("member.about"));
    }
}
