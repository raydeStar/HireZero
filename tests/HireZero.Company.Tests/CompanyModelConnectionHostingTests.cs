using System.Net;
using System.Net.Http.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyModelConnectionHostingTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-model-host-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    [Fact]
    public async Task HttpSettingsAreOwnerOnlyCompanyScopedCsrfProtectedAndNeverMakeProviderRequests()
    {
        using var fake = new CompanyModelFixture();
        var database = Path.Combine(_root.Path, "company.sqlite");
        var store = new CompanyStore(database, "owner@example.invalid", CompanyWorkspace.Definitions());
        _ = new CompanyStore(
            database,
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            companyId: "fictional-second"
        );
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            "owner@example.invalid",
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureDatabase: store.Database,
            fixtureTools: _ => { },
            settings: CompanySettings.Load(_ => null),
            fixtureCompanyModels: CompanyModelFixture.Models,
            fixtureCompanyModelHandler: _ => fake
        )!;
        await app.StartAsync(Token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(address) };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        var input = new CompanyModelConnectionInput(
            0,
            "openai",
            "openai/fictional-luna",
            "fictional-key",
            "use-company-key"
        );
        using (var noCsrf = await client.PutAsJsonAsync("/api/company/model-connection", input, Token))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        using (var saved = await client.PutAsJsonAsync("/api/company/model-connection", input, Token))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.DoesNotContain(input.Key, await saved.Content.ReadAsStringAsync(Token));
        }
        client.DefaultRequestHeaders.Add("X-HireZero-Company", "fictional-second");
        var second = await client.GetFromJsonAsync<CompanyModelConnectionStatus>(
            "/api/company/model-connection",
            Token
        );
        Assert.Equal("host", second!.Mode);
        Assert.False(second.KeyStored);
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        using (
            var removed = await client.SendAsync(
                new(HttpMethod.Delete, "/api/company/model-connection")
                {
                    Content = JsonContent.Create(new CompanyModelConnectionRemoval(1, "remove-key-and-use-host")),
                },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var owner = store.SignIn("fictional-second-owner", "second-owner@example.invalid", true, "Second owner")!;
        store.Apply(
            C("member.role") with
            {
                Id = "owner@example.invalid",
                ExpectedVersion = 1,
                Decision = "viewer",
            },
            owner.Subject,
            out _
        );
        using (var denied = await client.GetAsync("/api/company/model-connection", Token))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (
            var denied = await client.PutAsJsonAsync(
                "/api/company/model-connection",
                input with
                {
                    ExpectedVersion = 2,
                },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(fake.Requests);
        await app.StopAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
