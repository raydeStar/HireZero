using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HireZero.Core;

namespace HireZero.Integrations.Git;

// Read-only Git inspection of a local checkout: explicit executable, sanitized environment, no hooks, fsmonitor or
// network, bounded hashing. Exposed as the git.checkout-* tools.
public sealed class GitCheckoutInspector(string gitExecutable)
{
    public async Task<CompanyRepositoryReview> ReviewAsync(string root, string revision, CancellationToken token)
    {
        if (revision.Length is not (40 or 64) || revision.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("Select an exact Git revision for review.");
        var before = await InspectAsync(root, token);
        root = before.Workspace;
        if (before.Revision != revision)
            throw new WorkspaceException("The checkout revision changed before diff review.");
        var temporary = Path.Combine(Path.GetTempPath(), "hirezero-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string tree;
            try
            {
                tree = new UTF8Encoding(false, true).GetString(
                    await ReadGit(root, 1024 * 1024, false, token, "ls-tree", "-r", "--full-tree", "-z", revision, "--")
                );
            }
            catch (DecoderFallbackException)
            {
                throw new WorkspaceException("Git baseline paths must use UTF-8.");
            }
            var entries = tree.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length > 2000)
                throw new WorkspaceException("Git baseline exceeds the bounded review scope.");
            var blobs = new Dictionary<string, string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
            );
            string Resolve(string name)
            {
                var full = Path.GetFullPath(Path.Combine(root, name));
                if (
                    Path.IsPathRooted(name)
                    || name.Replace('\\', '/').Split('/').Any(p => p is "" or "." or "..")
                    || !full.StartsWith(
                        root + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                    )
                )
                    throw new WorkspaceException("Git review path escaped its checkout.");
                return full;
            }
            foreach (var entry in entries)
            {
                var parts = entry.Split('\t', 2);
                var meta = parts[0].Split(' ');
                if (
                    parts.Length != 2
                    || meta.Length != 3
                    || meta[0] is not ("100644" or "100755")
                    || meta[1] != "blob"
                    || meta[2].Length != revision.Length
                    || meta[2].Any(c => !Uri.IsHexDigit(c))
                    || !blobs.TryAdd(parts[1], meta[2])
                )
                    throw new WorkspaceException("Git baseline contains unsupported or ambiguous entries.");
                Resolve(parts[1]);
            }
            var current = new Dictionary<string, string>(blobs.Comparer);
            var ignored = await IgnoredAsync(root, token);
            var stack = new Stack<string>();
            stack.Push(root);
            var visited = 0;
            while (stack.TryPop(out var folder))
                foreach (var file in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    if (folder == root && Path.GetFileName(file) == ".git" || Ignored(ignored, root, file))
                        continue;
                    if (++visited > 2000)
                        throw new WorkspaceException("Checkout exceeds the bounded review scope.");
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new WorkspaceException("Review cannot follow reparse paths.");
                    if ((attributes & FileAttributes.Directory) != 0)
                        stack.Push(file);
                    else
                        current.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), file);
                }
            var diffs = new List<CompanyFileDiff>();
            var bytesUsed = 0;
            foreach (var name in blobs.Keys.Concat(current.Keys).Distinct(blobs.Comparer).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var actual = current.TryGetValue(name, out var file)
                    ? await ReadFile(file, 20 * 1024 * 1024, token)
                    : [];
                var committed = blobs.TryGetValue(name, out var oid);
                if (
                    committed
                    && current.ContainsKey(name)
                    && BlobId(actual, revision.Length).Equals(oid, StringComparison.OrdinalIgnoreCase)
                )
                    continue;
                var original = committed
                    ? await ReadGit(root, 1024 * 1024, false, token, "cat-file", "blob", oid!)
                    : [];
                // Also unchanged: the committed text as Git checks it out under a CRLF eol attribute (each LF without a
                // preceding CR written as CRLF). Only reviewed paths are ever staged, so such a file is never committed.
                if (committed && current.ContainsKey(name) && HasCrlf(actual) && Crlf(original).SequenceEqual(actual))
                    continue;
                if (diffs.Count >= 50)
                    throw new WorkspaceException("Use at most 50 changed files in the coding pilot.");
                try
                {
                    var utf8 = new UTF8Encoding(false, true);
                    utf8.GetString(actual);
                    utf8.GetString(original);
                }
                catch (DecoderFallbackException)
                {
                    throw new WorkspaceException("Binary changes need a separate review path.");
                }
                if (actual.Contains((byte)0) || original.Contains((byte)0))
                    throw new WorkspaceException("Binary changes need a separate review path.");
                var left = Path.Combine(temporary, "before");
                var right = Path.Combine(temporary, "after");
                await File.WriteAllBytesAsync(left, original, token);
                // A checkout converted to CRLF from LF text is compared as LF, so only real edits fill the patch.
                await File.WriteAllBytesAsync(
                    right,
                    !HasCrlf(original) && HasCrlf(actual) ? Lf(actual) : actual,
                    token
                );
                // Compare raw files outside the checkout. Project attributes/clean filters never run.
                var patch = await ReadGit(
                    temporary,
                    65536 - bytesUsed,
                    true,
                    token,
                    "diff",
                    "--no-index",
                    "--no-ext-diff",
                    "--no-textconv",
                    "--",
                    "before",
                    "after"
                );
                bytesUsed += patch.Length;
                diffs.Add(
                    new(
                        name,
                        !blobs.ContainsKey(name) ? "added"
                            : !current.ContainsKey(name) ? "deleted"
                            : "modified",
                        Encoding.UTF8.GetString(patch),
                        Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
                        Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant()
                    )
                );
            }
            var after = await InspectAsync(root, token);
            if (before.Fingerprint != after.Fingerprint || after.Revision != revision)
                throw new WorkspaceException("Checkout changed while its diff was captured.");
            var review = new CompanyRepositoryReview(revision, after.Fingerprint, [.. diffs]);
            return review with { Digest = CompanyJson.Digest(review) };
        }
        finally
        {
            // Only fixed files in this fresh, task-owned direct child of the temporary directory.
            foreach (var name in new[] { "before", "after" })
                File.Delete(Path.Combine(temporary, name));
            Directory.Delete(temporary);
        }
    }

    private async Task<byte[]> ReadGit(
        string cwd,
        int limit,
        bool differences,
        CancellationToken token,
        params string[] arguments
    )
    {
        if (limit < 1)
            throw new WorkspaceException("Repository diff exceeds its 64 KiB review allowance.");
        var start = new ProcessStartInfo(gitExecutable)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach (var key in new[] { "SystemRoot", "WINDIR", "ComSpec", "PATHEXT", "PATH", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(key) is { } value)
                start.Environment[key] = value;
        var disabled = Path.Combine(Path.GetTempPath(), "hirezero-no-config-" + Guid.NewGuid().ToString("N"));
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(disabled, "global");
        start.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (
            var arg in new[]
            {
                "--no-optional-locks",
                "--no-replace-objects",
                "-c",
                "core.fsmonitor=false",
                "-c",
                "core.hooksPath=" + Path.Combine(disabled, "hooks"),
                "-c",
                "core.attributesFile=" + Path.Combine(disabled, "attributes"),
            }.Concat(arguments)
        )
            start.ArgumentList.Add(arg);
        using var process =
            Process.Start(start) ?? throw new WorkspaceException("Read-only diff inspection did not start.");
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + read > limit)
                    throw new WorkspaceException("Repository diff or baseline exceeds its bounded allowance.");
                output.Write(buffer, 0, read);
            }
            await process.WaitForExitAsync(token);
            await error;
            if (process.ExitCode != 0 && !(differences && process.ExitCode == 1))
                throw new WorkspaceException("Read-only diff inspection failed.");
            return output.ToArray();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    // Untracked paths the checkout's own ignore rules exclude (build output such as obj/), whole folders collapsed. They
    // are never staged, so neither the fingerprint nor the review covers them; tracked files are never in this list.
    private async Task<HashSet<string>> IgnoredAsync(string root, CancellationToken token)
    {
        var listed = Encoding.UTF8.GetString(
            await ReadGit(
                root,
                1024 * 1024,
                false,
                token,
                "ls-files",
                "--others",
                "--ignored",
                "--exclude-standard",
                "--directory",
                "-z"
            )
        );
        return new HashSet<string>(
            listed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(p => p.TrimEnd('/')),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );
    }

    private static bool Ignored(HashSet<string> ignored, string root, string path) =>
        ignored.Count > 0 && ignored.Contains(Path.GetRelativePath(root, path).Replace('\\', '/'));

    private static string BlobId(byte[] content, int length)
    {
        using var hash =
            length == 40
                ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1)
                : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + content.Length + "\0"));
        hash.AppendData(content);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool HasCrlf(byte[] content) => content.AsSpan().IndexOf("\r\n"u8) >= 0;

    // Git's LF-to-CRLF checkout conversion: each LF not already preceded by CR becomes CRLF.
    private static byte[] Crlf(byte[] content)
    {
        var output = new List<byte>(content.Length + content.Length / 32);
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == (byte)'\n' && (i == 0 || content[i - 1] != (byte)'\r'))
                output.Add((byte)'\r');
            output.Add(content[i]);
        }
        return [.. output];
    }

    // The inverse for text with no CRLF of its own: each CRLF becomes LF.
    private static byte[] Lf(byte[] content)
    {
        var output = new List<byte>(content.Length);
        for (var i = 0; i < content.Length; i++)
            if (!(content[i] == (byte)'\r' && i + 1 < content.Length && content[i + 1] == (byte)'\n'))
                output.Add(content[i]);
        return [.. output];
    }

    private static async Task<byte[]> ReadFile(string path, int limit, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (file.Length > limit)
            throw new WorkspaceException("Review file exceeds its allowance.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await file.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + read > limit)
                throw new WorkspaceException("Review file exceeds its allowance.");
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }

    public async Task<CompanyRepositorySnapshot> InspectAsync(string root, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(gitExecutable) || !File.Exists(gitExecutable))
            throw new WorkspaceException("An explicit Git executable is required.");
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(root))
            throw new WorkspaceException("Checkout does not exist.");
        for (var p = new DirectoryInfo(root); p is not null; p = p.Parent)
            if ((p.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException("Checkout aliases through reparse points are refused.");
        async Task<string> Git(params string[] args)
        {
            return Encoding.UTF8.GetString(await ReadGit(root, 65536, false, token, args)).Trim();
        }
        var top = Path.GetFullPath(await Git("rev-parse", "--show-toplevel")).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(root, top, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("Use the exact checkout root.");
        var revision = await Git("rev-parse", "HEAD");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var stack = new Stack<string>();
        stack.Push(root);
        long total = 0;
        int count = 0;
        var files = new List<string>();
        var ignored = await IgnoredAsync(root, token);
        while (stack.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (directory == root && Path.GetFileName(entry) == ".git" || Ignored(ignored, root, entry))
                    continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new WorkspaceException("Checkout contains an unapproved symlink or reparse path.");
                if ((attributes & FileAttributes.Directory) != 0)
                    stack.Push(entry);
                else
                    files.Add(entry);
                if (files.Count + stack.Count > 2000)
                    throw new WorkspaceException("Use a bounded checkout for the execution pilot.");
            }
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            total += new FileInfo(file).Length;
            if (total > 20 * 1024 * 1024 || ++count > 2000)
                throw new WorkspaceException("Checkout inspection exceeds its bounded pilot scope.");
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/')));
            hash.AppendData([0]);
            hash.AppendData(SHA256.HashData(await ReadFile(file, 20 * 1024 * 1024, token)));
        }
        // Detect a concurrent ref change during fingerprinting; content is rechecked immediately before dispatch.
        if (revision != await Git("rev-parse", "HEAD"))
            throw new WorkspaceException("Repository changed during inspection.");
        return new(root, revision, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}
