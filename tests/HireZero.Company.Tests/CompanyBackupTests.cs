using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

// Local backups (docs/adr/0044-local-company-backups.md): a consistent copy of each database and the key and secret
// files, a manifest, no locks or side files, and only the newest backups kept. Fictional data.
public sealed class CompanyBackupTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-backup-");

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    [Fact]
    public void ABackupCopiesTheCompanyConsistentlyAndKeepsOnlyTheNewest()
    {
        var data = Path.Combine(_root.Path, "data");
        var backups = Path.Combine(_root.Path, "backups");
        Directory.CreateDirectory(Path.Combine(data, "keys"));
        var store = new CompanyStore(
            Path.Combine(data, "workspace.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions()
        );
        store.Apply(
            new CompanyCommand(Guid.NewGuid().ToString("D"), "department.create") { Name = "Fictional studio" }
        );
        File.WriteAllText(Path.Combine(data, "keys", "key-fictional.xml"), "<key />");
        File.WriteAllText(Path.Combine(data, "company-scheduler.lock"), "");
        var first = DateTimeOffset.Parse("2026-10-20T03:00:00Z");

        var one = CompanyBackup.Run(data, backups, keep: 2, now: first);
        Assert.Equal(1, one.Databases);
        Assert.False(File.Exists(Path.Combine(one.Folder, "company-scheduler.lock")));
        Assert.True(File.Exists(Path.Combine(one.Folder, "keys", "key-fictional.xml")));
        using (
            var copy = new SqliteConnection($"Data Source={Path.Combine(one.Folder, "workspace.sqlite")};Pooling=False")
        )
        {
            copy.Open();
            using var count = copy.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM co_events";
            Assert.True((long)count.ExecuteScalar()! > 0);
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(one.Folder, "manifest.json")));
        var listed = manifest
            .RootElement.GetProperty("files")
            .EnumerateArray()
            .Select(f => f.GetProperty("path").GetString());
        Assert.Equal(one.Files, listed.Count());
        Assert.Contains("keys/key-fictional.xml", listed);
        Assert.DoesNotContain("company-scheduler.lock", listed);

        CompanyBackup.Run(data, backups, keep: 2, now: first.AddDays(1));
        var three = CompanyBackup.Run(data, backups, keep: 2, now: first.AddDays(2));
        Assert.Equal([Path.GetFileName(one.Folder)], three.Removed);
        Assert.Equal(2, Directory.GetDirectories(backups).Length);

        // Backups never go inside the data root.
        Assert.Throws<InvalidOperationException>(() => CompanyBackup.Run(data, Path.Combine(data, "b"), keep: 2));
    }
}
