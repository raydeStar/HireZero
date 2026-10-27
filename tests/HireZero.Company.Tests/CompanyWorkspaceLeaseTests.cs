using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyWorkspaceLeaseTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-checkout-lease-");
    private string Checkout => Path.Combine(_root.Path, "checkout");

    private CompanyStore Store(string company) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company);

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private CompanyAttempt Prepare(CompanyStore store)
    {
        Directory.CreateDirectory(Path.Combine(Checkout, ".git"));
        Directory.CreateDirectory(Path.Combine(Checkout, "nested"));
        var department = Apply<CompanyDepartment>(
            store,
            C("department.create") with
            {
                Name = "Fictional engineering",
            }
        );
        var employee = Apply<CompanyEmployee>(
            store,
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "developer",
                Name = "Fictional developer",
                Responsibility = "Review fictional files",
            }
        );
        var fields = store
            .Roles.Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(k => k, k => "Approved fictional " + k);
        var knowledge = Apply<CompanyKnowledge>(
            store,
            C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(fields),
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var thread = Apply<CompanyThread>(
            store,
            C("thread.create") with
            {
                EmployeeId = employee.Id,
                Name = "Fictional review",
            }
        );
        var task = Apply<CompanyTask>(
            store,
            C("task.create") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = "developer-work",
                Body = "Review the fictional checkout",
            }
        );
        store.Apply(C("workspace.grant") with { Id = task.Id, ExpectedVersion = task.Version, Body = Checkout });
        return Assert.IsType<CompanyAttempt>(store.Claim(employee.Id, true, task.Id, execution: true));
    }

    [Fact]
    public void APhysicalCheckoutHasOneOwnerAcrossCompaniesAndPathAliases()
    {
        var a = Store("co-a");
        var b = Store("co-b");
        var first = Prepare(a);
        var second = Prepare(b);
        var claim = a.AcquireWorkspace(first.Id, Checkout);
        Assert.Equal(claim, a.AcquireWorkspace(first.Id, Path.Combine(Checkout, "nested")));
        var refused = Assert.Throws<WorkspaceException>(() =>
            b.AcquireWorkspace(second.Id, Path.Combine(Checkout, "nested"))
        );
        Assert.DoesNotContain(a.CompanyId, refused.Message);
        Assert.DoesNotContain(first.Id, refused.Message);
        Assert.Throws<WorkspaceException>(() => b.FailAttempt(first.Id, "not-sent-fixture", false));
        Assert.Throws<WorkspaceException>(() => b.AcquireWorkspace(second.Id, Checkout));
        a.FailAttempt(first.Id, "not-sent-fixture", false);
        Assert.Equal(second.Id, b.AcquireWorkspace(second.Id, Checkout).AttemptId);
    }

    [Fact]
    public async Task ConcurrentCompaniesGetOneLeaseAndAnUnknownOutcomeKeepsItAfterReopen()
    {
        var stores = new[] { Store("co-race-a"), Store("co-race-b") };
        var attempts = stores.Select(Prepare).ToArray();
        using var gate = new ManualResetEventSlim();
        var requests = stores
            .Select(
                (store, i) =>
                    Task.Run(
                        () =>
                        {
                            gate.Wait(TestContext.Current.CancellationToken);
                            try
                            {
                                store.AcquireWorkspace(attempts[i].Id, Checkout);
                                return true;
                            }
                            catch (WorkspaceException)
                            {
                                return false;
                            }
                        },
                        TestContext.Current.CancellationToken
                    )
            )
            .ToArray();
        gate.Set();
        var results = await Task.WhenAll(requests);
        Assert.Single(results, success => success);
        var winner = Array.FindIndex(results, success => success);
        var loser = 1 - winner;
        stores[winner].FailAttempt(attempts[winner].Id, "coding-outcome-unknown", true);
        var reopened = Store(stores[winner].CompanyId);
        reopened.Recover();
        Assert.Equal("unknown", reopened.Work(attempts[winner].TaskId).State);
        Assert.Throws<WorkspaceException>(() => stores[loser].AcquireWorkspace(attempts[loser].Id, Checkout));
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal("1", check.Scalar("SELECT count(*) FROM co_workspace_leases"));
        Assert.Equal("1", check.Scalar("SELECT count(*) FROM co_resource_claims"));
    }

    [Fact]
    public void UpgradeRetainsLegacyClaimsAndOnlyConfirmedReleaseMakesTheCheckoutAvailable()
    {
        var a = Store("co-upgrade-a");
        var b = Store("co-upgrade-b");
        var first = Prepare(a);
        var second = Prepare(b);
        a.AcquireWorkspace(first.Id, Checkout);
        string[] hashes;
        using (var db = new StoreConnection(a.Database.Open(), a.Database))
        {
            hashes = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                row => row.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE co_workspace_leases; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=20;"
            );
        }
        var reopened = Store(a.CompanyId);
        Assert.Throws<WorkspaceException>(() => b.AcquireWorkspace(second.Id, Checkout));
        using (var check = new StoreConnection(reopened.Database.Open(), reopened.Database))
        {
            Assert.Equal(
                hashes,
                check.Rows(
                    "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                    row => row.GetString(0)
                )
            );
            Assert.Equal("0", check.Scalar("SELECT count(*) FROM co_workspace_leases"));
        }
        Assert.Equal(first.Id, reopened.AcquireWorkspace(first.Id, Checkout).AttemptId);
        reopened.FailAttempt(first.Id, "not-sent-fixture", false);
        Assert.Equal(second.Id, b.AcquireWorkspace(second.Id, Checkout).AttemptId);
    }

    [Fact]
    public void ConflictingLegacyClaimsAreNotSilentlyAssignedToEitherCompany()
    {
        var a = Store("co-legacy-a");
        var b = Store("co-legacy-b");
        var first = Prepare(a);
        var second = Prepare(b);
        var claim = a.AcquireWorkspace(first.Id, Checkout);
        using (var db = new StoreConnection(a.Database.Open(), a.Database))
        {
            db.Execute(
                "DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE co_workspace_leases; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=20;"
            );
            var other = new CompanyResourceClaim(claim.Id, second.TaskId, second.Id, second.EmployeeId);
            db.Execute(
                "INSERT INTO co_resource_claims(id,company_id,state,revision,payload) VALUES($0,$1,'saved',1,$2)",
                claim.Id,
                b.CompanyId,
                CompanyJson.Encode(other)
            );
        }
        var reopened = Store(a.CompanyId);
        Assert.Throws<WorkspaceException>(() => reopened.AcquireWorkspace(first.Id, Checkout));
        Assert.Throws<WorkspaceException>(() => b.AcquireWorkspace(second.Id, Checkout));
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal("2", check.Scalar("SELECT count(*) FROM co_resource_claims"));
        Assert.Equal("0", check.Scalar("SELECT count(*) FROM co_workspace_leases"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
