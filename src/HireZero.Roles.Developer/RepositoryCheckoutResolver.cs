namespace HireZero.Roles.Developer;

// Discovery reads Git metadata only. Never checkout, reset, clean or run repository code here.
public sealed class RepositoryCheckoutResolver
{
    private readonly string root;
    private readonly Func<string, string[], CancellationToken, Task<string>> git;
    private readonly Func<string, IDisposable> _directoryLease;
    public RepositoryCheckoutResolver(string root, Func<string, string[], CancellationToken, Task<string>> git)
        : this(root, git, CloneDirectoryLease.Acquire) { }
    internal RepositoryCheckoutResolver(string root, Func<string, string[], CancellationToken, Task<string>> git, Func<string, IDisposable> directoryLease)
    { this.root = root; this.git = git; _directoryLease = directoryLease; }
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const int Limit = 256;

    public static string CloneFailure(string error) =>
        new[] { "Authentication failed", "could not read Username", "Permission denied", "HTTP 401", "HTTP 403", "returned error: 401", "returned error: 403" }
            .Any(value => error.Contains(value, StringComparison.OrdinalIgnoreCase))
        ? "Repository clone could not authenticate. Check the existing Git account and access; no repair was started."
        : error.Contains("not found", StringComparison.OrdinalIgnoreCase)
        ? "The requested repository is missing or hidden from the existing account. No repair was started."
        : new[] { "Could not resolve host", "Failed to connect", "timed out", "unable to access" }
            .Any(value => error.Contains(value, StringComparison.OrdinalIgnoreCase))
        ? "Repository clone could not reach GitHub. Check connectivity; no repair was started."
        : "Repository clone failed. Its destination was preserved for inspection; no repair was started.";

    public static void SafeLocalPath(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ReviewAccessException("Repository discovery cannot follow a junction or symbolic link.");
        var marker = Path.Combine(path, ".git");
        if ((File.Exists(marker) || Directory.Exists(marker)) && File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint))
            throw new ReviewAccessException("The Git metadata path cannot be a symbolic link.");
    }
    private bool Inside(string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, PathComparison);

    private async Task<bool> MatchesAsync(string path, string repository, string[] registered, CancellationToken token)
    {
        SafeLocalPath(path);
        if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, ".git")) && !Directory.Exists(Path.Combine(path, ".git"))) return false;
        var top = (await git(path, ["rev-parse", "--show-toplevel"], token)).Trim();
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(top)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), PathComparison)) return false;
        // Git files are valid linked-worktree markers. Validate the resolved common metadata path too.
        var common = (await git(path, ["rev-parse", "--git-common-dir"], token)).Trim();
        var metadata = Path.GetFullPath(common, path);
        SafeLocalPath(metadata);
        if (!Inside(metadata) && !registered.Any(allowed => metadata.StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowed)) + Path.DirectorySeparatorChar, PathComparison)))
            throw new ReviewAccessException("Linked Git metadata escapes the repositories folder and registered checkouts. Nothing was cloned.", 409);
        var remote = (await git(path, ["config", "--local", "--no-includes", "--get", "remote.origin.url"], token)).Trim();
        return string.Equals(GitHubReviewPolicy.RemoteRepository(remote), repository, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ResolveAsync(string repository, IEnumerable<string> registered, CancellationToken token)
    {
        if (!GitHubReviewPolicy.ValidRepository(repository)) throw new ReviewAccessException("Choose the approved GitHub repository in owner/name form.", 400);
        SafeLocalPath(root);
        if (!Directory.Exists(root)) throw new ReviewAccessException("The repositories folder is unavailable. Configure an existing repositories folder before retrying.", 503);
        using var scan = CancellationTokenSource.CreateLinkedTokenSource(token);
        scan.CancelAfter(TimeSpan.FromSeconds(20));
        var approvedPaths = registered.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(PathComparer).Take(Limit + 1).ToArray();
        var children = Directory.EnumerateDirectories(root).Where(path => !Path.GetFileName(path).StartsWith(".hirezero-clone-", StringComparison.Ordinal)).Take(Limit + 1).ToArray();
        if (children.Length > Limit) throw new ReviewAccessException("Repository discovery reached its folder limit. Connect the exact checkout in Code reviews.", 422);
        var candidates = approvedPaths.Concat(children).Distinct(PathComparer).Take(Limit + 1)
            .OrderByDescending(path => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)).Equals(repository.Split('/')[1], StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length > Limit) throw new ReviewAccessException("Repository discovery reached its registered-path limit. Connect the exact checkout in Code reviews.", 422);
        var remaining = Limit; var incomplete = false;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(candidate)) continue;
            if (--remaining < 0) throw new ReviewAccessException("Repository discovery reached its total path limit. Connect the exact checkout.", 422);
            try
            {
                if (await MatchesAsync(candidate, repository, approvedPaths, scan.Token)) return Path.GetFullPath(candidate);
                if (!File.Exists(Path.Combine(candidate, ".git")) && !Directory.Exists(Path.Combine(candidate, ".git"))) continue;
                var worktrees = (await git(candidate, ["worktree", "list", "--porcelain"], scan.Token)).Split('\n')
                    .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal)).Select(line => line[9..].TrimEnd('\r')).Take(Limit + 1).ToArray();
                if (worktrees.Length > Limit) throw new ReviewAccessException("Repository discovery reached its worktree limit.", 422);
                foreach (var worktree in worktrees)
                {
                    if (--remaining < 0) throw new ReviewAccessException("Repository discovery reached its total path limit. Connect the exact checkout.", 422);
                    if (Inside(worktree) && await MatchesAsync(worktree, repository, approvedPaths, scan.Token)) return Path.GetFullPath(worktree);
                }
            }
            catch (ReviewAccessException)
            {
                token.ThrowIfCancellationRequested();
                if (scan.IsCancellationRequested) throw new ReviewAccessException("Local repository discovery timed out. Connect the exact checkout; nothing was cloned.", 503);
                incomplete = true; // Keep looking for an exact match, but an incomplete scan never permits cloning.
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { incomplete = true; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new ReviewAccessException("Local repository discovery timed out. Connect the exact checkout; nothing was cloned.", 503); }
        }
        if (incomplete) throw new ReviewAccessException("Repository discovery could not verify every local path. Connect the checkout explicitly; nothing was cloned.", 503);
        using var rootLease = _directoryLease(root);
        // Deterministic name avoids collisions between organizations; never overwrite an existing destination.
        var destination = Path.Combine(Path.GetFullPath(root), repository.Replace('/', '.'));
        SafeLocalPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new ReviewAccessException("The clone destination already exists but is not a verified matching checkout. Nothing was overwritten; connect or reconcile it first.", 409);
        var drive = new DriveInfo(Path.GetPathRoot(destination)!);
        if (drive.AvailableFreeSpace < 11L * 1024 * 1024 * 1024)
            throw new ReviewAccessException("Cloning needs the 10 GiB disk reserve plus 1 GiB headroom.");
        // Stage a new clone, then atomically move it into the final name. A racing destination is never reused/overwritten.
        var staging = Path.Combine(Path.GetFullPath(root), ".hirezero-clone-" + repository.Replace('/', '.') + "-" + Guid.NewGuid().ToString("N"));
        SafeLocalPath(staging);
        Directory.CreateDirectory(staging);
        using (var stagingLease = _directoryLease(staging))
        {
        // Fixed GitHub HTTPS URL from the already authorized repository identity, never from PR text.
        await git(root, ["clone", "--template=", "--no-checkout", "--no-recurse-submodules", "--", "https://github.com/" + repository + ".git", staging], token);
        if (!await MatchesAsync(staging, repository, approvedPaths, token))
            throw new ReviewAccessException("The cloned repository identity could not be verified. No repair was started.", 409);
        }
        try { SafeLocalPath(destination); Directory.Move(staging, destination); }
        catch (IOException) { throw new ReviewAccessException("The clone destination collided with another checkout. The staged clone was preserved; nothing was overwritten.", 409); }
        if (!await MatchesAsync(destination, repository, approvedPaths, token))
            throw new ReviewAccessException("The final cloned repository identity could not be verified. No repair was started.", 409);
        return destination;
    }
}

public partial class GitHubReviewClient
{
    public virtual async Task<ReviewRepository> ResolveSourceAsync(ReviewRepository[] registered, PullRequestWorkflow workflow, CancellationToken token)
    {
        var existing = registered.FirstOrDefault(repo => repo.GitHubId == workflow.RepositoryGitHubId && repo.RootPath.Length > 0);
        if (existing is not null)
        {
            try { await InspectAsync(existing, null, false, token); return existing; }
            catch (ReviewAccessException) when (!Directory.Exists(existing.RootPath)) { token.ThrowIfCancellationRequested(); }
        }
        var root = Environment.GetEnvironmentVariable("HIREZERO_REPOSITORIES_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub");
        var resolver = new RepositoryCheckoutResolver(root, (path, args, cancellation) => CommandAsync("git", path,
            ["-c", "core.fsmonitor=false", "-c", "core.hooksPath=" + NoHooks, "-c", "protocol.ext.allow=never", "-c", "protocol.file.allow=never", .. args], null, cancellation));
        var resolved = await resolver.ResolveAsync(workflow.Repository, registered.Select(repo => repo.RootPath), token);
        return new ReviewRepository(Guid.NewGuid().ToString("N"), workflow.AccountId, workflow.RepositoryGitHubId,
            workflow.Repository, resolved, "github-source");
    }
}
