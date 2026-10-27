using System.Net;
using System.Net.Http.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyRetirementTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-retirement-");

    private CompanyStore Store(string id = "fictional-company") =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: id);

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static StoreConnection Open(CompanyStore store) => new(store.Database.Open(), store.Database);

    private static void Row(CompanyStore store, string table, string state, object? payload = null)
    {
        using var db = Open(store);
        db.Execute(
            $"INSERT INTO {table}(id,company_id,state,revision,payload) VALUES($0,$1,$2,1,$3)",
            Guid.NewGuid().ToString("N"),
            store.CompanyId,
            state,
            CompanyJson.Encode(payload ?? new { marker = "Fictional private contents" })
        );
    }

    private static long Issue(CompanyRetirementReview review, string area) =>
        review.Issues.SingleOrDefault(i => i.Area == area)?.Records ?? 0;

    [Fact]
    public async Task EndpointUsesSelectedCompanyAndRejectsAnOwnerWhoWasDemoted()
    {
        var token = TestContext.Current.CancellationToken;
        var store = Store();
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureDatabase: store.Database,
            settings: CompanySettings.Load(_ => null)
        )!;
        await app.StartAsync(token);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(
                    app.Services.GetRequiredService<IServer>()
                        .Features.Get<IServerAddressesFeature>()!
                        .Addresses.First()
                ),
            };
            client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
            client.DefaultRequestHeaders.Add("X-HireZero-Company", store.CompanyId);
            using var response = await client.GetAsync("/api/company/retirement-review", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var review = await response.Content.ReadFromJsonAsync<CompanyRetirementReview>(token);
            Assert.Equal(store.CompanyId, review!.CompanyId);
            Assert.False(review.ErasureAvailable);
            store.Apply(C("member.invite") with { Body = "next-owner@example.invalid", Decision = "owner" });
            var second = store.SignIn("fictional-next", "next-owner@example.invalid", true, "Next owner")!;
            store.Apply(
                C("member.role") with
                {
                    Id = Owner,
                    ExpectedVersion = 1,
                    Decision = "viewer",
                },
                second.Subject,
                out _
            );
            using var denied = await client.GetAsync("/api/company/retirement-review", token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain("collections", await denied.Content.ReadAsStringAsync(token));
        }
        finally
        {
            await app.StopAsync(token);
        }
    }

    [Fact]
    public void ReviewRequiresCurrentOwnerEvenAfterAnEarlierSuccessfulRead()
    {
        var store = Store();
        foreach (var role in new[] { "owner", "approver", "viewer" })
        {
            var email = role + "-two@example.invalid";
            store.Apply(C("member.invite") with { Body = email, Decision = role });
            var member = store.SignIn("fictional-" + role, email, true, "Fictional member")!;
            if (role == "owner")
            {
                Assert.False(store.RetirementReview(member.Subject).ErasureAvailable);
                var current = store.Members().Single(m => m.Id == member.Subject);
                store.Apply(
                    C("member.role") with
                    {
                        Id = current.Id,
                        ExpectedVersion = current.Version,
                        Decision = "viewer",
                    }
                );
            }
            Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.RetirementReview(member.Subject)).Status);
        }
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.RetirementReview("unknown-subject")).Status);
    }

    [Fact]
    public void InventoryCoversCompanyTablesWithoutReturningContentsOrChangingRecords()
    {
        var store = Store();
        var neighbor = Store("fictional-neighbor");
        Row(store, "co_memory", "discarded");
        Row(neighbor, "co_memory", "discarded");
        Row(neighbor, "co_memory", "discarded");
        using var db = Open(store);
        var tables = db.Rows(
            "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'co_%' ORDER BY name",
            r => r.GetString(0)
        );
        string Snapshot() =>
            CompanyJson.Encode(
                tables
                    .Select(t => new
                    {
                        Table = t,
                        Rows = db.Rows(
                            $"SELECT * FROM {t} ORDER BY 1",
                            r =>
                                Enumerable
                                    .Range(0, r.FieldCount)
                                    .Select(i => r.IsDBNull(i) ? null : r.GetValue(i).ToString())
                                    .ToArray()
                        ),
                    })
                    .ToArray()
            );
        var before = Snapshot();
        var review = store.RetirementReview(Owner);
        Assert.Equal(tables.Where(t => t != "co_billing_events"), review.Collections.Select(c => c.Collection));
        Assert.Equal(1, review.Collections.Single(c => c.Collection == "co_memory").Records);
        Assert.Equal(2, neighbor.RetirementReview(Owner).Collections.Single(c => c.Collection == "co_memory").Records);
        Assert.Equal(before, Snapshot());
        Assert.DoesNotContain("Fictional private contents", CompanyJson.Encode(review));
        Assert.DoesNotContain(Owner, CompanyJson.Encode(review));
        Assert.False(review.ErasureAvailable);
        Assert.Contains(review.UnverifiedAreas, a => a.Contains("Backups"));
        Assert.Contains(review.UnverifiedAreas, a => a.Contains("pause"));
    }

    [Fact]
    public void SettledHistoryIsNotAnOutstandingActionButUnknownStatesAndHoldsRemainVisible()
    {
        var store = Store();
        (string Table, string Area, string[] Terminal, string[] Open)[] cases =
        [
            (
                "co_attempts",
                "model-attempts",
                ["committed", "withheld", "not_sent"],
                ["claimed", "reserved", "dispatching", "unknown", "future-state"]
            ),
            (
                "co_business_drafts",
                "profile-generation",
                ["completed", "withheld", "not_sent", "expired", "invalid", "reconciled"],
                ["proposed", "running", "unknown"]
            ),
            (
                "co_memory_analyses",
                "memory-analysis",
                ["completed", "withheld", "not_sent", "expired", "invalid", "reconciled"],
                ["proposed", "running", "unknown"]
            ),
            (
                "co_publications",
                "publications",
                ["published", "failed", "revoked", "expired", "refused"],
                ["proposed", "approved", "committing", "pushing", "unknown"]
            ),
            (
                "co_billing_checkouts",
                "checkout-payments",
                ["complete", "failed", "expired", "prepared"],
                ["queued", "dispatching", "open", "uncertain"]
            ),
            ("co_business_lookups", "website-discovery", ["completed", "partial", "withheld"], ["running", "unknown"]),
            ("co_reservations", "budget-holds", ["settled"], ["reserved"]),
            ("co_usage_entries", "usage-holds", ["settled"], ["reserved"]),
            (
                "co_notice_deliveries",
                "email-delivery",
                ["accepted", "closed", "skipped"],
                ["pending", "dispatching", "unknown"]
            ),
            ("co_connections", "connections", ["connected", "dismissed"], ["open"]),
        ];
        foreach (var c in cases)
        {
            foreach (var state in c.Terminal)
                Row(store, c.Table, state);
            Assert.Equal(0, Issue(store.RetirementReview(Owner), c.Area));
            foreach (var state in c.Open)
                Row(store, c.Table, state);
            Assert.Equal(c.Open.Length, Issue(store.RetirementReview(Owner), c.Area));
        }
    }

    [Fact]
    public void CompletionDoesNotHideUnreleasedResourcesOrASubscription()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        Row(store, "co_tasks", "completed");
        var grant = new CompanyExecutionGrant(
            "fictional-grant",
            "attempt",
            "task",
            "employee",
            "private-workspace",
            "revision",
            "fingerprint",
            "digest",
            "route",
            false,
            "digest",
            "completed",
            now,
            DispatchedAt: now
        );
        Row(store, "co_execution_grants", "completed", grant);
        Row(store, "co_execution_grants", "completed", grant with { Release = new(true, false, "held", now) });
        Row(store, "co_execution_grants", "completed", grant with { Release = new(true, true, "released", now) });
        foreach (var table in new[] { "co_browser_jobs", "co_sandbox_jobs" })
        {
            Row(store, table, "completed", new { startedAt = now, settledAt = (DateTimeOffset?)null });
            Row(store, table, "failed", new { startedAt = now, settledAt = now });
        }
        Row(store, "co_resource_claims", "held");
        Row(store, "co_billing_state", "canceled", new { subscriptionId = "private-provider-id" });
        var review = store.RetirementReview(Owner);
        Assert.Equal(0, Issue(review, "unfinished-work"));
        Assert.Equal(2, Issue(review, "code-cleanup"));
        Assert.Equal(1, Issue(review, "script-resources"));
        Assert.Equal(1, Issue(review, "browser-resources"));
        Assert.Equal(1, Issue(review, "checkout-claims"));
        Assert.Equal(1, Issue(review, "subscription"));
        Assert.DoesNotContain("private-provider-id", CompanyJson.Encode(review));
        Assert.DoesNotContain("private-workspace", CompanyJson.Encode(review));
        Assert.Empty(Store("empty-neighbor").RetirementReview(Owner).Issues);
    }

    [Fact]
    public void MerchantInboxCountsOnlyExplicitlyAssignedUnresolvedEvents()
    {
        var store = Store();
        using var db = Open(store);
        foreach (var company in new[] { store.CompanyId, "another-company", null })
        foreach (var state in new[] { "complete", "ignored", "retry", "needs-review" })
            db.Execute(
                "INSERT INTO co_billing_events(provider,id,state,payload) VALUES($0,$1,$2,$3)",
                new string('b', 64),
                Guid.NewGuid().ToString("N"),
                state,
                CompanyJson.Encode(new { companyId = company })
            );
        var review = store.RetirementReview(Owner);
        Assert.Equal(2, Issue(review, "billing-events"));
        Assert.Contains(review.UnverifiedAreas, a => a.Contains("not yet assigned"));
        Assert.DoesNotContain("another-company", CompanyJson.Encode(review));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
