using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyBillingDeliveryTests : IDisposable
{
    private readonly TemporaryRoot _root = new("billing-delivery-");
    private readonly BillingFixtureProvider _provider = new();
    private DateTimeOffset _now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private CompanyStore Store(string company = "fictional-company") =>
        new(Path.Combine(_root.Path, "company.sqlite"), "owner@example.invalid", [], companyId: company)
        {
            Clock = () => _now,
            BillingConfiguration = _provider.Runtime().Configuration,
        };

    private BillingEventNotice Notice(string id = "evt_fictional") =>
        new(id, "checkout.session.completed", "cs_fictional", _now, new('a', 64));

    [Fact]
    public async Task ConcurrentDuplicateDeliveryIsDurableAndChangedCountersDoNotCreateAnotherEvent()
    {
        var store = Store();
        var notice = Notice();
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 3)
                .Select(_ =>
                    Task.Run(
                        () => Store().QueueBillingEvent(_provider.Identity, notice),
                        TestContext.Current.CancellationToken
                    )
                )
        );
        Assert.Single(results, inserted => inserted);
        Assert.False(
            Store("other-company").QueueBillingEvent(_provider.Identity, notice with { PayloadDigest = new('b', 64) })
        );
        Assert.Single(Store().BillingEventStatus(_provider.Identity));
        Assert.Throws<WorkspaceException>(() =>
            store.QueueBillingEvent(_provider.Identity, notice with { ObjectId = "cs_changed" })
        );
    }

    [Fact]
    public void ExpiredWorkerLeaseCanReadAgainButCannotOverwriteANewerAcknowledgement()
    {
        var store = Store();
        store.QueueBillingEvent(_provider.Identity, Notice());
        var original = store.TakeBillingEvent(_provider.Identity)!;
        Assert.Null(Store().TakeBillingEvent(_provider.Identity));
        _now = _now.AddMinutes(6);
        var replacement = Store().TakeBillingEvent(_provider.Identity)!;
        Assert.NotEqual(original.Lease, replacement.Lease);
        Assert.Equal(2, replacement.Attempts);
        Assert.False(store.FinishBillingEvent(_provider.Identity, original, "complete"));
        Assert.True(
            store.FinishBillingEvent(
                _provider.Identity,
                replacement,
                "complete",
                store.CompanyId,
                "fictional-operation"
            )
        );
        Assert.Null(store.TakeBillingEvent(_provider.Identity));
        Assert.False(
            store.QueueBillingEvent(_provider.Identity, Notice() with { CreatedAt = original.Notice.CreatedAt })
        );
    }

    [Fact]
    public void RepeatedReadFailuresBackOffAndRemainVisibleForOperatorReview()
    {
        var store = Store();
        store.QueueBillingEvent(_provider.Identity, Notice());
        for (var count = 1; count <= 12; count++)
        {
            var attempt = store.TakeBillingEvent(_provider.Identity)!;
            Assert.Equal(count, attempt.Attempts);
            store.FinishBillingEvent(_provider.Identity, attempt, "retry");
            Assert.Null(store.TakeBillingEvent(_provider.Identity));
            _now = _now.AddHours(2);
        }
        var saved = Store().BillingEventStatus(_provider.Identity).Single();
        Assert.Equal("needs-review", saved.State);
        Assert.Null(saved.Lease);
        Assert.Null(store.TakeBillingEvent(_provider.Identity));
        Assert.Equal(0, _provider.Creates);
    }

    [Fact]
    public void Migration26PreservesAnExistingApprovedPlanAndItsEnforcement()
    {
        var store = Store();
        var command = new CompanyCommand(Guid.NewGuid().ToString("D"), "billing.prepare")
        {
            Scope = BillingFixtureProvider.Plan.Id,
        };
        var review = CompanyJson.Decode<CompanyBillingCheckout>(store.Apply(command));
        string[] hashes;
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            hashes = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_decisions; DROP TABLE co_task_cost_reviews; DROP TABLE co_memory_analyses; DROP TABLE co_billing_events; DROP TABLE co_billing_portals; DROP TABLE IF EXISTS co_member_invites; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=26;"
            );
        }
        var reopened = Store();
        Assert.Equal(review, reopened.BillingCheckout(review.Id));
        Assert.True(reopened.Billing().Required);
        Assert.True(reopened.IsBillingOperation(command.RequestId));
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            hashes,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
    }

    [Fact]
    public void PartialBillingSettingsAreRejectedAndStripeSecretsAreNotSettings()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(name => name == "HIREZERO_BILLING_ENABLED" ? "1" : null)
        );
        Assert.Contains("HIREZERO_BILLING_CATALOG", error.Message);
        Assert.Contains("HIREZERO_STRIPE_ACCOUNT", error.Message);
        Assert.Contains("HIREZERO_STRIPE_LIVE_MODE", error.Message);
        Assert.Null(CompanyBillingRuntime.Create(CompanySettings.Load(_ => null), null));
        Assert.Contains(CompanyBillingRuntime.KeySecret, CompanySecrets.Known);
        Assert.Contains(CompanyBillingRuntime.WebhookSecret, CompanySecrets.Known);
        var file = Path.Combine(_root.Path, "settings.json");
        File.WriteAllText(
            file,
            CompanyJson.Encode(new Dictionary<string, string> { [CompanyBillingRuntime.KeySecret] = "fictional-value" })
        );
        var refused = Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(name => name == CompanySettings.FileVariable ? file : null)
        );
        Assert.DoesNotContain("fictional-value", refused.Message);
        Assert.Contains("is a secret", refused.Message);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
