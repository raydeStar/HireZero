using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Google;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyDriveDocumentTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-drive-");
    private const string Owner = "owner@example.invalid";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    [Fact]
    public async Task PreviewRequiresCompanyOwnerAndCurrentConnectionThenSeparateDocumentApproval()
    {
        using var fake = new DriveDocumentFixture();
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: new UnavailableModelProvider(),
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            fixtureGoogle: fake,
            settings: CompanySettings.Load(_ => null)
        )!;
        await app.StartAsync(Token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(address) };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        async Task Put(string suffix, object value)
        {
            using var response = await client.PutAsJsonAsync(
                "/api/company/integrations/google-drive/" + suffix,
                value,
                Token
            );
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var fileIds = new[] { DriveDocumentFixture.FileId, DriveDocumentFixture.FileId.ToUpperInvariant() };
        await Put(
            "settings",
            new
            {
                values = new
                {
                    fileIds = new[]
                    {
                        "https://docs.google.com/document/d/" + fileIds[0] + "/edit?usp=sharing",
                        fileIds[0],
                        "https://drive.google.com/file/d/" + fileIds[1] + "/view",
                    },
                },
            }
        );
        await Put("secrets", new { name = GoogleDriveDocuments.KeySecret, value = fake.Key });
        var options = await client.GetFromJsonAsync<JsonElement>("/api/company/documents/drive", Token);
        Assert.True(options.GetProperty("ready").GetBoolean());
        Assert.Equal(fileIds, options.GetProperty("fileIds").EnumerateArray().Select(x => x.GetString()));
        using (
            var refused = await client.PutAsJsonAsync(
                "/api/company/integrations/google-drive/settings",
                new { values = new { fileIds = new[] { "https://example.invalid/file/d/" + fileIds[0] } } },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(
            fileIds,
            (await client.GetFromJsonAsync<JsonElement>("/api/company/documents/drive", Token))
                .GetProperty("fileIds")
                .EnumerateArray()
                .Select(x => x.GetString())
        );
        using (
            var refused = await client.PutAsJsonAsync(
                "/api/company/integrations/google-drive/settings",
                new { values = new { fileIds = Enumerable.Range(0, 21).Select(i => "fictional_file_" + i).ToArray() } },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Empty(fake.Requests);
        var store = app.Services.GetRequiredService<CompanyStore>();
        const string route = "/api/company/documents/drive-preview";
        var body = new { fileId = DriveDocumentFixture.FileId };
        CompanyDriveDocuments.ImportPreview preview;
        string sourceReceipt;
        using (var response = await client.PostAsJsonAsync(route, body, Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
            preview = payload.Deserialize<CompanyDriveDocuments.ImportPreview>(CompanyJson.Options)!;
            sourceReceipt = payload.GetProperty("sourceReceipt").GetString()!;
        }
        Assert.Empty(store.Documents());
        var saved = CompanyJson.Decode<CompanyDocument>(
            store.Apply(
                C("document.save") with
                {
                    Body = CompanyJson.Encode(
                        new CompanyDocumentInput(
                            preview.Title,
                            preview.Text,
                            SourceName: preview.SourceName,
                            SourceReceipt: sourceReceipt
                        )
                    ),
                }
            )
        );
        Assert.Empty(store.RetrieveDocuments("patient").Excerpts);
        store.Apply(
            C("document.review") with
            {
                Id = saved.Id,
                ExpectedVersion = saved.Version,
                Digest = saved.Digest,
                Decision = "approve",
            }
        );
        Assert.Contains("Drive version: 1", Assert.Single(store.RetrieveDocuments("patient").Excerpts).Text);
        var approved = store.Document(saved.Id);
        var sourceRoute = "/api/company/documents/" + saved.Id + "/check-source";
        var sourceRequest = new { expectedVersion = approved.Version, digest = approved.Digest };
        async Task<JsonElement> CheckSource()
        {
            using var response = await client.PostAsJsonAsync(sourceRoute, sourceRequest, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        }
        Assert.Equal("unchanged", (await CheckSource()).GetProperty("state").GetString());
        fake.Version = "3";
        Assert.Equal("metadata-changed", (await CheckSource()).GetProperty("state").GetString());
        fake.Content = System.Text.Encoding.UTF8.GetBytes("Cobalt now uses patient workshops.");
        var changed = await CheckSource();
        Assert.Equal("text-changed", changed.GetProperty("state").GetString());
        Assert.Equal(approved, store.Document(saved.Id));
        Assert.Single(store.Documents());
        using (
            var response = await client.PostAsJsonAsync(
                "/api/companies",
                new { name = "Fictional neighboring studio" },
                Token
            )
        )
        {
            var neighbor = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetString();
            client.DefaultRequestHeaders.Add("X-HireZero-Company", neighbor);
        }
        var calls = fake.Requests.Count;
        using (var response = await client.PostAsJsonAsync(route, body, Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, fake.Requests.Count);
        Assert.False(
            (await client.GetFromJsonAsync<JsonElement>("/api/company/documents/drive", Token))
                .GetProperty("ready")
                .GetBoolean()
        );
        using (var response = await client.PostAsJsonAsync(sourceRoute, sourceRequest, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        fake.BeforeContent = _ => Put("settings", new { values = new { fileIds = Array.Empty<string>() } });
        using (var response = await client.PostAsJsonAsync(route, body, Token))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // Disconnect stops new reads; the separately approved copy remains until its owner revokes it.
        Assert.NotEmpty(store.RetrieveDocuments("patient").Excerpts);
        await Put("settings", new { values = new { fileIds = new[] { DriveDocumentFixture.FileId } } });
        fake.BeforeContent = _ =>
        {
            store.Apply(
                C("document.review") with
                {
                    Id = approved.Id,
                    ExpectedVersion = approved.Version,
                    Digest = approved.Digest,
                    Decision = "revoke",
                }
            );
            return Task.CompletedTask;
        };
        using (var response = await client.PostAsJsonAsync(sourceRoute, sourceRequest, Token))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var second = store.SignIn(
            "fictional-second-drive-owner",
            "second-owner@example.invalid",
            true,
            "Second owner"
        )!;
        fake.BeforeContent = ignoredToken =>
        {
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
            return Task.CompletedTask;
        };
        using (var response = await client.PostAsJsonAsync(route, body, Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        calls = fake.Requests.Count;
        using (var response = await client.GetAsync("/api/company/documents/drive", Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await client.PostAsJsonAsync(sourceRoute, sourceRequest, Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await client.PostAsJsonAsync(route, body, Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, fake.Requests.Count);
        await app.StopAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
