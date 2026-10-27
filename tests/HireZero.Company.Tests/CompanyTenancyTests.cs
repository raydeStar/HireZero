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
        Assert.Equal(("My company", "My company"), (first.Name(), CompanyStore.Companies(path)[1].Name));
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
    public Task ARequestActsInTheCompanyItNames() => RequestsActInTheCompanyTheyName(_root.Path);

    // Also run on PostgreSQL by StoreDatabaseTests (docs/adr/0025).
    internal static async Task RequestsActInTheCompanyTheyName(string root, StoreDatabase? database = null)
    {
        var previous = Environment.GetEnvironmentVariable(ExecutionHold.Variable);
        Environment.SetEnvironmentVariable(ExecutionHold.Variable, "0");
        try
        {
            await using var app = CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                Owner,
                true,
                Path.Combine(root, "host"),
                new UnavailableModelProvider(),
                fixtureUrl: "http://127.0.0.1:0",
                fixtureTools: _ => { },
                fixtureDatabase: database
            )!;
            await app.StartAsync(Token);
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            // The test host is local: a machine proxy (or a sandbox's dead one) must never carry this call.
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(address),
            };
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

            var before = await Json(await Send(HttpMethod.Get, "/api/companies"));
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await Send(HttpMethod.Post, "/api/companies", new { name = "   " })).StatusCode
            );
            Assert.Equal(
                before.GetArrayLength(),
                (await Json(await Send(HttpMethod.Get, "/api/companies"))).GetArrayLength()
            );
            var requestId = Guid.NewGuid().ToString("D");
            var created = await Json(
                await Send(HttpMethod.Post, "/api/companies", new { name = "Fictional Second", requestId })
            );
            var second = created.GetProperty("id").GetString()!;
            Assert.Equal("owner", created.GetProperty("role").GetString());
            Assert.Equal(
                second,
                (
                    await Json(
                        await Send(HttpMethod.Post, "/api/companies", new { name = "Fictional Second", requestId })
                    )
                )
                    .GetProperty("id")
                    .GetString()
            );
            var recovered = await Send(HttpMethod.Get, "/api/companies/creation/" + requestId);
            Assert.True(recovered.Headers.CacheControl!.NoStore);
            Assert.Equal(second, (await Json(recovered)).GetProperty("id").GetString());
            var absent = await Send(HttpMethod.Get, "/api/companies/creation/" + Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            Assert.Equal(
                "company-creation-not-recorded",
                JsonDocument
                    .Parse(await absent.Content.ReadAsStringAsync(Token))
                    .RootElement.GetProperty("code")
                    .GetString()
            );
            Assert.Equal(
                HttpStatusCode.Conflict,
                (await Send(HttpMethod.Post, "/api/companies", new { name = "Different input", requestId })).StatusCode
            );
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
            // Each company's audit export ends with its verified head (docs/adr/0022).
            var export = await Send(HttpMethod.Get, "/api/company/audit/export", company: second);
            Assert.Equal("application/x-ndjson", export.Content.Headers.ContentType?.MediaType);
            var lines = (await export.Content.ReadAsStringAsync(Token)).Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
            );
            using (var last = JsonDocument.Parse(lines[^1]))
            {
                Assert.True(last.RootElement.GetProperty("verified").GetBoolean());
                Assert.Equal(lines.Length - 1, last.RootElement.GetProperty("count").GetInt32());
                Assert.Equal(second, last.RootElement.GetProperty("company").GetString());
            }
            await app.StopAsync(Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExecutionHold.Variable, previous);
        }
    }
}
