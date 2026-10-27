using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Google;

public sealed class GoogleDriveDocumentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "drive-documents-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void Configure(params string[] ids)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "google-drive.json"), CompanyJson.Encode(new { fileIds = ids }));
    }

    private GoogleDriveDocuments Reader(DriveDocumentFixture fake, Func<string?>? key = null)
    {
        Configure(DriveDocumentFixture.FileId);
        return new(_root, key ?? (() => fake.Key), fake);
    }

    [Fact]
    public void FileLinksBecomeExactIdsWithoutFollowingOtherAddresses()
    {
        var field = Assert.Single(new GoogleDriveDocuments(_root, () => null).Setup.Fields);
        const string id = "Fictional_AbC123";
        Assert.True(field.CaseSensitive);
        foreach (
            var input in new[]
            {
                id,
                "https://docs.google.com/document/d/" + id + "/edit?usp=sharing#heading=test",
                "https://drive.google.com/file/d/" + id + "/view?usp=drive_link",
                "https://drive.google.com/open?id=" + id + "&usp=sharing",
            }
        )
        {
            var normalized = field.Normalize!(input);
            Assert.Equal(id, normalized);
            Assert.Null(field.Check!(normalized));
        }
        foreach (
            var input in new[]
            {
                "https://example.invalid/file/d/" + id,
                "https://drive.google.com.example.invalid/file/d/" + id,
                "http://drive.google.com/file/d/" + id,
                "https://drive.google.com:8443/file/d/" + id,
                "https://owner@drive.google.com/file/d/" + id,
                "https://drive.google.com/drive/folders/" + id,
                "https://docs.google.com/spreadsheets/d/" + id + "/edit",
                "https://drive.google.com/open?id=" + id + "&id=AnotherFile123",
                "https://drive.google.com/file/d/" + id + "/permissions",
                "https://drive.google.com/file/d/Fictional%2FAbC123/view",
            }
        )
            Assert.NotNull(field.Check!(field.Normalize!(input)));
    }

    [Fact]
    public async Task OwnerSelectedGoogleDocExportsTextWithSourceVersionAndNoImpersonation()
    {
        using var fake = new DriveDocumentFixture();
        var reader = Reader(fake);
        Assert.True(reader.Status().Active);
        Assert.Empty(fake.Requests);
        var result = await reader.ReadAsync(DriveDocumentFixture.FileId, Token);
        Assert.Equal(GoogleDriveDocuments.ReadScope, fake.Scope);
        Assert.Contains("patient demonstrations", result.Text);
        Assert.Contains("Drive version: 1", result.Text);
        Assert.Contains("https://drive.google.com/file/d/" + DriveDocumentFixture.FileId, result.Text);
        Assert.EndsWith(".txt", result.SourceName);
        Assert.Contains("not a live sync", result.Notice);
        Assert.Equal("google-drive", result.Source.Provider);
        Assert.Equal(DriveDocumentFixture.FileId, result.Source.ExternalId);
        Assert.Equal("1", result.Source.Version);
        Assert.Equal(CompanyJson.Digest(result.Text.Replace("\r\n", "\n")), result.Source.CopyDigest);
        Assert.Equal(CompanyJson.Digest(Encoding.UTF8.GetString(fake.Content)), result.Source.ContentDigest);
        Assert.Equal(4, fake.Requests.Count);
        Assert.Contains(fake.Requests, r => r.Contains("/export?mimeType=text%2Fplain"));
        Assert.DoesNotContain(fake.Requests, r => r.Contains("/permissions") || r.Contains("files?"));
    }

    [Fact]
    public async Task DisallowedIdsOrMissingKeysNeverReachGoogle()
    {
        using var fake = new DriveDocumentFixture();
        var reader = Reader(fake);
        foreach (
            var id in new[]
            {
                "unlisted_file_id",
                "../private",
                "https://example.invalid/",
                DriveDocumentFixture.FileId.ToUpperInvariant(),
            }
        )
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(id, Token))).Status);
        Assert.Empty(fake.Requests);
        reader = Reader(fake, () => null);
        Assert.False(reader.Status().Active);
        Assert.Equal(
            409,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task WordAndUtf8ImportsUseTheSameBoundedTextReviewContract()
    {
        using var fake = new DriveDocumentFixture
        {
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Name = "Fictional / brand.docx",
            Content = WordDocumentFixture.BrandGuide(),
        };
        var reader = Reader(fake);
        var word = await reader.ReadAsync(DriveDocumentFixture.FileId, Token);
        Assert.Contains("Channel\tCadence", word.Text);
        Assert.Equal("Fictional _ brand.docx", word.SourceName);
        Assert.Contains("automatic list labels", word.Notice);
        Assert.Contains(fake.Requests, r => r.Contains("?alt=media&supportsAllDrives=true"));
        fake.MimeType = "text/markdown";
        fake.Content = Encoding.UTF8.GetBytes("\uFEFF# Fictional guide\nKeep a patient voice.");
        Assert.Contains("# Fictional guide", (await reader.ReadAsync(DriveDocumentFixture.FileId, Token)).Text);
        fake.Content = [255, 254, 0];
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
    }

    [Fact]
    public async Task ChangedVersionsOrRevokedConnectionWithholdTheEntirePreview()
    {
        using var fake = new DriveDocumentFixture { ChangeVersion = true };
        var key = fake.Key;
        var reader = Reader(fake, () => key);
        Assert.Equal(
            409,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        fake.ChangeVersion = false;
        var beforeRevocation = fake.Requests.Count;
        fake.BeforeContent = _ =>
        {
            key = "";
            return Task.CompletedTask;
        };
        Assert.Equal(
            409,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        Assert.Equal(beforeRevocation + 3, fake.Requests.Count);
        key = fake.Key;
        fake.BeforeContent = _ =>
        {
            Configure();
            return Task.CompletedTask;
        };
        Assert.Equal(
            409,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
    }

    [Fact]
    public async Task UnsupportedOrRestrictedFilesAndRedirectsAreNotDownloaded()
    {
        using var fake = new DriveDocumentFixture();
        var reader = Reader(fake);
        foreach (
            var mime in new[]
            {
                "application/vnd.google-apps.folder",
                "application/vnd.google-apps.shortcut",
                "application/pdf",
            }
        )
        {
            fake.MimeType = mime;
            Assert.Equal(
                422,
                (
                    await Assert.ThrowsAsync<WorkspaceException>(() =>
                        reader.ReadAsync(DriveDocumentFixture.FileId, Token)
                    )
                ).Status
            );
        }
        fake.MimeType = "text/plain";
        fake.CanDownload = false;
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        fake.CanDownload = true;
        fake.Trashed = true;
        Assert.Equal(
            422,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        fake.Trashed = false;
        fake.Status = HttpStatusCode.Redirect;
        var failure = await Assert.ThrowsAsync<WorkspaceException>(() =>
            reader.ReadAsync(DriveDocumentFixture.FileId, Token)
        );
        Assert.Equal(502, failure.Status);
        Assert.DoesNotContain("upstream error", failure.Message);
        Assert.DoesNotContain(fake.Requests, r => r.Contains("alt=media") || r.Contains("/export"));
    }

    [Fact]
    public async Task DeclaredAndStreamingSizeLimitsRejectWholeCopiesWithoutTruncation()
    {
        using var fake = new DriveDocumentFixture { Content = new byte[65537] };
        var reader = Reader(fake);
        Assert.Equal(
            413,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        fake.UnknownLength = true;
        Assert.Equal(
            413,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
        fake.Content = Encoding.UTF8.GetBytes(new string('a', 65536));
        Assert.Equal(
            413,
            (
                await Assert.ThrowsAsync<WorkspaceException>(() => reader.ReadAsync(DriveDocumentFixture.FileId, Token))
            ).Status
        );
    }

    [Fact]
    public async Task CancelingAPreviewStopsTheReadWithoutAutomaticRetry()
    {
        using var fake = new DriveDocumentFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.BeforeContent = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var reader = Reader(fake);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var read = reader.ReadAsync(DriveDocumentFixture.FileId, cancel.Token);
        await started.Task.WaitAsync(Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(3, fake.Requests.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
