using HireZero.Core;
using Microsoft.Data.Sqlite;

// Members, roles and sign-in binding (docs/adr/0020-members-roles-and-sign-in.md). Identities are fictional.
public sealed class CompanyMemberTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string Issuer = "https://idp.example.invalid|";
    private readonly TemporaryRoot _root = new("company-members-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private CompanyStore Store() =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T As<T>(CompanyStore store, string subject, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command, subject, out _));

    // Invites bind on the first sign-in with that verified email.
    private static string Join(CompanyStore store, string email, string role)
    {
        As<CompanyMember>(store, Owner, Cmd("member.invite") with { Body = email, Decision = role });
        var subject = Issuer + email.Split('@')[0];
        Assert.Null(store.SignIn(subject, email, emailVerified: false, "Unverified"));
        var actor = store.SignIn(subject, email.ToUpperInvariant(), emailVerified: true, "Fictional " + role);
        Assert.Equal(new CompanyActor(subject, role), actor);
        return subject;
    }

    [Fact]
    public void TheCreatorIsTheFirstOwnerAndStrangersAreRefused()
    {
        var store = Store();
        var owner = Assert.Single(store.Members());
        Assert.Equal((Owner, CompanyRoles.Owner, "active"), (owner.Id, owner.Role, owner.State));
        Assert.Null(store.SignIn(Issuer + "stranger", "stranger@example.invalid", true, "Stranger"));
        Assert.Equal(
            403,
            Assert
                .Throws<WorkspaceException>(() =>
                    store.Apply(Cmd("department.create") with { Name = "Ops" }, Issuer + "stranger", out _)
                )
                .Status
        );
    }

    [Fact]
    public void EachRoleRunsOnlyWhatItAllows()
    {
        var store = Store();
        var viewer = Join(store, "viewer@example.invalid", CompanyRoles.Viewer);
        var approver = Join(store, "approver@example.invalid", CompanyRoles.Approver);
        var refused = Assert.Throws<WorkspaceException>(() =>
            store.Apply(Cmd("thread.create") with { EmployeeId = "none", Name = "x" }, viewer, out _)
        );
        Assert.Contains("needs approver", refused.Message);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(Cmd("department.create") with { Name = "Ops" }, approver, out _)
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(Cmd("member.invite") with { Body = "x@example.invalid", Decision = "owner" }, approver, out _)
        );
        var department = As<CompanyDepartment>(store, Owner, Cmd("department.create") with { Name = "Ops" });
        Assert.Equal("Ops", department.Name);
        Assert.Equal(CompanyAccess.Steps, CompanyRoles.Approver);
        Assert.Equal(CompanyRoles.Owner, CompanyAccess.Required("workspace.grant"));
        Assert.Equal(CompanyRoles.Owner, CompanyAccess.Required("memory.keep"));
        var lessonRefusal = Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                Cmd("memory.keep") with
                {
                    Id = "fictional-report",
                    Decision = "keep",
                    Body = "A lesson",
                },
                approver,
                out _
            )
        );
        Assert.Contains("needs owner", lessonRefusal.Message);
        Assert.Equal(CompanyRoles.Approver, CompanyAccess.Required("publication.review"));
    }

    [Fact]
    public void AnActiveOwnerAlwaysRemainsAndChangesRecordTheirActor()
    {
        var store = Store();
        var approver = Join(store, "approver@example.invalid", CompanyRoles.Approver);
        var owner = store.Members().Single(m => m.Id == Owner);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(Cmd("member.revoke") with { Id = Owner, ExpectedVersion = owner.Version }, Owner, out _)
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                Cmd("member.role") with
                {
                    Id = Owner,
                    ExpectedVersion = owner.Version,
                    Decision = "viewer",
                },
                Owner,
                out _
            )
        );
        var member = store.Members().Single(m => m.Id == approver);
        var promoted = As<CompanyMember>(
            store,
            Owner,
            Cmd("member.role") with
            {
                Id = approver,
                ExpectedVersion = member.Version,
                Decision = "owner",
            }
        );
        As<CompanyMember>(store, approver, Cmd("member.revoke") with { Id = Owner, ExpectedVersion = owner.Version });
        Assert.Throws<WorkspaceException>(() => store.Actor(Owner));
        var revoked = store.Feed(0).Last(e => e.Type == "member.revoked");
        Assert.Equal(approver, revoked.Actor);
        Assert.Equal(Owner, store.Feed(0).First(e => e.Type == "member.invited").Actor);
        Assert.Equal(CompanyRoles.Owner, promoted.Role);
    }
}
