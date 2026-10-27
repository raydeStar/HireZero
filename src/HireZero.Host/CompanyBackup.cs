using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// A local backup of a company data root (docs/adr/0044-local-company-backups.md): each database through SQLite's
// online backup, so it is consistent while the company runs; the other files (Data Protection keys, app-entered
// secrets, integration settings, the audit key) copied as they are; a manifest with SHA-256 digests; an integrity
// check of every copied database; and only the newest backups kept. Nothing leaves the machine. Keys stay
// DPAPI-protected, so only the same Windows user can restore the secrets.
public static partial class CompanyBackup
{
    public const string Flag = "--company-backup";
    public const string Prefix = "hirezero-backup-";

    public sealed record Result(string Folder, int Databases, int Files, string[] Removed, string[] NeedsReview);

    public static Result Run(string dataRoot, string destination, int keep, DateTimeOffset? now = null)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        RequireNoLinks(source);
        RequireNoLinks(target);
        if (!Directory.Exists(source))
            throw new InvalidOperationException("The data root does not exist: " + source);
        if (IsWithin(target, source) || IsWithin(source, target))
            throw new InvalidOperationException("Keep backups outside the data root, and the data root outside them.");
        if (keep is < 1 or > 365)
            throw new InvalidOperationException("Keep between 1 and 365 backups.");

        var files = TreeEntries(source).Where(p => !Directory.Exists(p)).Order().ToArray();
        if (files.Contains(Path.Combine(source, "manifest.json"), PathComparer))
            throw new InvalidOperationException(
                "The data root contains the reserved backup manifest filename. Nothing was copied."
            );
        Directory.CreateDirectory(target);
        RequireNoLinks(target);
        var lockPath = Path.Combine(target, ".hirezero-backup.lock");
        RequireNoLinks(lockPath);
        using var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var created = now ?? DateTimeOffset.Now;
        var folder = Path.Combine(
            target,
            Prefix + created.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
        );
        if (Path.Exists(folder))
            throw new InvalidOperationException("A backup with this time already exists: " + folder);
        var partial = folder + ".partial";
        RequireNoLinks(partial);
        if (Path.Exists(partial))
            throw new InvalidOperationException(
                "A partial backup with this time already exists. It was preserved; choose a new backup time."
            );
        Directory.CreateDirectory(partial);
        var manifest = new List<object>();
        var ownedFiles = new List<string>();
        var databases = 0;
        try
        {
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(source, file);
                // Locks and SQLite side files are not data: a database is copied whole by the online backup.
                if (
                    file.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith("-journal", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
                )
                    continue;
                var copy = Path.Combine(partial, relative);
                RequireNoLinks(file);
                RequireNoLinks(copy);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                if (file.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    using (new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    ownedFiles.Add(copy);
                    Backup(file, copy);
                    databases++;
                }
                else
                {
                    using var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    ownedFiles.Add(copy);
                    using var input = File.OpenRead(file);
                    input.CopyTo(output);
                }
                manifest.Add(
                    new
                    {
                        path = relative.Replace('\\', '/'),
                        bytes = new FileInfo(copy).Length,
                        sha256 = Sha256(copy),
                    }
                );
            }
            var manifestPath = Path.Combine(partial, "manifest.json");
            using (var output = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownedFiles.Add(manifestPath);
                JsonSerializer.Serialize(
                    output,
                    new
                    {
                        schema = 1,
                        created = created.ToString("O"),
                        source,
                        files = manifest,
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                );
            }
            RequireNoLinks(partial);
            Directory.Move(partial, folder);
        }
        catch
        {
            // Only this attempt's files belong to our broom. An interrupted predecessor is not ours to sweep.
            CleanOwnedPartial(partial, ownedFiles);
            throw;
        }

        var (removed, preserved) = PruneVerifiedBackups(target, source, keep);
        return new(folder, databases, manifest.Count, removed, preserved);
    }

    private static void Backup(string from, string to)
    {
        using (
            var source = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = from,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString()
            )
        )
        using (
            var copy = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = to, Pooling = false }.ToString()
            )
        )
        {
            source.Open();
            copy.Open();
            source.BackupDatabase(copy);
            using var check = copy.CreateCommand();
            check.CommandText = "PRAGMA integrity_check";
            if (check.ExecuteScalar() as string != "ok")
                throw new InvalidOperationException(
                    "The backup of " + Path.GetFileName(from) + " failed its integrity check."
                );
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static bool IsWithin(string path, string root) =>
        (path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(
            root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );
}
