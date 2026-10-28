using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

public sealed class CompanyDecisionConnectionTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("decision-connection-");
    private readonly EphemeralDataProtectionProvider _protection = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static CompanyModelConnectionInput Key(int version = 0, string key = "fictional-decision-key") =>
        new(version, "openrouter", CompanyDecisionFixture.Model, key, "use-company-key");

    private static CompanyDecisionInput Input() =>
        new(
            CompanyDecisionFixture.Model,
            "{\"context\":\"Fictional bakery\"}",
            "Which next step respects the brief?",
            new() { ["draft"] = "Draft the FAQ", ["clarify"] = "Ask for the missing deadline" }
        );

    private CompanyModelConnection Connection(
        CompanyDecisionFixture handler,
        string company = CompanyStore.DefaultCompanyId
    )
    {
        var model = new CompanyModelConnection(
            company,
            new UnavailableModelProvider(),
            new(),
            CompanyDecisionFixture.Models,
            decisionHandler: handler
        );
        model.Initialize(_root.Path, _protection);
        return model;
    }

    private CompanyStore Store(CompanyModelConnection model) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions())
        {
            PrepareDecisionPacket = model.PrepareDecision,
            CurrentDecisionPacket = model.DecisionCurrent,
        };

    private static CompanyCommand Prepare() =>
        C("decision.prepare") with
        {
            CreditAllowance = 1,
            Body = CompanyJson.Encode(Input()),
        };

    private static CompanyCommand Run(CompanyDecision d) =>
        C("decision.run") with
        {
            Id = d.Id,
            ExpectedVersion = d.Version,
            Digest = d.Digest,
            Decision = "accept-soft-limits",
        };

    [Fact]
    public async Task SavedEncryptedAccountRestoresExactPacketsAndRotationRefusesOldAuthority()
    {
        using var handler = new CompanyDecisionFixture();
        var model = Connection(handler);
        Assert.Throws<WorkspaceException>(() => model.PrepareDecision(Input()));
        model.Save(Key());
        var store = Store(model);
        var d = CompanyJson.Decode<CompanyDecision>(store.Apply(Prepare()));
        var reopened = Connection(handler);
        Assert.True(reopened.DecisionCurrent(d.Packet));
        Assert.False(Connection(handler, "neighbor").DecisionCurrent(d.Packet));
        store.Apply(Run(d));
        var claim = store.TakeDecisionDispatch(d.Id)!;
        var result = await reopened.SendDecisionAsync(claim, Token);
        store.FinishDecision(d.Id, result);
        Assert.Equal("completed", store.Decision(d.Id).State);
        Assert.Equal(d.Packet.Json, Assert.Single(handler.Requests).Json);
        Assert.Equal("fictional-decision-key", handler.Requests[0].Key);
        reopened.Save(Key(1, "fictional-rotated-key"));
        Assert.False(reopened.DecisionCurrent(d.Packet));
        Assert.Equal("not_sent", (await reopened.SendDecisionAsync(claim, Token)).State);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("fictional-decision-key", CompanyJson.Encode(d));
    }

    [Fact]
    public async Task ModifiedSavedBytesAndUnsupportedInputsCannotReachTheProvider()
    {
        using var handler = new CompanyDecisionFixture();
        var model = Connection(handler);
        model.Save(Key());
        var store = Store(model);
        var d = CompanyJson.Decode<CompanyDecision>(store.Apply(Prepare()));
        store.Apply(Run(d));
        var claim = store.TakeDecisionDispatch(d.Id)!;
        var altered = claim with { Packet = claim.Packet with { Json = claim.Packet.Json.Replace("bakery", "shop") } };
        Assert.Equal("not_sent", (await model.SendDecisionAsync(altered, Token)).State);
        Assert.Throws<WorkspaceException>(() => model.PrepareDecision(Input() with { Model = "typesafe/jev-router" }));
        Assert.Throws<WorkspaceException>(() => model.PrepareDecision(Input() with { Criteria = null! }));
        Assert.Throws<WorkspaceException>(() => model.PrepareDecision(Input() with { StateJson = "{broken" }));
        Assert.Empty(handler.Requests);
    }

    private async Task<(WebApplication App, HttpClient Client)> Host(CompanyDecisionFixture handler)
    {
        var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            settings: CompanySettings.Load(_ => null),
            fixtureCompanyModels: CompanyDecisionFixture.Models,
            fixtureCompanyDecisionHandler: _ => handler
        )!;
        await app.StartAsync(Token);
        var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new(
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
            ),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        using var saved = await client.PutAsJsonAsync("/api/company/model-connection", Key(), Token);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        return (app, client);
    }

    [Fact]
    public async Task HttpReviewsReplayAndUncertainAccountingUseTheSameCompanyBoundLifecycle()
    {
        using var handler = new CompanyDecisionFixture();
        var (app, client) = await Host(handler);
        await using var owned = app;
        using var http = client;
        var store = app.Services.GetRequiredService<CompanyStore>();
        var prepare = Prepare();
        using var prepared = await client.PostAsJsonAsync("/api/company/commands", prepare, Token);
        var d = (await prepared.Content.ReadFromJsonAsync<CompanyDecision>(Token))!;
        using (var response = await client.GetAsync("/api/company/decisions/" + d.Id, Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        using (
            var response = await client.PostAsJsonAsync(
                "/api/companies",
                new { name = "Fictional neighboring bakery" },
                Token
            )
        )
        {
            var neighbor = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetString();
            client.DefaultRequestHeaders.Add("X-HireZero-Company", neighbor);
        }
        using (var response = await client.GetAsync("/api/company/decisions/" + d.Id, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await client.GetAsync("/api/company/operations/" + prepare.RequestId, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        var run = Run(d);
        using var first = await client.PostAsJsonAsync("/api/company/commands", run, Token);
        using var replay = await client.PostAsJsonAsync("/api/company/commands", run, Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await replay.Content.ReadAsStringAsync(Token));
        Assert.Single(handler.Requests);
        Assert.Equal("completed", store.Decision(d.Id).State);
        handler.Unknown = true;
        using var next = await client.PostAsJsonAsync("/api/company/commands", Prepare(), Token);
        var uncertain = (await next.Content.ReadFromJsonAsync<CompanyDecision>(Token))!;
        using var sent = await client.PostAsJsonAsync("/api/company/commands", Run(uncertain), Token);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        uncertain = store.Decision(uncertain.Id);
        Assert.Equal("unknown", uncertain.State);
        Assert.Equal(1, store.Usage().Current.HeldJobs);
        using var reconciled = await client.PostAsJsonAsync(
            "/api/company/commands",
            C("decision.reconcile") with
            {
                Id = uncertain.Id,
                ExpectedVersion = uncertain.Version,
                Digest = uncertain.Digest,
                Decision = "confirmed-usage-and-termination",
                Body = CompanyJson.Encode(
                    new CompanyDecisionReconciliation(
                        uncertain.ProviderReceipt!,
                        new(100, 20, BilledUsd: .00002m),
                        true,
                        "Fictional receipt verified."
                    )
                ),
            },
            Token
        );
        Assert.Equal(HttpStatusCode.OK, reconciled.StatusCode);
        Assert.Equal(0, store.Usage().Current.HeldJobs);
        Assert.Equal(2, handler.Requests.Count);
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task OwnerDemotionDuringTheCallWithholdsAdviceAndHidesAllPrivateReceipts()
    {
        using var handler = new CompanyDecisionFixture();
        var (app, client) = await Host(handler);
        await using var owned = app;
        using var http = client;
        var store = app.Services.GetRequiredService<CompanyStore>();
        var prepare = Prepare();
        using var prepared = await client.PostAsJsonAsync("/api/company/commands", prepare, Token);
        var d = (await prepared.Content.ReadFromJsonAsync<CompanyDecision>(Token))!;
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var second = store.SignIn("fictional-second-owner", "second-owner@example.invalid", true, "Second owner")!;
        handler.BeforeReply = () =>
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
        var run = Run(d);
        using var sent = await client.PostAsJsonAsync("/api/company/commands", run, Token);
        Assert.Equal(HttpStatusCode.Forbidden, sent.StatusCode);
        Assert.Equal("withheld", store.Decision(d.Id).State);
        Assert.Equal(120, store.Usage().Current.Tokens);
        foreach (
            var route in new[]
            {
                "/api/company/decisions",
                "/api/company/decisions/" + d.Id,
                "/api/company/operations/" + prepare.RequestId,
                "/api/company/operations/" + run.RequestId,
            }
        )
        {
            using var response = await client.GetAsync(route, Token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Single(handler.Requests);
        await app.StopAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
