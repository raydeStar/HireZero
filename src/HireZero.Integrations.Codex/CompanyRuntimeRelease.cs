namespace HireZero.Integrations.Codex;

// Release only a run-owned scratch pair, after its process stop has been confirmed. A stubborn door stays
// marked for attention; the butler does not report the room empty while someone is still inside.
public static class CompanyRuntimeRelease
{
    public static CodingReleaseReceipt Complete(string root, string scratch, Action stop)
    {
        var resource = scratch.Replace('\\', '/').Split('/').LastOrDefault();
        if (
            resource is null
            || resource.Length != 39
            || !resource.StartsWith("coding-", StringComparison.Ordinal)
            || !resource[7..].All(Uri.IsHexDigit)
        )
            resource = null;
        CodingReleaseReceipt Receipt(bool stopped, bool removed, string status) =>
            new(stopped, removed, status, DateTimeOffset.UtcNow, resource);
        try
        {
            stop();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return Receipt(false, false, "termination-unconfirmed");
        }
        try
        {
            var parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(path);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (
                !string.Equals(Path.GetDirectoryName(path), parent, comparison)
                || !name.StartsWith("coding-", StringComparison.Ordinal)
                || name.Length != 39
                || !name[7..].All(Uri.IsHexDigit)
            )
                return Receipt(true, false, "cleanup-scope-refused");
            var targets = new[] { path, path + "-inputs" };
            // Verify all targets and their contents before deleting either. Never traverse a link or junction.
            foreach (var target in targets)
            {
                if (
                    Attributes(target) is { } attributes
                    && (
                        !attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)
                    )
                )
                    return Receipt(true, false, "cleanup-scope-refused");
                for (var directory = new DirectoryInfo(target); directory is not null; directory = directory.Parent)
                    if (Attributes(directory.FullName) is { } ancestor && ancestor.HasFlag(FileAttributes.ReparsePoint))
                        return Receipt(true, false, "cleanup-scope-refused");
                if (Directory.Exists(target) && ContainsLink(new DirectoryInfo(target)))
                    return Receipt(true, false, "cleanup-scope-refused");
            }
            foreach (var target in targets)
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
            return Receipt(true, true, "released");
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Receipt(true, false, "cleanup-incomplete");
        }
    }

    private static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool ContainsLink(DirectoryInfo root)
    {
        var seen = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (++seen > 50_000 || watch.Elapsed > TimeSpan.FromSeconds(3))
                    throw new IOException("The temporary tree exceeds this cleanup pass's inspection bound.");
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return true;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
            }
        return false;
    }
}
