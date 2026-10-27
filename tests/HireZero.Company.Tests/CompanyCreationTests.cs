using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyCreationTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-creation-");
    private string DatabasePath => Path.Combine(_root.Path, "company.sqlite");

    private CompanyStore Store(string id = CompanyStore.DefaultCompanyId) =>
        new(DatabasePath, Owner, CompanyWorkspace.Definitions(), companyId: id);

    private static string RequestId() => Guid.NewGuid().ToString("D");

    private static CompanyCommand C(string action) => new(RequestId(), action);

    [Fact]
    public void RefusedNamesAndIncompleteWritesNeverLeavePhantomCompanies()
    {
        var store = Store();
        foreach (var name in new[] { "", "   ", new string('x', 81), "api_key=fictional-secret" })
            Assert.Throws<WorkspaceException>(() => store.CreateCompany(name, RequestId(), Owner));
        Assert.Throws<WorkspaceException>(() => store.CreateCompany("Fictional Studio", "not-a-uuid", Owner));
        Assert.Single(CompanyStore.Companies(DatabasePath));

        using var db = new SqliteConnection("Data Source=" + DatabasePath);
        db.Open();
        using var trigger = db.CreateCommand();
        trigger.CommandText =
            "CREATE TRIGGER fictional_creation_failure BEFORE INSERT ON co_members WHEN NEW.company_id <> 'local-company' BEGIN SELECT RAISE(ABORT,'fictional membership failure'); END";
        trigger.ExecuteNonQuery();
        var request = RequestId();
        Assert.Throws<SqliteException>(() => store.CreateCompany("Fictional Studio", request, Owner));
        Assert.Single(CompanyStore.Companies(DatabasePath));
        Assert.Null(store.CreatedCompany(request, Owner));
    }

    [Fact]
    public async Task ConcurrentExactRequestsCommitOneCompanyAndOneAuditedOwner()
    {
        var store = Store();
        var request = RequestId();
        var receipts = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => Task.Run(() => store.CreateCompany("Fictional Studio", request, Owner)))
        );
        Assert.All(receipts, receipt => Assert.Equal(receipts[0], receipt));
        Assert.Equal(2, CompanyStore.Companies(DatabasePath).Length);
        var destination = Store(receipts[0].Id);
        Assert.Single(destination.Members());
        Assert.Single(
            destination.AuditEntries(),
            entry => entry.Kind == "event" && entry.Body.Contains("company.created")
        );
        Assert.DoesNotContain(store.AuditEntries(), entry => entry.Body.Contains(receipts[0].Id));
        Assert.True(destination.VerifyAudit().Intact);
    }

    [Fact]
    public void RecoverySurvivesRestartAndDoesNotRenameOrRecreateTheDestination()
    {
        var store = Store();
        var request = RequestId();
        var created = store.CreateCompany("Fictional Studio", request, Owner);
        var destination = Store(created.Id);
        destination.Rename("Renamed Fictional Studio");
        Assert.Equal("Renamed Fictional Studio", Store().CreatedCompany(request, Owner)!.Name);
        Assert.Equal("Renamed Fictional Studio", Store().CreateCompany("Fictional Studio", request, Owner).Name);
        Assert.Equal(
            409,
            Assert.Throws<WorkspaceException>(() => store.CreateCompany("Different Input", request, Owner)).Status
        );
        Assert.Equal(2, CompanyStore.Companies(DatabasePath).Length);
        Assert.Null(store.CreatedCompany(RequestId(), Owner));
    }

    [Fact]
    public void CreationAndRecoveryUseCurrentMembershipAndNeverGrantAccessToAnotherActor()
    {
        var store = Store();
        store.Apply(C("member.invite") with { Body = "other@example.invalid", Decision = CompanyRoles.Owner });
        var other = store.SignIn("fictional-other-subject", "other@example.invalid", true, "Other owner")!;
        var request = RequestId();
        var created = store.CreateCompany("Fictional Studio", request, Owner);
        Assert.Null(store.CreatedCompany(request, other.Subject));
        var otherCreated = store.CreateCompany("Fictional Studio", request, other.Subject);
        Assert.NotEqual(created.Id, otherCreated.Id);
        var secondOwner = store.Members().Single(member => member.Id == other.Subject);
        store.Apply(
            C("member.role") with
            {
                Id = other.Subject,
                ExpectedVersion = secondOwner.Version,
                Decision = CompanyRoles.Viewer,
            }
        );
        Assert.Equal(
            403,
            Assert
                .Throws<WorkspaceException>(() => store.CreateCompany("Forbidden Studio", RequestId(), other.Subject))
                .Status
        );
        Assert.NotNull(store.CreatedCompany(request, other.Subject));

        var destination = Store(created.Id);
        destination.Apply(
            C("member.invite") with
            {
                Body = "replacement@example.invalid",
                Decision = CompanyRoles.Owner,
            }
        );
        destination.SignIn("fictional-replacement", "replacement@example.invalid", true, "Replacement owner");
        destination.Apply(C("member.revoke") with { Id = Owner, ExpectedVersion = 1 });
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.CreatedCompany(request, Owner)).Status);
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => store.CreateCompany("Fictional Studio", request, Owner)).Status
        );
        Assert.Equal("revoked", destination.Members().Single(member => member.Id == Owner).State);
        Assert.Equal(3, CompanyStore.Companies(DatabasePath).Length);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
