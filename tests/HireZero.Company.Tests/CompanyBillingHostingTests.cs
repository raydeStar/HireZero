using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Stripe;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyBillingHostingTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("billing-host-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private CompanyStore Store(BillingFixtureProvider provider, string company = CompanyStore.DefaultCompanyId) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company)
        {
            BillingConfiguration = provider.Runtime().Configuration,
        };

    private WebApplication App(CompanyStore store, BillingFixtureProvider? provider, bool enabled = true) =>
        CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureDatabase: store.Database,
            fixtureBilling: provider?.Runtime(enabled),
            settings: CompanySettings.Load(_ => null)
        )!;

    private static HttpClient Client(WebApplication app)
    {
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        return client;
    }

    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!done() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(30, Token);
        Assert.True(done(), "The fictional payment butler expected a saved receipt before tea went cold.");
    }

    private static async Task<T> Post<T>(HttpClient client, CompanyCommand command)
    {
        using var response = await client.PostAsJsonAsync("/api/company/commands", command, Token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<CompanyBillingCheckout> Approve(HttpClient client)
    {
        var proposed = await Post<CompanyBillingCheckout>(
            client,
            C("billing.prepare") with
            {
                Scope = BillingFixtureProvider.Plan.Id,
            }
        );
        return await Post<CompanyBillingCheckout>(
            client,
            C("billing.checkout") with
            {
                Id = proposed.Id,
                ExpectedVersion = proposed.Version,
                Digest = proposed.Digest,
                Decision = "approve-subscription",
            }
        );
    }

    private static async Task<HttpStatusCode> Deliver(HttpClient client, (string Body, string Signature) envelope)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CompanyBillingRuntime.WebhookPath)
        {
            Content = new StringContent(envelope.Body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", envelope.Signature);
        using var response = await client.SendAsync(request, Token);
        return response.StatusCode;
    }

    [Fact]
    public async Task OwnerJourneyUsesOneCheckoutAndSignedEventsActivateAndRemovePurchasedAllowance()
    {
        var provider = new BillingFixtureProvider();
        var store = Store(provider);
        await using var app = App(store, provider);
        await app.StartAsync(Token);
        using var client = Client(app);
        var checkout = await Approve(client);
        await Until(() => store.BillingCheckout(checkout.Id).Receipt is not null);
        Assert.Equal(1, provider.Creates);
        Assert.Null(store.Billing().Allowance);
        provider.Change(checkout.Id, "paid");
        var envelope = provider.Event(checkout.Id);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove("X-Dev-Request");
        Assert.Equal(HttpStatusCode.OK, await Deliver(client, envelope));
        Assert.Equal(HttpStatusCode.OK, await Deliver(client, envelope));
        await Until(() => store.Billing().Allowance is not null);
        await Until(() => store.BillingEventStatus(provider.Identity).Single().State == "complete");
        Assert.Equal(1, provider.Creates);
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        var portal = await Post<CompanyBillingPortal>(
            client,
            C("billing.portal") with
            {
                ExpectedVersion = store.Billing().State.Version,
                Decision = "open-billing",
            }
        );
        await Until(() => store.BillingPortal()?.Receipt is not null);
        Assert.Equal(portal.Id, store.BillingPortal()!.Id);
        Assert.Equal(1, provider.Portals);
        var view = await client.GetFromJsonAsync<JsonElement>("/api/company/billing", Token);
        Assert.Equal("active", view.GetProperty("state").GetProperty("status").GetString());
        provider.Change(checkout.Id, "past_due");
        Assert.Equal(HttpStatusCode.OK, await Deliver(client, provider.Event(checkout.Id, "invoice.payment_failed")));
        await Until(() => store.Billing().State.Status == "past_due");
        Assert.Null(store.Billing().Allowance);
        // An older event still reads current canonical state; delivery order cannot restore an old paid invoice.
        Assert.Equal(HttpStatusCode.OK, await Deliver(client, provider.Event(checkout.Id, "invoice.paid")));
        await Until(() => store.BillingEventStatus(provider.Identity).All(e => e.State == "complete"));
        Assert.Null(store.Billing().Allowance);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task DurableEventRecoversLostCreationForAnUnopenedCompanyWhileNewSubscriptionsAreDisabled()
    {
        var provider = new BillingFixtureProvider { LoseNextCheckoutReply = true };
        var first = Store(provider);
        var other = Store(provider, "fictional-other");
        var proposed = CompanyJson.Decode<CompanyBillingCheckout>(
            other.Apply(C("billing.prepare") with { Scope = BillingFixtureProvider.Plan.Id })
        );
        other.Apply(
            C("billing.checkout") with
            {
                Id = proposed.Id,
                ExpectedVersion = 1,
                Digest = proposed.Digest,
                Decision = "approve-subscription",
            }
        );
        await Assert.ThrowsAsync<BillingProviderException>(() =>
            new CompanyBillingWorkflow(other, provider).DispatchAsync(proposed.Id, Token)
        );
        provider.Change(proposed.Id, "paid");
        var envelope = provider.Event(proposed.Id);
        var notice = provider
            .Runtime()
            .Verify(Encoding.UTF8.GetBytes(envelope.Body), envelope.Signature, DateTimeOffset.UtcNow)!;
        first.QueueBillingEvent(provider.Identity, notice);
        await using var app = App(first, provider, enabled: false);
        await app.StartAsync(Token);
        await Until(() => other.Billing().Allowance is not null);
        Assert.Null(first.Billing().Allowance);
        Assert.Equal(1, provider.Creates);
        Assert.Equal("complete", first.BillingEventStatus(provider.Identity).Single().State);
        using var client = Client(app);
        using var disabled = await client.PostAsJsonAsync(
            "/api/company/commands",
            C("billing.prepare") with
            {
                Scope = BillingFixtureProvider.Plan.Id,
            },
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task DisposingAnUnstartedHostReleasesItsSchedulerOwnership()
    {
        var provider = new BillingFixtureProvider();
        var store = Store(provider);
        await App(store, provider).DisposeAsync();
        await using var reopened = App(store, provider);
    }

    [Fact]
    public async Task WebhookExceptionIsExactBoundedAndDoesNotBypassBrowserOrOwnerChecks()
    {
        var provider = new BillingFixtureProvider();
        var store = Store(provider);
        await using var app = App(store, provider);
        await app.StartAsync(Token);
        using var client = Client(app);
        var prepare = C("billing.prepare") with { Scope = BillingFixtureProvider.Plan.Id };
        var review = await Post<CompanyBillingCheckout>(client, prepare);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove("X-Dev-Request");
        using var noCsrf = await client.PostAsJsonAsync(
            "/api/company/commands",
            C("billing.prepare") with
            {
                Scope = BillingFixtureProvider.Plan.Id,
            },
            Token
        );
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, await Deliver(client, ("{}", "t=1,v1=00")));
        var ignored = CompanyJson.Encode(
            new
            {
                id = "evt_fictional_large",
                @object = "event",
                type = "customer.created",
                api_version = StripeBilling.ApiVersion,
                livemode = false,
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                data = new { @object = new { id = "cus_fictional_large", description = new string('x', 20_000) } },
            }
        );
        Assert.Equal(
            HttpStatusCode.OK,
            await Deliver(client, (ignored, BillingFixtureProvider.Sign(ignored, DateTimeOffset.UtcNow)))
        );
        var huge = new string('x', StripeWebhook.MaximumBodyBytes + 1);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            await Deliver(client, (huge, BillingFixtureProvider.Sign(huge, DateTimeOffset.UtcNow)))
        );
        using var suffix = await client.PostAsJsonAsync(CompanyBillingRuntime.WebhookPath + "/extra", new { }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, suffix.StatusCode);
        Assert.Empty(store.BillingEventStatus(provider.Identity));
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var second = store.SignIn("fictional-second", "second-owner@example.invalid", true, "Second owner")!;
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
        foreach (
            var path in new[]
            {
                "/api/company/billing",
                "/api/company/billing/checkouts/" + review.Id,
                "/api/company/operations/" + prepare.RequestId,
            }
        )
        {
            using var result = await client.GetAsync(path, Token);
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.DoesNotContain("price_fictional", await result.Content.ReadAsStringAsync(Token));
        }
        Assert.Equal(0, provider.Creates);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task UnknownPortalCreationIsNotReplayedAndRequiresAnExplicitNewLink()
    {
        var provider = new BillingFixtureProvider();
        var store = Store(provider);
        await using var app = App(store, provider);
        await app.StartAsync(Token);
        using var client = Client(app);
        var checkout = await Approve(client);
        await Until(() => store.BillingCheckout(checkout.Id).Receipt is not null);
        provider.Change(checkout.Id, "paid");
        Assert.Equal(HttpStatusCode.OK, await Deliver(client, provider.Event(checkout.Id)));
        await Until(() => store.Billing().Allowance is not null);
        provider.LoseNextPortalReply = true;
        await Post<CompanyBillingPortal>(
            client,
            C("billing.portal") with
            {
                ExpectedVersion = store.Billing().State.Version,
                Decision = "open-billing",
            }
        );
        await Until(() => store.BillingPortal()?.State == "uncertain");
        using var refused = await client.PostAsJsonAsync(
            "/api/company/commands",
            C("billing.portal") with
            {
                ExpectedVersion = store.Billing().State.Version,
                Decision = "open-billing",
            },
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(1, provider.Portals);
        await Post<CompanyBillingPortal>(
            client,
            C("billing.portal") with
            {
                ExpectedVersion = store.Billing().State.Version,
                Decision = "create-new-link",
            }
        );
        await Until(() => store.BillingPortal()?.Receipt is not null);
        Assert.Equal(2, provider.Portals);
        Assert.Equal(1, provider.Creates);
        await app.StopAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
