using System.Net;
using System.Net.Http.Json;
using HireZero.Core;
using HireZero.Integrations.Documents;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyWordImportTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("word-import-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId, string owner = Owner) =>
        new(Path.Combine(_root.Path, "company.sqlite"), owner, CompanyWorkspace.Definitions(), companyId: company);

    [Fact]
    public void ExtractedWordTextKeepsExistingReviewScopePersistenceAndRevocationBoundaries()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var preview = WordTextImport.Extract(WordDocumentFixture.BrandGuide(), CompanyStore.DocumentByteLimit);
        Assert.Empty(store.Documents());
        var saved = CompanyJson.Decode<CompanyDocument>(
            store.Apply(
                C("document.save") with
                {
                    Body = CompanyJson.Encode(
                        new CompanyDocumentInput(
                            "Fictional brand guide",
                            preview.Text,
                            EmployeeId: task.EmployeeId,
                            SourceName: "fictional-brand.docx"
                        )
                    ),
                }
            )
        );
        Assert.Empty(store.RetrieveDocuments("cobalt", task.EmployeeId).Excerpts);
        Assert.Equal("fictional-brand.docx", Store().Document(saved.Id).SourceName);
        var approved = CompanyJson.Decode<CompanyDocument>(
            store.Apply(
                C("document.review") with
                {
                    Id = saved.Id,
                    ExpectedVersion = saved.Version,
                    Digest = saved.Digest,
                    Decision = "approve",
                }
            )
        );
        Assert.Equal(saved.Text, Assert.Single(store.RetrieveDocuments("cobalt", task.EmployeeId).Excerpts).Text);
        Assert.Empty(store.RetrieveDocuments("cobalt", task.EmployeeId, true).Excerpts);
        var other = Store("co-other-word");
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.Document(saved.Id)).Status);
        store.Apply(
            C("document.review") with
            {
                Id = approved.Id,
                ExpectedVersion = approved.Version,
                Digest = approved.Digest,
                Decision = "revoke",
            }
        );
        Assert.Empty(store.RetrieveDocuments("cobalt", task.EmployeeId).Excerpts);
    }

    [Fact]
    public async Task HttpExtractionIsBoundedStatelessAndRequiresCurrentCompanyOwnerAuthority()
    {
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
        await app.StartAsync(Token);
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(address),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        const string path = "/api/company/documents/extract-word";
        var input = new CompanyDocuments.WordImportRequest(
            "fictional-brand.docx",
            Convert.ToBase64String(WordDocumentFixture.BrandGuide())
        );
        using (var response = await client.PostAsJsonAsync(path, input, Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Contains(
                "patient demonstrations",
                (await response.Content.ReadFromJsonAsync<WordTextPreview>(Token))!.Text
            );
        }
        Assert.Empty(store.Documents());
        foreach (
            var invalid in new[]
            {
                input with
                {
                    Name = "../outside.docx",
                },
                input with
                {
                    Name = "fictional.docm",
                },
                input with
                {
                    ContentBase64 = "not-base64",
                },
                input with
                {
                    ContentBase64 = null,
                },
            }
        )
        {
            using var response = await client.PostAsJsonAsync(path, invalid, Token);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }
        using (
            var response = await client.PostAsJsonAsync(
                path,
                input with
                {
                    ContentBase64 = new string('A', 3 * 1024 * 1024),
                },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        _ = Store("co-private-word", "different-owner@example.invalid");
        client.DefaultRequestHeaders.Add("X-HireZero-Company", "co-private-word");
        using (var response = await client.PostAsJsonAsync(path, input, Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var second = store.SignIn("fictional-second-owner", "second-owner@example.invalid", true, "Second owner")!;
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
        using (var response = await client.PostAsJsonAsync(path, input, Token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(store.Documents());
        await app.StopAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
