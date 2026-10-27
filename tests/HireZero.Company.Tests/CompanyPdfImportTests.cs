using System.Net;
using System.Net.Http.Json;
using HireZero.Core;
using HireZero.Integrations.Documents;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed class CompanyPdfImportTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("pdf-import-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId, string owner = Owner) =>
        new(Path.Combine(_root.Path, "company.sqlite"), owner, CompanyWorkspace.Definitions(), companyId: company);

    [Fact]
    public void ExtractedPdfTextKeepsExistingReviewScopePersistenceAndRevocationBoundaries()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var preview = PdfTextImport.Extract(PdfDocumentFixture.Read("brand-guide"));
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
                            SourceName: "fictional-brand.pdf"
                        )
                    ),
                }
            )
        );
        Assert.Empty(store.RetrieveDocuments("cobalt", task.EmployeeId).Excerpts);
        Assert.Equal("fictional-brand.pdf", Store().Document(saved.Id).SourceName);
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
        var other = Store("co-other-pdf");
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
        const string path = "/api/company/documents/extract-pdf";
        var input = new CompanyDocuments.WordImportRequest(
            "fictional-brand.pdf",
            Convert.ToBase64String(PdfDocumentFixture.Read("brand-guide"))
        );
        using (var response = await client.PostAsJsonAsync(path, input, Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Contains(
                "patient demonstrations",
                (await response.Content.ReadFromJsonAsync<PdfTextPreview>(Token))!.Text
            );
        }
        Assert.Empty(store.Documents());
        foreach (
            var invalid in new[]
            {
                input with
                {
                    Name = "../outside.pdf",
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
        _ = Store("co-private-pdf", "different-owner@example.invalid");
        client.DefaultRequestHeaders.Add("X-HireZero-Company", "co-private-pdf");
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

    [Fact]
    public async Task PdfWorkerExtractsPageLabelsAndReportsMissingTextWithoutSilentlyDroppingPages()
    {
        var preview = await PdfImportProcess.ExtractAsync(
            PdfDocumentFixture.Read("brand-guide"),
            typeof(CompanyDocuments).Assembly.Location,
            Token
        );
        Assert.Equal(2, preview.Pages);
        Assert.Empty(preview.TextlessPages);
        Assert.Contains("Page 1\nCobalt Paper Studio", preview.Text);
        Assert.Contains("Page 2\nOwner review", preview.Text);
        var mixed = await PdfImportProcess.ExtractAsync(
            PdfDocumentFixture.Read("mixed-pages"),
            typeof(CompanyDocuments).Assembly.Location,
            Token
        );
        Assert.Equal([2], mixed.TextlessPages);
        Assert.Contains("Page 2\n[No selectable text", mixed.Text);
        Assert.Contains("pages: 2", mixed.Notice);
    }

    [Fact]
    public async Task PdfWorkerRejectsScansEncryptionDamagedFilesAndDocumentLimitsWithoutPartialText()
    {
        foreach (
            var (name, message, status) in new[]
            {
                ("scanned", "No selectable text", 422),
                ("encrypted", "Encrypted", 422),
                ("too-many-pages", "50 pages", 413),
                ("too-much-text", "64 KiB", 413),
            }
        )
        {
            var error = await Assert.ThrowsAsync<DocumentImportException>(() =>
                PdfImportProcess.ExtractAsync(
                    PdfDocumentFixture.Read(name),
                    typeof(CompanyDocuments).Assembly.Location,
                    Token
                )
            );
            Assert.Equal(status, error.Status);
            Assert.Contains(message, error.Message);
        }
        var malformed = await Assert.ThrowsAsync<DocumentImportException>(() =>
            PdfImportProcess.ExtractAsync(
                "%PDF-1.7 private-fixture-content"u8.ToArray(),
                typeof(CompanyDocuments).Assembly.Location,
                Token
            )
        );
        Assert.DoesNotContain("private-fixture-content", malformed.Message);
        Assert.Equal(
            413,
            (
                await Assert.ThrowsAsync<DocumentImportException>(() =>
                    PdfImportProcess.ExtractAsync(
                        new byte[PdfTextImport.FileByteLimit + 1],
                        typeof(CompanyDocuments).Assembly.Location,
                        Token
                    )
                )
            ).Status
        );
    }

    [Fact]
    public async Task CancelledImportDoesNotConsumeAWorkerSlotOrPreventTheNextImport()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PdfImportProcess.ExtractAsync(
                PdfDocumentFixture.Read("brand-guide"),
                typeof(CompanyDocuments).Assembly.Location,
                cancelled.Token
            )
        );
        var preview = await PdfImportProcess.ExtractAsync(
            PdfDocumentFixture.Read("brand-guide"),
            typeof(CompanyDocuments).Assembly.Location,
            Token
        );
        Assert.Contains("patient demonstrations", preview.Text);
    }

    [Fact]
    public async Task WorkerExitsOnItsOwnDeadlineEvenWhenItsParentNeverFinishesInput()
    {
        var executable = Path.GetFullPath(
            Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                "../../..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"
            )
        );
        var host = typeof(CompanyDocuments).Assembly.Location;
        var start = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (
            var arg in new[]
            {
                "exec",
                "--runtimeconfig",
                Path.ChangeExtension(host, ".runtimeconfig.json"),
                "--depsfile",
                Path.ChangeExtension(host, ".deps.json"),
                typeof(PdfTextImport).Assembly.Location,
                "--extract-pdf",
            }
        )
            start.ArgumentList.Add(arg);
        start.Environment.Clear();
        if (OperatingSystem.IsWindows())
            start.Environment["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot");
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            // Deliberately leave stdin open: the worker's own deadline must end this stalled parent.
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(74, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(Token));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}

internal static class PdfDocumentFixture
{
    public static byte[] Read(string name)
    {
        using var stream = typeof(PdfDocumentFixture).Assembly.GetManifestResourceStream(
            "PdfFixture." + name + ".pdf"
        )!;
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
