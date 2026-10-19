using System.Globalization;
using System.Text.Json;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;

namespace HireZero.Roles.Developer;

// The company Developer's pull request fix (docs/adr/0012-company-publication.md). The fix runs as reviewed execution
// in the owner's checkout of the PR head; its diff becomes a publication the owner approves. Publishing commits
// exactly the reviewed files and pushes them to the PR branch with a lease on the reviewed head, once.
public sealed class CompanyPullRequestFix(CompanyStore store, ToolRegistry registry, ICompanyCodingFactory coding)
{
    public sealed record Target(string Repository, int Number, string Head);

    private sealed record Branch(string Repository, string Name, string Head);

    private IToolbox Tools(string employeeId, string taskId) =>
        registry.For(DeveloperModule.Shared, employeeId, taskId);

    private CompanyDeveloperBridge Bridge(CompanyTask task) => new(store, Tools(task.EmployeeId, task.Id), coding);

    // The pull request the fix is for: its one approved evidence artifact, gathered by the pr-evidence action.
    public Target TargetOf(CompanyTask task)
    {
        if (task.Skill != DeveloperModule.Fix)
            throw new WorkspaceException("This is not a pull request fix.");
        var evidence = task
            .Evidence.Select(store.Artifact)
            .Where(a => a.Provenance == "action:" + PullRequestEvidenceAction.Id && a.State == "approved")
            .ToArray();
        if (evidence.Length != 1)
            throw new WorkspaceException("Attach exactly one approved pull request evidence artifact.");
        var action = store.Work(evidence[0].TaskId);
        Dictionary<string, string>? input;
        try
        {
            input = JsonSerializer.Deserialize<Dictionary<string, string>>(action.Input, CompanyJson.Options);
        }
        catch (JsonException)
        {
            input = null;
        }
        var head = evidence[0]
            .Body.Split('\n')
            .TakeWhile(line => line != "Description:")
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("Branches: ", StringComparison.Ordinal))
            .Select(line => line[(line.LastIndexOf("; head ", StringComparison.Ordinal) + 7)..])
            .ToArray();
        if (
            input?.GetValueOrDefault("repository")?.Trim() is not { } repository
            || !GitHubReviewPolicy.ValidRepository(repository)
            || !int.TryParse(
                input.GetValueOrDefault("number"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number
            )
            || number <= 0
            || head is not [{ } sha]
            || !GitHubReviewPolicy.Sha(sha)
        )
            throw new WorkspaceException("The evidence does not identify one pull request head.");
        return new(repository, number, sha);
    }

    // Proposes the fix's execution in the owner's checkout, which must be at the head the owner reviewed, as must the
    // pull request itself.
    public async Task<CompanyExecutionGrant> PrepareAsync(string taskId, CancellationToken token = default)
    {
        var task = store.Work(taskId);
        var target = TargetOf(task);
        var root = task.Resource ?? throw new WorkspaceException("Grant the checkout of the pull request first.");
        var tools = Tools(task.EmployeeId, task.Id);
        var live = await PullAsync(tools, target, token);
        if (live.Head != target.Head)
            throw new WorkspaceException("The pull request changed after its evidence was reviewed; gather it again.");
        var status = await tools.InvokeAsync(GitCommands.Read, new(root, ["status", "--porcelain"]), token);
        if (status.Trim().Length != 0)
            throw new WorkspaceException("The checkout has uncommitted changes.");
        return await Bridge(task).PrepareAsync(task.Id, root, true, token, target.Head);
    }

    public Task ExecuteFixtureAsync(string grantId, CancellationToken token = default) =>
        Bridge(store.Work(store.Execution(grantId).TaskId)).ExecuteFixtureAsync(grantId, token);

    // After the fix ran, its reviewed diff becomes a publication: exactly these files, one commit, the PR branch.
    public async Task<CompanyPublication> ProposePublicationAsync(string grantId, CancellationToken token = default)
    {
        var grant = store.Execution(grantId);
        var task = store.Work(grant.TaskId);
        var target = TargetOf(task);
        if (grant.State != "completed" || grant.Review is not { Files.Length: > 0 } review)
            throw new WorkspaceException("Only a completed fix with changes can be published.");
        if (grant.Revision != target.Head)
            throw new WorkspaceException("The fix did not run at the reviewed pull request head.");
        foreach (var file in review.Files)
        {
            if (!GitHubReviewPolicy.SafePath(file.Path))
                throw new WorkspaceException("A changed file is outside the publishable scope: " + file.Path);
            if (ContentGuard.Clean(file.Patch, 70000).Flags.Contains(ContentGuard.Redacted))
                throw new WorkspaceException("A changed file contains a credential-like value: " + file.Path);
        }
        var artifact =
            store.ArtifactsOf(task.Id).LastOrDefault(a => a.Body.Contains(review.Digest, StringComparison.Ordinal))
            ?? throw new WorkspaceException("The fix has no reviewable artifact.");
        var branch = await BranchAsync(Tools(task.EmployeeId, task.Id), target, token);
        return store.ProposePublication(
            new(
                grant.Id,
                artifact.Id,
                Summary(target, branch, review),
                [.. LocalCalls(grant, target, review).Select(c => ToolRegistry.Digest(GitCommands.Write, c))]
            )
        );
    }

    // Publishes an approved fix: re-checks the change and the branch, commits through git.write and pushes through
    // git.push, each authorized by the publication. Never retried; an unconfirmed push is left unknown.
    public async Task<CompanyPublication> PublishAsync(string publicationId, CancellationToken token = default)
    {
        var publication = store.Publication(publicationId);
        var grant = store.Execution(
            publication.GrantId ?? throw new WorkspaceException("This publication is not a pull request fix.")
        );
        var task = store.Work(publication.TaskId);
        var target = TargetOf(task);
        var tools = Tools(task.EmployeeId, task.Id);
        var review = grant.Review ?? throw new WorkspaceException("The fix has no reviewed diff.");
        var branch = await BranchAsync(tools, target, token);
        if (Summary(target, branch, review) != publication.Summary)
            throw new WorkspaceException("The pull request branch changed after approval; propose a fresh fix.");
        store.ClaimPublication(publication.Id);
        var authority = new ToolAuthority(CompanyStore.PublicationAuthorityKind, publication.Id);
        try
        {
            var current = await tools.InvokeAsync(
                GitTools.ReviewCheckout,
                new(grant.Workspace, grant.Revision),
                CancellationToken.None
            );
            if (current.Digest != publication.ReviewDigest)
                throw new WorkspaceException("The checkout no longer holds the reviewed diff.");
            var paths = review.Files.Select(f => f.Path).Order(StringComparer.Ordinal).ToArray();
            EmptyHooks(grant.Workspace);
            var calls = LocalCalls(grant, target, review);
            await tools.InvokeAsync(GitCommands.Write, calls[0], CancellationToken.None, authority);
            var staged = await Read(tools, grant.Workspace, "diff", "--cached", "--name-only", "-z");
            if (!Names(staged).SequenceEqual(paths))
                throw new WorkspaceException("The staged files do not match the reviewed diff.");
            await tools.InvokeAsync(GitCommands.Write, calls[1], CancellationToken.None, authority);
            var commit = (await Read(tools, grant.Workspace, "rev-parse", "HEAD")).Trim();
            if (
                !GitHubReviewPolicy.Sha(commit)
                || (await Read(tools, grant.Workspace, "rev-parse", "HEAD^")).Trim() != grant.Revision
                || !Names(await Read(tools, grant.Workspace, "diff", "--name-only", "-z", "HEAD^", "HEAD"))
                    .SequenceEqual(paths)
            )
                throw new WorkspaceException(
                    "The commit does not hold exactly the reviewed change on the reviewed head."
                );
            var push = Push(grant, branch, commit);
            store.BindPublication(publication.Id, commit, ToolRegistry.Digest(GitCommands.Push, push));
            await tools.InvokeAsync(GitCommands.Push, push, CancellationToken.None, authority);
            var delivered = await PullAsync(tools, target, CancellationToken.None);
            return store.FinishPublication(
                publication.Id,
                delivered.Head == commit,
                delivered.Head == commit ? null : "The pull request head is not the pushed commit yet."
            );
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            store.FinishPublication(
                publication.Id,
                false,
                error is WorkspaceException ? error.Message : "The publication stopped: " + error.GetType().Name
            );
            throw;
        }
    }

    // A read-only check of an unknown publication: if the pull request's head is the pushed commit, it was published.
    public async Task<CompanyPublication> ReconcileAsync(string publicationId, CancellationToken token = default)
    {
        var publication = store.Publication(publicationId);
        if (publication.State != "unknown" || publication.Result is null)
            return publication;
        var task = store.Work(publication.TaskId);
        var live = await PullAsync(Tools(task.EmployeeId, task.Id), TargetOf(task), token);
        return live.Head == publication.Result ? store.ConfirmPublication(publication.Id) : publication;
    }

    private static string Summary(Target target, Branch branch, CompanyRepositoryReview review) =>
        $"Commit {review.Files.Length} reviewed file{(review.Files.Length == 1 ? "" : "s")} to {branch.Repository}:{branch.Name} "
        + $"(pull request {target.Repository}#{target.Number}) over {target.Head[..12]}, then push it once.";

    // Staging exactly the reviewed files, then one commit without repository hooks.
    private static GitCommand[] LocalCalls(
        CompanyExecutionGrant grant,
        Target target,
        CompanyRepositoryReview review
    ) =>
        [
            new(grant.Workspace, ["add", "--", .. review.Files.Select(f => f.Path).Order(StringComparer.Ordinal)]),
            new(
                grant.Workspace,
                [
                    "-c",
                    "core.hooksPath=" + HooksPath(grant.Workspace),
                    "commit",
                    "-m",
                    "fix: address PR #" + target.Number.ToString(CultureInfo.InvariantCulture) + " findings",
                    "-m",
                    "Prepared by the HireZero Developer and reviewed by the owner before publication.",
                ]
            ),
        ];

    // The lease makes the push conditional on the branch still being at the reviewed head.
    private static GitCommand Push(CompanyExecutionGrant grant, Branch branch, string commit) =>
        new(
            grant.Workspace,
            [
                "-c",
                "core.hooksPath=" + HooksPath(grant.Workspace),
                "push",
                "--force-with-lease=refs/heads/" + branch.Name + ":" + grant.Revision,
                "https://github.com/" + branch.Repository + ".git",
                commit + ":refs/heads/" + branch.Name,
            ]
        );

    private static string HooksPath(string workspace) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspace))!, ".publisher-no-hooks");

    private static void EmptyHooks(string workspace)
    {
        var path = HooksPath(workspace);
        Directory.CreateDirectory(path);
        if (
            new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)
            || Directory.EnumerateFileSystemEntries(path).Any()
        )
            throw new WorkspaceException("The publisher's hook isolation directory is not empty and safe.");
    }

    private static string[] Names(string output) =>
        [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];

    private static Task<string> Read(IToolbox tools, string root, params string[] args) =>
        tools.InvokeAsync(GitCommands.Read, new(root, args), CancellationToken.None);

    private static async Task<JsonElement> ApiAsync(IToolbox tools, string path, CancellationToken token)
    {
        var json = await tools.InvokeAsync(
            GitHubCommands.Read,
            new(["api", "--hostname", "github.com", "--method", "GET", path]),
            token
        );
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new WorkspaceException("GitHub returned an unrecognized response.", 502);
        }
    }

    private static string Text(JsonElement value, params string[] path)
    {
        foreach (var name in path)
            value =
                value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var next) ? next : default;
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    }

    private static async Task<Branch> PullAsync(IToolbox tools, Target target, CancellationToken token)
    {
        var pull = await ApiAsync(
            tools,
            "repos/" + target.Repository + "/pulls/" + target.Number.ToString(CultureInfo.InvariantCulture),
            token
        );
        if (Text(pull, "state") != "open")
            throw new WorkspaceException("The pull request is not open.");
        return new(Text(pull, "head", "repo", "full_name"), Text(pull, "head", "ref"), Text(pull, "head", "sha"));
    }

    // The pull request's head branch, checked as the legacy publisher does: an unprotected feature branch at the
    // reviewed head, which the signed-in account may push to.
    private static async Task<Branch> BranchAsync(IToolbox tools, Target target, CancellationToken token)
    {
        var pull = await ApiAsync(
            tools,
            "repos/" + target.Repository + "/pulls/" + target.Number.ToString(CultureInfo.InvariantCulture),
            token
        );
        var branch = new Branch(
            Text(pull, "head", "repo", "full_name"),
            Text(pull, "head", "ref"),
            Text(pull, "head", "sha")
        );
        if (
            Text(pull, "state") != "open"
            || branch.Head != target.Head
            || !GitHubReviewPolicy.ValidRepository(branch.Repository)
            || branch.Name.Length is 0 or > 200
            || branch.Name == Text(pull, "head", "repo", "default_branch")
            || branch.Name is "main" or "master" or "stage" or "production"
            || branch.Name.StartsWith('-')
            || branch.Name.Any(c =>
                char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '\\' or '~' or '^' or '?' or '*' or '['
            )
        )
            throw new WorkspaceException(
                "Publication needs an open pull request on an unprotected feature branch at the reviewed head."
            );
        var remote = await ApiAsync(
            tools,
            "repos/" + branch.Repository + "/branches/" + Uri.EscapeDataString(branch.Name),
            token
        );
        if (
            remote.TryGetProperty("protected", out var guarded) && guarded.ValueKind != JsonValueKind.False
            || Text(remote, "commit", "sha") != target.Head
        )
            throw new WorkspaceException("The pull request branch is protected or changed.");
        var repository = await ApiAsync(tools, "repos/" + branch.Repository, token);
        if (
            !repository.TryGetProperty("permissions", out var permissions)
            || !permissions.TryGetProperty("push", out var push)
            || push.ValueKind != JsonValueKind.True
        )
            throw new WorkspaceException("The signed-in account cannot push to this pull request's branch.", 403);
        return branch;
    }
}
