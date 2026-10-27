using System.Globalization;
using System.Text.Json;

public static partial class CompanyBackup
{
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record VerifiedBackup(string Folder, string[] Files, string[] Directories);

    private static void RequireNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(
                        "Backup paths must not contain symbolic links, junctions or other reparse points."
                    );
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string[] TreeEntries(string root)
    {
        RequireNoLinks(root);
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            RequireNoLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RequireNoLinks(entry);
                entries.Add(entry);
                if (Directory.Exists(entry))
                    pending.Push(entry);
            }
        }
        return entries.ToArray();
    }

    private static void CleanOwnedPartial(string partial, List<string> ownedFiles)
    {
        try
        {
            RequireNoLinks(partial);
            foreach (var file in ownedFiles)
            {
                if (!IsWithin(file, partial))
                    return;
                RequireNoLinks(file);
                File.Delete(file);
            }
            foreach (var directory in TreeEntries(partial).Where(Directory.Exists).OrderByDescending(p => p.Length))
                Directory.Delete(directory, recursive: false);
            Directory.Delete(partial, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Preserve anything unexpected for the owner; never turn a copy failure into recursive data loss.
        }
    }

    private static (string[] Removed, string[] Preserved) PruneVerifiedBackups(string target, string source, int keep)
    {
        var removed = new List<string>();
        var preserved = new HashSet<string>(PathComparer);
        var verified = new List<VerifiedBackup>();
        foreach (
            var candidate in Directory
                .EnumerateDirectories(target, Prefix + "*")
                .Where(p => !p.EndsWith(".partial", StringComparison.Ordinal))
        )
        {
            if (VerifyBackup(candidate, source) is { } backup)
                verified.Add(backup);
            else
                preserved.Add(Path.GetFileName(candidate));
        }
        foreach (
            var old in verified.OrderByDescending(b => Path.GetFileName(b.Folder), StringComparer.Ordinal).Skip(keep)
        )
        {
            try
            {
                // Verification is repeated at deletion, not merely trusted from the earlier directory listing.
                if (VerifyBackup(old.Folder, source) is not { } current)
                {
                    preserved.Add(Path.GetFileName(old.Folder));
                    continue;
                }
                foreach (var file in current.Files)
                {
                    RequireNoLinks(file);
                    if (!IsWithin(file, old.Folder))
                        throw new InvalidOperationException("Backup cleanup escaped its folder.");
                    File.Delete(file);
                }
                foreach (var directory in current.Directories.OrderByDescending(p => p.Length))
                {
                    RequireNoLinks(directory);
                    Directory.Delete(directory, recursive: false);
                }
                RequireNoLinks(old.Folder);
                Directory.Delete(old.Folder, recursive: false);
                removed.Add(Path.GetFileName(old.Folder));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                preserved.Add(Path.GetFileName(old.Folder));
            }
        }
        return (removed.ToArray(), preserved.Order(StringComparer.Ordinal).ToArray());
    }

    private static VerifiedBackup? VerifyBackup(string folder, string source)
    {
        try
        {
            RequireNoLinks(folder);
            var name = Path.GetFileName(folder);
            if (
                !name.StartsWith(Prefix, StringComparison.Ordinal)
                || !DateTime.TryParseExact(
                    name[Prefix.Length..],
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                )
            )
                return null;
            var manifest = Path.Combine(folder, "manifest.json");
            RequireNoLinks(manifest);
            using var input = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 1 or > 16 * 1024 * 1024)
                return null;
            var bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            if (input.ReadByte() != -1)
                return null;
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = UniqueProperties(document.RootElement, ["schema", "created", "source", "files"]);
            if (root.TryGetValue("schema", out var schema) && schema.GetInt32() != 1)
                return null;
            var manifestSource = root["source"].GetString();
            if (manifestSource is null || !Path.IsPathFullyQualified(manifestSource))
                return null;
            if (!PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(manifestSource)), source))
                return null;
            if (
                !DateTimeOffset.TryParseExact(
                    root["created"].GetString(),
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var created
                )
                || Prefix + created.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) != name
            )
                return null;
            var list = root["files"];
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 100000)
                return null;
            var files = new HashSet<string>(PathComparer) { manifest };
            var directories = new HashSet<string>(PathComparer);
            foreach (var entry in list.EnumerateArray())
            {
                var fields = UniqueProperties(entry, ["path", "bytes", "sha256"]);
                var relative = fields["path"].GetString();
                if (
                    string.IsNullOrEmpty(relative)
                    || relative.Length > 32767
                    || relative.Contains('\\')
                    || relative.Contains(':')
                    || Path.IsPathRooted(relative)
                    || relative.Split('/').Any(p => p is "" or "." or "..")
                )
                    return null;
                var file = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithin(file, folder) || !files.Add(file))
                    return null;
                RequireNoLinks(file);
                var length = fields["bytes"].GetInt64();
                if (length < 0 || !File.Exists(file) || new FileInfo(file).Length != length)
                    return null;
                var digest = fields["sha256"].GetString();
                if (
                    digest is null
                    || digest.Length != 64
                    || !digest.All(Uri.IsHexDigit)
                    || !Sha256(file).Equals(digest, StringComparison.OrdinalIgnoreCase)
                )
                    return null;
                for (
                    var parent = Path.GetDirectoryName(file);
                    parent is not null && !PathComparer.Equals(parent, folder);
                    parent = Path.GetDirectoryName(parent)
                )
                    directories.Add(parent);
            }
            var actual = TreeEntries(folder);
            if (
                !files.SetEquals(actual.Where(p => !Directory.Exists(p)))
                || !directories.SetEquals(actual.Where(Directory.Exists))
            )
                return null;
            // Keep the manifest until every data file is gone, so a failed cleanup still has its inventory.
            return new(
                folder,
                files.Where(p => !PathComparer.Equals(p, manifest)).Append(manifest).ToArray(),
                directories.ToArray()
            );
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or JsonException
                        or ArgumentException
                        or KeyNotFoundException
                        or FormatException
                        or OverflowException
            )
        {
            return null;
        }
    }

    private static Dictionary<string, JsonElement> UniqueProperties(JsonElement value, string[] allowed)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name) || !result.TryAdd(property.Name, property.Value))
                throw new InvalidOperationException("Unrecognized or duplicate backup manifest property.");
        return result;
    }
}
