using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanySignupTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-signup-");
    private string Path => System.IO.Path.Combine(_root.Path, "company.sqlite");
    private DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private CompanyStore Store(string id = CompanyStore.DefaultCompanyId, string owner = "owner@example.invalid") =>
        new(Path, owner, CompanyWorkspace.Definitions(), companyId: id) { Clock = () => _now };

    private static CompanyCreation Signup(CompanyStore store, string subject, CompanySignupPolicy? policy = null) =>
        store.Signup(subject, subject + "@example.invalid", true, "Fictional owner", policy ?? new());

    [Fact]
    public void SignupIsOffByDefaultAndRefusedInLocalMode()
    {
        var settings = CompanySettings.Load(_ => null);
        Assert.False(settings.OpenSignup);
        Assert.Equal(new CompanySignupPolicy(), settings.SignupPolicy);
        var store = Store();
        Assert.False(store.HasSpendCaps);
        Assert.Null(store.FreeAllowance());
        Assert.Throws<WorkspaceException>(() => Signup(store, "guest"));
        var enabled = CompanySettings.Load(n => n == "HIREZERO_OPEN_SIGNUP" ? "1" : null);
        var error = Assert.Throws<InvalidOperationException>(() =>
            CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                "owner@example.invalid",
                true,
                _root.Path,
                settings: enabled
            )
        );
        Assert.Contains("HIREZERO_OPEN_SIGNUP", error.Message);
        foreach (
            var setting in new[]
            {
                "HIREZERO_SIGNUP_MAX_COMPANIES",
                "HIREZERO_SIGNUP_COMPANY_CAP_CENTS",
                "HIREZERO_DAILY_CAP_CENTS",
            }
        )
            Assert.Throws<InvalidOperationException>(() => CompanySettings.Load(n => n == setting ? "-1" : null));
    }

    [Fact]
    public async Task ConcurrentSignupsForOneSubjectCreateOneOwnedCompanyAndSurviveRestart()
    {
        var store = Store();
        store.ConfigureSignup(new());
        var created = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Signup(store, "guest"))));
        Assert.All(created, c => Assert.Equal(created[0], c));
        Assert.Equal(2, CompanyStore.Companies(Path).Length);
        var company = Store(created[0].Id, "guest");
        Assert.Equal("owner", company.Actor("guest").Role);
        Assert.Single(company.Members());
        Assert.Equal(created[0], Signup(Store(), "guest"));
        Assert.Equal(50m, company.FreeAllowance()!.RemainingCents);
        Assert.Throws<WorkspaceException>(() =>
            company.CreateCompany("Extra company", Guid.NewGuid().ToString("D"), "guest")
        );
    }

    [Fact]
    public async Task InstallMaximumIsAtomicAndAnUnverifiedEmailCannotConsumeASlot()
    {
        var store = Store();
        var policy = new CompanySignupPolicy(MaxCompanies: 1);
        store.ConfigureSignup(policy);
        Assert.Throws<WorkspaceException>(() =>
            store.Signup("unverified", "guest@example.invalid", false, "Guest", policy)
        );
        Assert.Throws<WorkspaceException>(() => store.Signup("invalid", "invalid", true, "Guest", policy));
        var results = await Task.WhenAll(
            new[] { "one", "two" }.Select(subject =>
                Task.Run(() =>
                {
                    try
                    {
                        return Signup(store, subject, policy);
                    }
                    catch (WorkspaceException)
                    {
                        return null;
                    }
                })
            )
        );
        Assert.Single(results.OfType<CompanyCreation>());
        Assert.Equal(2, CompanyStore.Companies(Path).Length);
    }

    [Fact]
    public void CompaniesCannotReadEachOthersMembershipOrUseAnotherCompanyToCreateAgain()
    {
        var root = Store();
        root.ConfigureSignup(new());
        var a = Signup(root, "alpha");
        var b = Signup(root, "beta");
        var first = Store(a.Id, "alpha");
        var second = Store(b.Id, "beta");
        Assert.Throws<WorkspaceException>(() => first.Actor("beta"));
        Assert.Throws<WorkspaceException>(() => second.Actor("alpha"));
        Assert.DoesNotContain(first.Members(), m => m.Id == "beta");
        root.Apply(
            new(Guid.NewGuid().ToString("D"), "member.invite") { Body = "alpha@example.invalid", Decision = "owner" }
        );
        root.SignIn("alpha", "alpha@example.invalid", true, "Alpha");
        Assert.Throws<WorkspaceException>(() =>
            root.CreateCompany("Extra company", Guid.NewGuid().ToString("D"), "alpha")
        );
    }

    [Fact]
    public async Task SharedCapSerializesCompaniesAndUnknownReservationsStayOnTheirDayAndCompany()
    {
        var root = Store();
        var policy = new CompanySignupPolicy(DailyCapCents: 5);
        root.ConfigureSignup(policy);
        var stores = new[] { "alpha", "beta" }.Select(s => Store(Signup(root, s, policy).Id, s)).ToArray();
        var accepted = await Task.WhenAll(
            stores.Select(store =>
                Task.Run(() =>
                {
                    try
                    {
                        store.ReserveModelSpend("job", .04m);
                        return true;
                    }
                    catch (ModelTransportException)
                    {
                        return false;
                    }
                })
            )
        );
        Assert.Single(accepted, x => x);
        Assert.All(stores, s => Assert.Equal(1m, s.FreeAllowance()!.DailyRemainingCents));
        _now = _now.AddDays(1);
        // An uncertain hold cannot pin later days' shared limit, but stays on its company's lifetime cap.
        Assert.All(stores, s => Assert.Equal(5m, s.FreeAllowance()!.DailyRemainingCents));
        var winner = stores[accepted[0] ? 0 : 1];
        Assert.Equal(46m, winner.FreeAllowance()!.CompanyRemainingCents);
        Assert.Throws<ModelTransportException>(() => winner.ReserveModelSpend("job", .01m));
        winner.SettleModelSpend("job", .01m);
        Assert.Equal(5m, winner.FreeAllowance()!.DailyRemainingCents);
        Assert.Equal(49m, winner.FreeAllowance()!.CompanyRemainingCents);
        Assert.True(Store().HasSpendCaps); // Removing the enrollment switch does not erase the ledger.
    }

    [Fact]
    public void ASignupCompanyCannotInviteOrClaimSomeoneElsesSignIn()
    {
        var root = Store();
        root.ConfigureSignup(new());
        var attacker = Store(Signup(root, "attacker").Id, "attacker");
        var refused = Assert.Throws<WorkspaceException>(() =>
            attacker.Apply(
                new(Guid.NewGuid().ToString("D"), "member.invite")
                {
                    Body = "victim@example.invalid",
                    Decision = "viewer",
                }
            )
        );
        Assert.Equal(403, refused.Status);
        Assert.Null(attacker.SignIn("victim", "victim@example.invalid", true, "Victim"));
        Assert.Equal(CompanyRoles.Owner, Signup(root, "victim").Role);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
