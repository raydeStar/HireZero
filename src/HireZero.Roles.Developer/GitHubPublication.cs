using System.Text.Json;

namespace HireZero.Roles.Developer;

public static class GitHubPublication
{
    public static bool SafePath(string path) => path is { Length: > 0 and <= 400 } && !path.StartsWith('/') &&
        !path.Contains('\\') && !path.Contains(':') && !path.Split('/').Any(p => p is "" or "." or ".." || p.Equals(".git", StringComparison.OrdinalIgnoreCase)) &&
        !path.StartsWith(".developer-workspace-evidence/", StringComparison.OrdinalIgnoreCase);
    public static void InspectPath(string root, string relative)
    {
        if (!SafePath(relative)) throw new ReviewAccessException("A changed file path is outside the approved code scope.");
        for (var path = Path.Combine(root, relative); path is not null; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new ReviewAccessException("Changed files must not traverse symbolic links or junctions.");
    }
    public static string[] ChangedFiles(JsonElement run)
    {
        if (!run.TryGetProperty("resultJson", out var result) || result.ValueKind != JsonValueKind.String) throw new ReviewAccessException("The worker did not return a structured result.");
        using var parsed = JsonDocument.Parse(result.GetString()!); var value = parsed.RootElement;
        if (value.GetProperty("questions").GetArrayLength() != 0) throw new ReviewAccessException("The worker still needs an answer before publication.");
        var files = value.GetProperty("changedFiles").EnumerateArray().Select(f => f.GetString()!)
            .Where(path=>!path.StartsWith(".developer-workspace-evidence/",StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > 40 || files.Any(p => !SafePath(p))) throw new ReviewAccessException("The worker must report one to forty exact, safe changed code paths.");
        return files;
    }
    // Proof: a real test command failed and later passed. A command that passed and then broke blocks publication.
    // A command that never passed in this run (an unavailable service, an accepted advisory) can't be judged here:
    // it doesn't block, and it is reported as unverified so the PR's own CI decides.
    public static bool RegressionProved(JsonElement run) => Evidence(run) is { Reproduced: true, Broken.Length: 0 };
    public static string[] Unverified(JsonElement run) => Evidence(run).Unverified;

    public static (bool Reproduced, string[] Broken, string[] Unverified) Evidence(JsonElement run)
    {
        if (!run.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) return (false, [], []);
        var failed = new HashSet<string>(StringComparer.Ordinal); var passed = new HashSet<string>(StringComparer.Ordinal);
        var final = new Dictionary<string,int>(StringComparer.Ordinal); var reproduced = false;
        foreach (var item in events.EnumerateArray())
        {
            if (!item.TryGetProperty("eventType", out var kind)) item.TryGetProperty("type", out kind);
            if (kind.GetString() != "command-completed" || !item.TryGetProperty("dataJson", out var data) || data.ValueKind != JsonValueKind.String) continue;
            try
            {
                using var parsed = JsonDocument.Parse(data.GetString()!); var value = parsed.RootElement;
                if (!value.TryGetProperty("command", out var command) || !value.TryGetProperty("exitCode", out var exit) || exit.ValueKind != JsonValueKind.Number) continue;
                var text = ValidationCommand(command.GetString() ?? "");
                // Only real test/build runner commands count, never narrative claims or echo commands.
                if (text is null) continue;
                final[text] = exit.GetInt32();
                if (exit.GetInt32() != 0) failed.Add(text);
                else { passed.Add(text); if (failed.Contains(text)) reproduced = true; }
            }
            catch (JsonException) { }
        }
        string[] broken = [.. final.Where(item => item.Value != 0 && passed.Contains(item.Key)).Select(item => item.Key)];
        string[] unverified = [.. final.Where(item => item.Value != 0 && !passed.Contains(item.Key)).Select(item => item.Key)];
        return (reproduced, broken, unverified);
    }
    private static string? ValidationCommand(string command)
    {
        var text = command.Trim();
        var marker = text.IndexOf(" -Command ", StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
        {
            var prefix = text[..marker].Trim(); var quoted = prefix.StartsWith('"');
            var end = quoted ? prefix.IndexOf('"',1) : prefix.IndexOf(' ');
            if (end < 0) end = prefix.Length;
            var executable = prefix[(quoted?1:0)..end];
            var flags = prefix[Math.Min(prefix.Length,end+(quoted?1:0))..].Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if (Path.GetFileName(executable).ToLowerInvariant() is not ("powershell.exe" or "pwsh.exe" or "pwsh" or "powershell") ||
                flags.Any(flag=>!new[]{"-noprofile","-noninteractive","-nologo"}.Contains(flag.ToLowerInvariant()))) return null;
            text = text[(marker + 10)..].Trim().Trim('"', '\'');
        }
        if (text.Any(c => c is ';' or '&' or '|' or '`' or '\n' or '\r' or '<' or '>') || text.Contains("$(", StringComparison.Ordinal)) return null;
        if (text.StartsWith("npm audit", StringComparison.OrdinalIgnoreCase) &&
            !System.Text.RegularExpressions.Regex.IsMatch(text, @"^npm audit(?: --(?:audit-level=(?:info|low|moderate|high|critical|none)|json|omit=(?:dev|optional|peer)))*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return null;
        if (TrustedPostgres.Current?.Inner(text) is { } inner) return ValidationCommand(inner) is null ? null : text;
        // PowerShell repositories: a test script in the repository's test folder, run directly, or Pester.
        if (System.Text.RegularExpressions.Regex.IsMatch(text,
                @"^(?:pwsh|powershell)(?:\.exe)?(?: -(?:NoProfile|NonInteractive|NoLogo|ExecutionPolicy Bypass))* -File (?:\./)?(?:tests?|spec)/[\w./-]+\.ps1$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant) && !text.Contains("..", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(text, @"^Invoke-Pester(?: -(?:Path|Output|CI)(?: [\w./-]+)?)*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant) && !text.Contains("..", StringComparison.Ordinal))
            return text;
        var known = new[] { "dotnet test", "dotnet build", "npm test", "npm run test", "npm run build", "pnpm test", "pnpm run test", "pnpm build",
            "npm audit", "npx playwright test", "npx vitest", "pytest", "python -m pytest", "python -m unittest", "node --test" }
            .Any(prefix => text.Equals(prefix, StringComparison.OrdinalIgnoreCase) || text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase));
        return known ? text : null;
    }
    public static string[] Screenshots(string root)
    {
        var directory = Path.Combine(root, ".developer-workspace-evidence");
        if (!Directory.Exists(directory) || new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint)) return [];
        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) && new FileInfo(path).Length is > 0 and <= 5_000_000)
            .Take(12).Select(Path.GetFileName).OfType<string>().ToArray();
    }
}

public partial class GitHubReviewClient
{
    // Kept outside AppData: packaged desktop apps (Codex, Claude) redirect AppData writes into a private
    // per-app store, so a checkout created there is invisible to SemaForge and its builder.
    // HIREZERO_CHECKOUT_ROOT=repo opts into <source repo>\.worktrees\<id> instead of a shared folder.
    private static string? CheckoutSetting => Environment.GetEnvironmentVariable("HIREZERO_CHECKOUT_ROOT");
    public static bool CheckoutsInRepository => string.Equals(CheckoutSetting, "repo", StringComparison.OrdinalIgnoreCase);
    public static string CheckoutRoot => CheckoutsInRepository || CheckoutSetting is null
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HireZero", "ReviewCheckouts") : CheckoutSetting;
    public static bool RedirectedPath(string path) =>
        Path.GetFullPath(path).Contains(Path.DirectorySeparatorChar + "AppData" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string EmptyHooks(string checkout)
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(checkout))!, ".publisher-no-hooks");
        Directory.CreateDirectory(path);
        if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) || Directory.EnumerateFileSystemEntries(path).Any())
            throw new ReviewAccessException("The publisher hook isolation directory is not empty and safe.");
        return path;
    }
    public async Task<ReviewRepository> CheckoutAsync(ReviewRepository source, PullRequestWorkflow workflow, CancellationToken token)
    {
        await InspectAsync(source, null, false, token);
        var drive = new DriveInfo(Path.GetPathRoot(source.RootPath)!);
        if (drive.AvailableFreeSpace < 11L * 1024 * 1024 * 1024) throw new ReviewAccessException("The isolated checkout needs the 10 GiB disk reserve plus 1 GiB headroom.");
        var root = CheckoutsInRepository ? Path.Combine(source.RootPath, ".worktrees", workflow.Id) : Path.Combine(CheckoutRoot, workflow.Id);
        if (RedirectedPath(root)) throw new ReviewAccessException("Checkouts cannot live under AppData: packaged desktop apps hide it from SemaForge. Set HIREZERO_CHECKOUT_ROOT elsewhere.");
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        if (CheckoutsInRepository) await ExcludeWorktreesAsync(source.RootPath, token);
        if (Directory.Exists(root)) throw new ReviewAccessException("This workflow checkout already exists. Reconcile its saved handoff before retrying.");
        await GitAsync(source.RootPath, ["fetch", "--no-tags", "origin", "refs/pull/" + workflow.Number + "/head"], token);
        var head = (await GitAsync(source.RootPath, ["rev-parse", "FETCH_HEAD"], token)).Trim();
        if (head != workflow.HeadSha) throw new ReviewAccessException("The PR changed before checkout preparation. Request a new strategy.");
        await GitAsync(source.RootPath, ["worktree", "add", "--detach", root, head], token);
        var repository = source with { Id = workflow.Id, RootPath = root, Project = "github-fix-" + workflow.Id };
        await InspectAsync(repository, workflow.HeadSha, true, token);
        return repository;
    }
    // A retry starts from the PR head again: only this workflow's own worktree is reset, and only when nothing was
    // committed in it, so the publish step's exact-files check sees just the new attempt's changes.
    public async Task ResetCheckoutAsync(ReviewRepository checkout, PullRequestWorkflow workflow, CancellationToken token)
    {
        if (checkout.Id != workflow.Id || !Path.GetFileName(checkout.RootPath.TrimEnd('\\', '/')).Equals(workflow.Id, StringComparison.OrdinalIgnoreCase))
            throw new ReviewAccessException("Only this fix's own worktree can be reset.");
        await InspectAsync(checkout, null, false, token);
        if ((await GitAsync(checkout.RootPath, ["rev-parse", "HEAD"], token)).Trim() != workflow.HeadSha)
            throw new ReviewAccessException("Something was committed in this fix's worktree, so it isn't reset automatically. Ask for a fresh strategy.");
        await GitAsync(checkout.RootPath, ["reset", "--hard", "--quiet", workflow.HeadSha], token);
        await GitAsync(checkout.RootPath, ["clean", "-fd", "--quiet"], token);
        await InspectAsync(checkout, workflow.HeadSha, true, token);
    }
    // Local-only ignore (.git/info/exclude), so in-repo checkouts never show in the source's status or a commit.
    private async Task ExcludeWorktreesAsync(string sourceRoot, CancellationToken token)
    {
        var common = Path.GetFullPath(Path.Combine(sourceRoot, (await GitAsync(sourceRoot, ["rev-parse", "--git-common-dir"], token)).Trim()));
        var exclude = Path.Combine(common, "info", "exclude");
        Directory.CreateDirectory(Path.GetDirectoryName(exclude)!);
        var lines = File.Exists(exclude) ? await File.ReadAllLinesAsync(exclude, token) : [];
        if (!lines.Any(line => line.Trim() == "/.worktrees/"))
            await File.AppendAllTextAsync(exclude, (lines.Length > 0 && !string.IsNullOrEmpty(lines[^1]) ? Environment.NewLine : "") + "/.worktrees/" + Environment.NewLine, token);
    }
    public async Task<(string Diff, string Hash, string[] Files)> DiffAsync(ReviewRepository repository, PullRequestWorkflow workflow,
        JsonElement run, CancellationToken token)
    {
        await InspectAsync(repository, workflow.HeadSha, false, token);
        if ((await GitAsync(repository.RootPath, ["diff", "--cached", "--name-only"], token)).Length > 0)
            throw new ReviewAccessException("The isolated checkout has staged changes outside the publisher's control.");
        var expected = GitHubPublication.ChangedFiles(run);
        var actual = (await GitAsync(repository.RootPath, ["ls-files", "-m", "-o", "--exclude-standard", "-z"], token)).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Concat((await GitAsync(repository.RootPath, ["ls-files", "-d", "-z"], token)).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => !p.StartsWith(".developer-workspace-evidence/", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        if (!actual.Order().SequenceEqual(expected.Order())) throw new ReviewAccessException("Actual changed files do not match Sol's reported scope. Publication paused.");
        foreach (var path in actual) GitHubPublication.InspectPath(repository.RootPath, path);
        var diff = await GitAsync(repository.RootPath, ["diff", "--no-ext-diff", "--no-textconv", "HEAD", "--", .. actual], token);
        var newFiles=(await GitAsync(repository.RootPath,["ls-files","--others","--exclude-standard","-z"],token)).Split('\0',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var binding=new System.Text.StringBuilder(diff);
        var flagged = new List<PullRequestPolicy.SensitiveLine>();
        foreach (var path in actual)
        {
            var absolute = Path.Combine(repository.RootPath, path);
            if (!File.Exists(absolute)) continue;
            if (new FileInfo(absolute).Length > 200_000) throw new ReviewAccessException("Changed file exceeds the publication inspection limit.");
            var content = await File.ReadAllTextAsync(absolute, token);
            // Only what this run adds is checked: lines already on the PR are public and unchanged, so they never block it.
            var lines = PullRequestPolicy.SensitiveLines(path, content);
            if (!newFiles.Contains(path) && lines.Length > 0)
            {
                var added = PullRequestPolicy.AddedLines(await GitAsync(repository.RootPath, ["diff", "--no-ext-diff", "--no-textconv", "-U0", "HEAD", "--", path], token));
                lines = [.. lines.Where(line => added.Contains(line.Line))];
            }
            flagged.AddRange(lines);
            // Bind full files without sending unchanged source into another model briefing.
            binding.Append('\n').Append(path).Append(':').Append(PullRequestPolicy.Hash(content));
            if(newFiles.Contains(path))diff += "\nNEW FILE " + path + "\n" + content;
        }
        if (flagged.FirstOrDefault(line => line.Credential) is { } credential)
            throw new ReviewAccessException("Publication stopped: " + PullRequestPolicy.Describe(credential) + " looks like a real credential (a token or private key). Remove it and run again; this can't be confirmed away.");
        var acknowledged = flagged.Count > 0 && workflow.SensitiveAck == PullRequestPolicy.SensitiveHash(flagged);
        if (flagged.Count > 0 && !acknowledged)
            throw new SensitiveContentException("Publication paused: " + flagged.Count + " changed line" + (flagged.Count == 1 ? " looks" : "s look") +
                " like it could hold a secret (a password, key or connection-string field, or a user:password@ address). Sol's work is kept and nothing was committed. " +
                "Check " + (flagged.Count == 1 ? "it" : "them") + " locally: " + string.Join(", ", flagged.Take(8).Select(PullRequestPolicy.Describe)) + (flagged.Count > 8 ? ", …" : "") +
                ". If they're test placeholders, confirm below and the work is checked again; otherwise run again with a note.", [.. flagged], PullRequestPolicy.SensitiveHash(flagged));
        // Lines the owner confirmed are allowed through; any real-looking token in the diff still stops it.
        // Added lines were checked above; a real-looking token anywhere in what gets added still stops it.
        if (diff.Length > 45_000 || diff.Split('\n').Any(line => line.StartsWith('+') && (!GitHubReviewPolicy.SafeText(line) || line.Contains("PRIVATE KEY-----", StringComparison.Ordinal))))
            throw new ReviewAccessException("The diff is too large or may contain sensitive values; publication needs local review.");
        return (diff, PullRequestPolicy.Hash(binding.ToString()), actual);
    }
    public async Task<string> CommitAsync(ReviewRepository repository, PullRequestWorkflow workflow, string[] paths, CancellationToken token)
    {
        await GitAsync(repository.RootPath, ["add", "--", .. paths], token);
        var staged = (await GitAsync(repository.RootPath, ["diff", "--cached", "--name-only", "-z"], token)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (!staged.Order().SequenceEqual(paths.Order())) throw new ReviewAccessException("The staged changes do not match the approved diff.");
        // The credentialed publisher never runs repository-supplied hooks. Validation belongs to the sandboxed worker.
        await GitAsync(repository.RootPath, ["-c", "core.hooksPath=" + EmptyHooks(repository.RootPath),
            "commit", "-m", "fix: address PR #" + workflow.Number + " findings", "-m", "Validated through Developer Workspace. Private conversation and source context intentionally omitted."], token);
        var head = (await HeadAsync(repository, token)).Trim();
        if (!PullRequestPolicy.Sha(head) || (await GitAsync(repository.RootPath, ["rev-parse", "HEAD^"], token)).Trim() != workflow.HeadSha)
            throw new ReviewAccessException("The resulting commit does not descend directly from the approved revision.");
        return head;
    }
    public async Task PushAsync(ReviewRepository repository, PullRequestEvidence evidence, string commit, CancellationToken token)
    {
        if (!PullRequestPolicy.Sha(commit) || !GitHubReviewPolicy.ValidRepository(evidence.HeadRepository) ||
            evidence.HeadBranch == evidence.DefaultBranch || evidence.HeadBranch is "main" or "master" or "stage" or "production" ||
            evidence.HeadBranch.StartsWith('-') || evidence.HeadBranch.Any(c => char.IsWhiteSpace(c) || c is ':' or '\\'))
            throw new ReviewAccessException("Publication requires a verified, unprotected PR feature branch.");
        var branch = await ApiAsync("repos/" + evidence.HeadRepository + "/branches/" + Uri.EscapeDataString(evidence.HeadBranch), token);
        if (branch.GetProperty("protected").GetBoolean() || Text(branch.GetProperty("commit"), "sha") != evidence.HeadSha)
            throw new ReviewAccessException("The PR branch is protected or changed. Publication paused.");
        var headRepository = await ApiAsync("repos/" + evidence.HeadRepository, token);
        if (headRepository.GetProperty("id").GetInt64() != evidence.HeadRepositoryId ||
            !headRepository.GetProperty("permissions").GetProperty("push").GetBoolean()) throw new ReviewAccessException("The connected account cannot update this PR branch.", 403);
        await GitAsync(repository.RootPath, ["-c", "core.hooksPath=" + EmptyHooks(repository.RootPath), "push",
            "https://github.com/" + evidence.HeadRepository + ".git", commit + ":refs/heads/" + evidence.HeadBranch], token);
    }
}
