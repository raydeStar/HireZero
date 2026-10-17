using HireZero.Roles.Developer;

public static class RepositoryCheckoutResolverChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "repository-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var remotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var worktrees = new Dictionary<string, string[]>();
        var unreadable = new HashSet<string>();
        var commands = new List<string>();
        var clones = 0;
        var failClone = false;
        var raceClone = false;
        string Fixture(string name, string remote, bool gitFile = false)
        {
            var path = Path.GetFullPath(Path.Combine(root, name));
            Directory.CreateDirectory(path);
            if (gitFile)
                File.WriteAllText(Path.Combine(path, ".git"), "gitdir: synthetic metadata");
            else
                Directory.CreateDirectory(Path.Combine(path, ".git"));
            remotes[path] = remote;
            File.WriteAllText(Path.Combine(path, ".synthetic-remote"), remote);
            return path;
        }
        Task<string> Git(string path, string[] args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            commands.Add(string.Join(" ", args));
            if (unreadable.Contains(path))
                throw new ReviewAccessException("Synthetic unreadable repository", 503);
            if (args[0] == "clone")
            {
                clones++;
                var destination = args[^1];
                Fixture(Path.GetFileName(destination), args[^2]);
                if (failClone)
                    throw new ReviewAccessException(
                        RepositoryCheckoutResolver.CloneFailure("Authentication failed with withheld credentials"),
                        503
                    );
                if (raceClone)
                    Directory.CreateDirectory(Path.Combine(root, "owner.race"));
                return Task.FromResult("");
            }
            return Task.FromResult(
                args[0] switch
                {
                    "rev-parse" => args[1] == "--show-toplevel" ? path : Path.Combine(path, ".git"),
                    "config" => remotes.GetValueOrDefault(path)
                        ?? File.ReadAllText(Path.Combine(path, ".synthetic-remote")),
                    "worktree" => string.Join("\n", worktrees.GetValueOrDefault(path, []).Select(p => "worktree " + p)),
                    _ => throw new InvalidOperationException("Unexpected synthetic discovery command"),
                }
            );
        }
        async Task<bool> Refuses(RepositoryCheckoutResolver resolver, string repository, string[]? registered = null)
        {
            try
            {
                await resolver.ResolveAsync(repository, registered ?? [], default);
                return false;
            }
            catch (ReviewAccessException)
            {
                return true;
            }
        }
        try
        {
            // Broad discovery/clone tests use a fake filesystem capability; native failure/race is tested separately below.
            var resolver = new RepositoryCheckoutResolver(root, Git, _ => new FakeDirectoryLease());
            var dirty = Fixture("different-folder-name", "git@github.com:Owner/Example.git");
            File.WriteAllText(Path.Combine(dirty, "dirty.txt"), "preserve this edit");
            check(
                await resolver.ResolveAsync("owner/example", [], default) == dirty,
                "Exact SSH remote discovers a differently named dirty checkout."
            );
            check(
                File.ReadAllText(Path.Combine(dirty, "dirty.txt")) == "preserve this edit" && clones == 0,
                "Discovery never changes local edits or clones a matching repository."
            );
            check(
                commands.All(command =>
                    !new[] { "checkout", "reset", "clean", "fetch", "status" }.Any(command.StartsWith)
                ),
                "Discovery only reads bounded Git metadata; no source checkout or repository hooks/tests."
            );
            check(
                await resolver.ResolveAsync("OWNER/EXAMPLE", [dirty], default) == dirty && clones == 0,
                "Registered existing checkout and repeat discovery are idempotent."
            );
            var linked = Fixture("linked-tree", "ssh://git@github.com/owner/linked", gitFile: true);
            check(
                await resolver.ResolveAsync("owner/linked", [linked], default) == linked,
                "A linked Git-file worktree is accepted after exact remote verification."
            );
            var parent = Fixture("wrong-origin", "https://github.com/another/example.git");
            var nested = Fixture("nested-worktrees/one", "https://github.com/owner/nested.git", gitFile: true);
            worktrees[parent] = [nested];
            check(
                await resolver.ResolveAsync("owner/nested", [], default) == nested,
                "Bounded registered worktree enumeration validates identity rather than the parent folder name."
            );
            check(
                await Refuses(resolver, "owner/../escape") && await Refuses(resolver, "https://evil.invalid/o/r"),
                "Traversal and arbitrary URLs cannot become clone targets."
            );
            check(clones == 0, "Rejected targets never dispatch a clone.");
            Fixture("owner.collision", "https://github.com/wrong/collision.git");
            check(
                await Refuses(resolver, "owner/collision") && clones == 0,
                "Wrong-remote destination collision is preserved, never overwritten."
            );
            var cloned = await resolver.ResolveAsync("owner/new", [], default);
            check(
                Path.GetDirectoryName(cloned) == root && Path.GetFileName(cloned) == "owner.new",
                "Missing approved repository clones only under the canonical repositories folder."
            );
            check(
                clones == 1
                    && commands.Any(command =>
                        command.Contains("--no-checkout --no-recurse-submodules -- https://github.com/owner/new.git")
                    ),
                "Clone has a fixed trusted host, no checkout and no recursive submodules."
            );
            check(
                await resolver.ResolveAsync("owner/new", [], default) == cloned && clones == 1,
                "A verified completed clone is reused without duplicate network actions."
            );
            failClone = true;
            check(
                await Refuses(resolver, "owner/denied") && clones == 2,
                "Failed clone does not return a path or dispatch repair work."
            );
            failClone = false;
            var retried = await resolver.ResolveAsync("owner/denied", [], default);
            check(
                clones == 3 && Path.GetFileName(retried) == "owner.denied",
                "A failed partial staging clone is preserved and never silently accepted on retry."
            );
            raceClone = true;
            check(
                await Refuses(resolver, "owner/race")
                    && Directory.Exists(Path.Combine(root, "owner.race"))
                    && !Directory.EnumerateFileSystemEntries(Path.Combine(root, "owner.race")).Any(),
                "Atomic final rename refuses a racing destination without overwriting it."
            );
            check(
                Directory.EnumerateDirectories(root, ".hirezero-clone-*", SearchOption.TopDirectoryOnly).Count() == 2,
                "Failed and collided staging clones remain available for manual inspection."
            );
            check(
                RepositoryCheckoutResolver.CloneFailure("Authentication failed secret-value").Contains("authenticate")
                    && !RepositoryCheckoutResolver
                        .CloneFailure("Authentication failed secret-value")
                        .Contains("secret-value"),
                "Authentication error is classified without copying credential-bearing stderr."
            );
            check(
                RepositoryCheckoutResolver.CloneFailure("Could not resolve host").Contains("reach GitHub")
                    && RepositoryCheckoutResolver.CloneFailure("repository not found").Contains("missing or hidden"),
                "Network and missing repository outcomes are distinct curated errors."
            );
            var absent = new RepositoryCheckoutResolver(Path.Combine(root, "absent"), Git);
            check(
                await Refuses(absent, "owner/example"),
                "Discovery never invents or creates a missing repositories root."
            );
            unreadable.Add(parent);
            check(
                await resolver.ResolveAsync("owner/example", [parent, dirty], default) == dirty,
                "An unrelated unreadable checkout does not block a later verified exact local match."
            );
            var cloneCount = clones;
            check(
                await Refuses(resolver, "owner/incomplete") && clones == cloneCount,
                "An incomplete discovery scan cannot dispatch a clone or infer repository absence."
            );
            var boundaryRoot = Path.Combine(root, "native-boundary");
            Directory.CreateDirectory(boundaryRoot);
            var relocated = Path.Combine(root, "native-relocated");
            var cloneStarts = 0;
            var replacementBlocked = false;
            Task<string> NativeGit(string path, string[] arguments, CancellationToken cancellation)
            {
                if (arguments[0] != "clone")
                    throw new InvalidOperationException("Native fixture unexpected metadata command");
                cloneStarts++;
                try
                {
                    Directory.Move(boundaryRoot, relocated);
                }
                catch (IOException)
                {
                    replacementBlocked = true;
                }
                catch (UnauthorizedAccessException)
                {
                    replacementBlocked = true;
                }
                if (!replacementBlocked)
                {
                    Directory.Move(relocated, boundaryRoot);
                    throw new InvalidOperationException("Native directory lock did not prevent replacement");
                }
                throw new ReviewAccessException("Synthetic clone stops after proving replacement blocked", 503);
            }
            check(
                await Refuses(new RepositoryCheckoutResolver(boundaryRoot, NativeGit), "owner/native"),
                "Native clone must refuse an unavailable lock or preserve its acquired pathname lock."
            );
            check(
                cloneStarts == 0 || replacementBlocked,
                "A native root replacement cannot occur before or during clone; a lock failure never dispatches Git."
            );
            check(!Directory.Exists(relocated), "No native-race fixture is relocated outside its original pathname.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeDirectoryLease : IDisposable
    {
        public void Dispose() { }
    }
}
