using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// A local backup of a company data root (docs/adr/0044-local-company-backups.md): each database through SQLite's
// online backup, so it is consistent while the company runs; the other files (Data Protection keys, app-entered
// secrets, integration settings, the audit key) copied as they are; a manifest with SHA-256 digests; an integrity
// check of every copied database; and only the newest backups kept. Nothing leaves the machine. Keys stay
// DPAPI-protected, so only the same Windows user can restore the secrets.
public static class CompanyBackup
{
    public const string Flag = "--company-backup";
    public const string Prefix = "hirezero-backup-";

    public sealed record Result(string Folder, int Databases, int Files, string[] Removed);

    public static Result Run(string dataRoot, string destination, int keep, DateTimeOffset? now = null)
    {
        var source = Path.GetFullPath(dataRoot);
        var target = Path.GetFullPath(destination);
        if (!Directory.Exists(source))
            throw new InvalidOperationException("The data root does not exist: " + source);
        if (IsWithin(target, source) || IsWithin(source, target))
            throw new InvalidOperationException("Keep backups outside the data root, and the data root outside them.");
        if (keep is < 1 or > 365)
            throw new InvalidOperationException("Keep between 1 and 365 backups.");

        var folder = Path.Combine(target, Prefix + (now ?? DateTimeOffset.Now).ToString("yyyyMMdd-HHmmss"));
        if (Directory.Exists(folder))
            throw new InvalidOperationException("A backup with this time already exists: " + folder);
        var partial = folder + ".partial";
        Directory.CreateDirectory(partial);
        var manifest = new List<object>();
        var databases = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order())
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
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                if (file.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    Backup(file, copy);
                    databases++;
                }
                else
                    File.Copy(file, copy);
                manifest.Add(
                    new
                    {
                        path = relative.Replace('\\', '/'),
                        bytes = new FileInfo(copy).Length,
                        sha256 = Sha256(copy),
                    }
                );
            }
            File.WriteAllText(
                Path.Combine(partial, "manifest.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        created = (now ?? DateTimeOffset.Now).ToString("O"),
                        source,
                        files = manifest,
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
            Directory.Move(partial, folder);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(partial, true);
            throw;
        }

        // Only complete backups count; the newest are kept.
        var removed = Directory
            .EnumerateDirectories(target, Prefix + "*")
            .Where(d => !d.EndsWith(".partial", StringComparison.Ordinal))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .Skip(keep)
            .ToArray();
        foreach (var old in removed)
            Directory.Delete(old, true);
        return new(folder, databases, manifest.Count, [.. removed.Select(Path.GetFileName).OfType<string>()]);
    }

    private static void Backup(string from, string to)
    {
        using (var source = new SqliteConnection($"Data Source={from};Mode=ReadOnly;Pooling=False"))
        using (var copy = new SqliteConnection($"Data Source={to};Pooling=False"))
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
            StringComparison.OrdinalIgnoreCase
        );
}
