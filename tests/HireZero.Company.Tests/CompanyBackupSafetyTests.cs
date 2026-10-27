using System.Diagnostics;
using System.Text.Json;

public sealed partial class CompanyBackupTests
{
    [Fact]
    public void ExistingPartialFolderIsNeverReusedOrDeletedAfterCopyFailure()
    {
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "invalid.sqlite"), "Fictional invalid database");
        var when = DateTimeOffset.Parse("2026-10-26T03:00:00Z");
        var partial = Path.Combine(backups, "hirezero-backup-20261026-030000.partial");
        Directory.CreateDirectory(partial);
        var sentinel = Path.Combine(partial, "keep.txt");
        File.WriteAllText(sentinel, "Pre-existing fictional recovery material");
        Assert.ThrowsAny<Exception>(() => CompanyBackup.Run(data, backups, 1, when));
        Assert.True(File.Exists(sentinel), "A failed backup must never remove an existing partial folder.");
        Assert.Equal("Pre-existing fictional recovery material", File.ReadAllText(sentinel));
    }

    [Fact]
    public void RetentionPreservesUnverifiedModifiedAndOtherCompanyBackups()
    {
        var data = Path.Combine(_root.Path, "data");
        var other = Path.Combine(_root.Path, "other");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(data, "fictional.txt"), "Current company");
        File.WriteAllText(Path.Combine(other, "fictional.txt"), "Other company");
        var when = DateTimeOffset.Parse("2026-10-20T03:00:00Z");
        var foreign = CompanyBackup.Run(other, backups, 365, when);
        var modified = CompanyBackup.Run(data, backups, 365, when.AddDays(1));
        File.WriteAllText(Path.Combine(modified.Folder, "fictional.txt"), "Changed after backup");
        var unrelated = Path.Combine(backups, "hirezero-backup-20261019-030000");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "Unrelated fictional notes");
        var result = CompanyBackup.Run(data, backups, 1, when.AddDays(2));
        Assert.Empty(result.Removed);
        Assert.True(Directory.Exists(foreign.Folder));
        Assert.Equal("Changed after backup", File.ReadAllText(Path.Combine(modified.Folder, "fictional.txt")));
        Assert.Equal("Unrelated fictional notes", File.ReadAllText(Path.Combine(unrelated, "keep.txt")));
        Assert.Equal(3, result.NeedsReview.Length);
    }

    [Fact]
    public void MalformedTraversalAndUnexpectedContentsAreNeverRetentionCandidates()
    {
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "fictional.txt"), "Fictional content");
        var when = DateTimeOffset.Parse("2026-10-20T03:00:00Z");
        var traversal = CompanyBackup.Run(data, backups, 365, when);
        var metadata = Path.Combine(traversal.Folder, "manifest.json");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("fictional.txt", "../outside.txt"));
        var duplicate = CompanyBackup.Run(data, backups, 365, when.AddDays(1));
        metadata = Path.Combine(duplicate.Folder, "manifest.json");
        File.WriteAllText(
            metadata,
            File.ReadAllText(metadata).Replace("\"source\":", "\"source\": \"ignored\", \"source\":")
        );
        var extra = CompanyBackup.Run(data, backups, 365, when.AddDays(2));
        File.WriteAllText(Path.Combine(extra.Folder, "unlisted.txt"), "Unlisted fictional material");
        var result = CompanyBackup.Run(data, backups, 1, when.AddDays(3));
        Assert.Empty(result.Removed);
        Assert.Equal(3, result.NeedsReview.Length);
        foreach (var folder in new[] { traversal.Folder, duplicate.Folder, extra.Folder })
            Assert.Equal("Fictional content", File.ReadAllText(Path.Combine(folder, "fictional.txt")));
    }

    [Fact]
    public void DestinationLeaseAndReservedManifestRefuseWithoutChangingExistingFiles()
    {
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(data, "fictional.txt"), "Fictional content");
        using (
            var lease = new FileStream(
                Path.Combine(backups, ".hirezero-backup.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            )
        )
        {
            Assert.Throws<IOException>(() => CompanyBackup.Run(data, backups, 1));
            Assert.Empty(Directory.GetDirectories(backups));
        }
        File.WriteAllText(Path.Combine(data, "manifest.json"), "An original company file");
        Assert.Throws<InvalidOperationException>(() => CompanyBackup.Run(data, backups, 1));
        Assert.Equal("An original company file", File.ReadAllText(Path.Combine(data, "manifest.json")));
        Assert.Empty(Directory.GetDirectories(backups));
    }

    [Fact]
    public void FailedNewCopyCleansOnlyItsOwnPartialAndLegacyVerifiedManifestsStillPrune()
    {
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "fictional.txt"), "Fictional content");
        var when = DateTimeOffset.Parse("2026-10-20T03:00:00Z");
        var old = CompanyBackup.Run(data, backups, 365, when);
        var metadata = Path.Combine(old.Folder, "manifest.json");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(metadata)))
            File.WriteAllText(
                metadata,
                JsonSerializer.Serialize(
                    new
                    {
                        created = manifest.RootElement.GetProperty("created"),
                        source = manifest.RootElement.GetProperty("source"),
                        files = manifest.RootElement.GetProperty("files"),
                    }
                )
            );
        File.WriteAllText(Path.Combine(data, "invalid.sqlite"), "Fictional invalid database");
        Assert.ThrowsAny<Exception>(() => CompanyBackup.Run(data, backups, 1, when.AddDays(1)));
        Assert.True(Directory.Exists(old.Folder));
        Assert.False(Directory.Exists(Path.Combine(backups, "hirezero-backup-20261021-030000.partial")));
        File.Delete(Path.Combine(data, "invalid.sqlite"));
        var result = CompanyBackup.Run(data, backups, 1, when.AddDays(2));
        Assert.Equal([Path.GetFileName(old.Folder)], result.Removed);
        Assert.Empty(result.NeedsReview);
    }

    [Fact]
    public void FailedRetentionReportsManualReviewAndKeepsTheManifest()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows sharing modes make a refused deletion deterministic.
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "fictional.txt"), "Fictional content");
        var when = DateTimeOffset.Parse("2026-10-20T03:00:00Z");
        var old = CompanyBackup.Run(data, backups, 1, when);
        using var locked = new FileStream(
            Path.Combine(old.Folder, "fictional.txt"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        var result = CompanyBackup.Run(data, backups, 1, when.AddDays(1));
        Assert.Empty(result.Removed);
        Assert.Contains(Path.GetFileName(old.Folder), result.NeedsReview);
        Assert.True(File.Exists(Path.Combine(old.Folder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.Folder, "fictional.txt")));
    }

    [Fact]
    public async Task DirectoryLinksCannotImportOrEraseOutsideFiles()
    {
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        var outside = Path.Combine(_root.Path, "outside");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(data, "fictional.txt"), "Company content");
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "Outside fictional material");
        async Task Link(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(path, outside);
                return;
            }
            var start = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/c", "mklink", "/J", path, outside })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, process.ExitCode);
        }
        var sourceLink = Path.Combine(data, "linked");
        await Link(sourceLink);
        try
        {
            Assert.Throws<InvalidOperationException>(() => CompanyBackup.Run(data, backups, 1));
        }
        finally
        {
            Directory.Delete(sourceLink);
        }
        var targetLink = Path.Combine(_root.Path, "linked-target");
        await Link(targetLink);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                CompanyBackup.Run(data, Path.Combine(targetLink, "nested"), 1)
            );
        }
        finally
        {
            Directory.Delete(targetLink);
        }
        Directory.CreateDirectory(backups);
        var candidateLink = Path.Combine(backups, "hirezero-backup-20261019-030000");
        await Link(candidateLink);
        try
        {
            var result = CompanyBackup.Run(data, backups, 1, DateTimeOffset.Parse("2026-10-26T03:00:00Z"));
            Assert.Empty(result.Removed);
            Assert.Contains(Path.GetFileName(candidateLink), result.NeedsReview);
            Assert.Equal("Outside fictional material", File.ReadAllText(Path.Combine(outside, "keep.txt")));
            Assert.False(Directory.Exists(Path.Combine(outside, "nested")));
            Assert.False(File.Exists(Path.Combine(result.Folder, "linked", "keep.txt")));
        }
        finally
        {
            Directory.Delete(candidateLink);
        }
    }
}
