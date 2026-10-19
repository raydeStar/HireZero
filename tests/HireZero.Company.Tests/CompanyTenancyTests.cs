using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Several companies in one data root (docs/adr/0021-companies-per-data-root.md): each sees only its own work and
// members, and a request acts in the company it names. Companies are fictional.
public sealed class CompanyTenancyTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-tenancy-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    [Fact]
    public void CompaniesInOneDatabaseNeverSeeEachOther()
    {
        var path = Path.Combine(_root.Path, "company.sqlite");
        var first = new CompanyStore(path, Owner, CompanyWorkspace.Definitions());
        var second = new CompanyStore(path, Owner, CompanyWorkspace.Definitions(), companyId: "co-fictional-two");
        second.Rename("Fictional Two");
        first.Apply(Cmd("department.create") with { Name = "First only" });
        second.Apply(Cmd("member.invite") with { Body = "second@example.invalid", Decision = "viewer" });

        Assert.Equal(
            ["co-fictional-two", CompanyStore.DefaultCompanyId],
            CompanyStore.Companies(path).Select(c => c.Id)
        );
        Assert.Equal("Fictional Two", second.Name());
        var firstData = JsonSerializer.SerializeToElement(first.Dashboard(), CompanyJson.Options);
        var secondData = JsonSerializer.SerializeToElement(second.Dashboard(), CompanyJson.Options);
        Assert.Equal(1, firstData.GetProperty("departments").GetArrayLength());
        Assert.Equal(0, secondData.GetProperty("departments").GetArrayLength());
        Assert.Single(first.Members());
        Assert.Equal(2, second.Members().Length);
        Assert.Throws<ArgumentException>(() =>
            new CompanyStore(path, Owner, CompanyWorkspace.Definitions(), companyId: "../escape")
        );
    }

    [Fact]
    public async Task ARequestActsInTheCompanyItNames()
    {
        var previous = Environment.GetEnvironmentVariable(ExecutionHold.Variable);
        Environment.SetEnvironmentVariable(ExecutionHold.Variable, "0");
        try
        {
            await using var app = CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                Owner,
                true,
                Path.Combine(_root.Path, "host"),
                new UnavailableModelProvider(),
                fixtureUrl: "http://127.0.0.1:0",
                fixtureTools: _ => { }
            )!;
            await app.StartAsync(Token);
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            async Task<HttpResponseMessage> Send(
                HttpMethod method,
                string path,
                object? body = null,
                string? company = null
            )
            {
                var request = new HttpRequestMessage(method, path);
                request.Headers.Host = "127.0.0.1:5065";
                if (company is not null)
                    request.Headers.Add("X-HireZero-Company", company);
                if (body is not null)
                {
                    request.Headers.Add("Origin", "http://127.0.0.1:5065");
                    request.Headers.Add("X-Dev-Request", "1");
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body, CompanyJson.Options),
                        Encoding.UTF8,
                        "application/json"
                    );
                }
                return await client.SendAsync(request, Token);
            }
            async Task<JsonElement> Json(HttpResponseMessage response)
            {
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone();
            }

            var created = await Json(await Send(HttpMethod.Post, "/api/companies", new { name = "Fictional Second" }));
            var second = created.GetProperty("id").GetString()!;
            Assert.Equal("owner", created.GetProperty("role").GetString());
            await Json(
                await Send(
                    HttpMethod.Post,
                    "/api/company/commands",
                    Cmd("department.create") with
                    {
                        Name = "Second only",
                    },
                    second
                )
            );

            var mine = await Json(await Send(HttpMethod.Get, "/api/companies"));
            Assert.Equal(2, mine.GetArrayLength());
            var secondDashboard = await Json(await Send(HttpMethod.Get, "/api/company", company: second));
            var firstDashboard = await Json(await Send(HttpMethod.Get, "/api/company"));
            Assert.Equal(1, secondDashboard.GetProperty("data").GetProperty("departments").GetArrayLength());
            Assert.Equal(0, firstDashboard.GetProperty("data").GetProperty("departments").GetArrayLength());
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await Send(HttpMethod.Get, "/api/company", company: "co-missing")).StatusCode
            );
            Assert.Equal(
                "Fictional Second",
                (await Json(await Send(HttpMethod.Get, "/api/companies")))
                    .EnumerateArray()
                    .Single(c => c.GetProperty("id").GetString() == second)
                    .GetProperty("name")
                    .GetString()
            );
            await app.StopAsync(Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExecutionHold.Variable, previous);
        }
    }
}
