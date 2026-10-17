using System.Text.Json;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;

namespace HireZero.Roles.Developer;

public sealed record PullRequestLine(int Number, string Title, string Author, bool Draft, string Review, string At);

public sealed record AssignedIssue(int Number, string Title, string UpdatedAt);

public class ReviewAccessException(string message, int status = 409) : WorkspaceException(message, status);

// Changed files have secret-shaped lines the owner hasn't confirmed. Sol's work is kept; nothing was committed.
public sealed class SensitiveContentException(string message, PullRequestPolicy.SensitiveLine[] lines, string hash)
    : ReviewAccessException(message)
{
    public PullRequestPolicy.SensitiveLine[] Lines { get; } = lines;
    public string Hash { get; } = hash;
}

public partial class GitHubReviewClient(ToolRegistry? registry = null)
{
    private readonly IToolbox? _tools = registry?.For(DeveloperModule.Shared);
    private static string NoHooks => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
    private static readonly string Executable = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe")
        : "gh";
    private const string ReviewQuery = """
        query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name){databaseId
          pullRequest(number:$number){number title state headRefOid author{login} headRepository{databaseId}
            reviewThreads(first:100){pageInfo{hasNextPage} nodes{id path line isResolved isOutdated
              comments(first:20){pageInfo{hasNextPage} nodes{id body updatedAt author{login}}}}}}}}
        """;

    public virtual async Task<GitHubIdentity> IdentityAsync(CancellationToken token)
    {
        var user = await ApiAsync("user", token);
        return new(user.GetProperty("id").GetInt64(), user.GetProperty("login").GetString()!);
    }

    public async Task<(long Id, string Name)> RepositoryAsync(string name, CancellationToken token)
    {
        if (!GitHubReviewPolicy.ValidRepository(name))
            throw new ReviewAccessException("Choose a GitHub repository in owner/name form.", 400);
        var repo = await ApiAsync("repos/" + name, token);
        if (
            repo.GetProperty("archived").GetBoolean()
            || repo.GetProperty("disabled").GetBoolean()
            || !repo.TryGetProperty("permissions", out var permissions)
            || !permissions.GetProperty("pull").GetBoolean()
        )
            throw new ReviewAccessException(
                "The connected GitHub account needs read access to an active repository before it can be connected.",
                403
            );
        return (repo.GetProperty("id").GetInt64(), repo.GetProperty("full_name").GetString()!);
    }

    public async Task<RepositoryChoice[]> ResolveAsync(string requested, CancellationToken token)
    {
        requested = requested.Trim().TrimEnd('.', '!');
        if (GitHubReviewPolicy.ValidRepository(requested))
        {
            var repo = await RepositoryAsync(requested, token);
            return [new(repo.Id, repo.Name)];
        }
        if (
            requested.Length is < 2 or > 100
            || requested.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_' or ' ')
            )
        )
            throw new ReviewAccessException("Give a repository name or owner/name.", 400);
        // Search only owners you work with: HIREZERO_GITHUB_OWNERS (comma-separated), else your account and its organizations.
        var owners = Environment
            .GetEnvironmentVariable("HIREZERO_GITHUB_OWNERS")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(owner =>
                owner.Length <= 39 && owner.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            )
            .ToArray()
            is { Length: > 0 } configured
            ? configured
            :
            [
                (await IdentityAsync(token)).Login,
                .. (await ApiAsync("user/orgs?per_page=50", token))
                    .EnumerateArray()
                    .Select(org => org.GetProperty("login").GetString()!),
            ];
        var query = requested + " in:name " + string.Join(" ", owners.Take(20).Select(owner => "user:" + owner));
        var result = await ApiAsync("search/repositories?q=" + Uri.EscapeDataString(query) + "&per_page=5", token);
        return result
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => new RepositoryChoice(
                item.GetProperty("id").GetInt64(),
                item.GetProperty("full_name").GetString()!
            ))
            .ToArray();
    }

    // The owner's PRs created or merged between two instants (GitHub search by UTC day, then filtered exactly).
    public async Task<WorkLogEntry[]> MyPullRequestsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken token
    )
    {
        var range = from.UtcDateTime.ToString("yyyy-MM-dd") + ".." + to.UtcDateTime.ToString("yyyy-MM-dd");
        var entries = new List<WorkLogEntry>();
        foreach (
            var (qualifier, kind, stamp) in new[]
            {
                ("--created", "pr-opened", "createdAt"),
                ("--merged-at", "pr-merged", "closedAt"),
            }
        )
        {
            var output = await CommandAsync(
                "gh",
                null,
                [
                    "search",
                    "prs",
                    "--author",
                    "@me",
                    qualifier,
                    range,
                    "--json",
                    "number,title,repository,url,createdAt,closedAt,isDraft",
                    "--limit",
                    "100",
                ],
                null,
                token
            );
            using var document = JsonDocument.Parse(output);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!DateTimeOffset.TryParse(item.GetProperty(stamp).GetString(), out var at) || at < from || at >= to)
                    continue;
                var slug = item.GetProperty("repository").GetProperty("nameWithOwner").GetString() ?? "";
                if (GitRemote.Parse("https://github.com/" + slug) is not { } origin)
                    continue;
                var title = PullRequestPolicy.Safe(item.GetProperty("title").GetString(), 160);
                var draft = item.TryGetProperty("isDraft", out var isDraft) && isDraft.ValueKind == JsonValueKind.True;
                entries.Add(
                    new(
                        at.ToString("O"),
                        kind,
                        origin.Name,
                        (kind == "pr-opened" ? "Opened" : "Merged")
                            + " #"
                            + item.GetProperty("number").GetInt32()
                            + " "
                            + title
                            + (kind == "pr-opened" && draft ? " (draft)" : "")
                            + " in "
                            + origin.Name,
                        item.GetProperty("url").GetString(),
                        origin
                    )
                );
            }
        }
        return [.. entries];
    }

    // A few facts about one of the owner's PRs for the boss panel: state, branches, size, reviews, checks, a short description.
    public async Task<WorkLogDetail?> PullRequestDetailAsync(string url, string text, CancellationToken token)
    {
        if (
            !System.Text.RegularExpressions.Regex.IsMatch(
                url,
                @"^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/pull/\d+$",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(50)
            )
        )
            return null;
        var output = await CommandAsync(
            "gh",
            null,
            [
                "pr",
                "view",
                url,
                "--json",
                "number,title,body,state,isDraft,createdAt,mergedAt,baseRefName,headRefName,additions,deletions,changedFiles,reviewDecision,statusCheckRollup",
            ],
            null,
            token
        );
        using var document = JsonDocument.Parse(output);
        var pr = document.RootElement;
        string Text(string name) =>
            pr.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        static string When(string at) =>
            DateTimeOffset.TryParse(at, out var time) ? time.ToLocalTime().ToString("ddd MMM d, h:mm tt") : at;
        var facts = new List<WorkLogFact>
        {
            new(
                "State",
                (pr.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True ? "draft, " : "")
                    + Text("state").ToLowerInvariant()
            ),
            new("Branches", Text("headRefName") + " into " + Text("baseRefName")),
            new("Opened", When(Text("createdAt"))),
        };
        if (Text("mergedAt").Length > 0)
            facts.Add(new("Merged", When(Text("mergedAt"))));
        facts.Add(
            new(
                "Size",
                pr.GetProperty("changedFiles").GetInt32()
                    + " files, +"
                    + pr.GetProperty("additions").GetInt32()
                    + " / -"
                    + pr.GetProperty("deletions").GetInt32()
            )
        );
        if (Text("reviewDecision").Length > 0)
            facts.Add(new("Review", Text("reviewDecision").ToLowerInvariant().Replace('_', ' ')));
        if (
            pr.TryGetProperty("statusCheckRollup", out var checks)
            && checks.ValueKind == JsonValueKind.Array
            && checks.GetArrayLength() > 0
        )
        {
            var states = checks
                .EnumerateArray()
                .Select(check =>
                    (
                        check.TryGetProperty("conclusion", out var conclusion)
                        && conclusion.ValueKind == JsonValueKind.String
                        && conclusion.GetString()!.Length > 0
                            ? conclusion.GetString()!
                        : check.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
                            ? state.GetString()!
                        : "PENDING"
                    ).ToUpperInvariant()
                )
                .ToList();
            var passed = states.Count(state => state is "SUCCESS" or "NEUTRAL" or "SKIPPED");
            var failing = states.Count(state =>
                state is "FAILURE" or "ERROR" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED"
            );
            facts.Add(
                new(
                    "Checks",
                    passed
                        + " passed"
                        + (failing > 0 ? ", " + failing + " failing" : "")
                        + (states.Count - passed - failing is var pending and > 0 ? ", " + pending + " pending" : "")
                )
            );
        }
        return new(text, [.. facts], WorkLogText.Excerpt(Text("body")), url);
    }

    // One cheap listing for a repository: open or recently merged PRs with title, author, draft and review state.
    public async Task<PullRequestLine[]> PullRequestsAsync(
        string repository,
        string state,
        int limit,
        CancellationToken token
    )
    {
        if (
            !System.Text.RegularExpressions.Regex.IsMatch(
                repository,
                @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(50)
            ) || state is not ("open" or "merged")
        )
            throw new ReviewAccessException("That repository listing isn't allowed.", 400);
        var output = await CommandAsync(
            "gh",
            null,
            [
                "pr",
                "list",
                "--repo",
                repository,
                "--state",
                state,
                "--limit",
                Math.Clamp(limit, 1, 20).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--json",
                "number,title,author,isDraft,reviewDecision,updatedAt,mergedAt",
            ],
            null,
            token
        );
        using var document = JsonDocument.Parse(output);
        return
        [
            .. document
                .RootElement.EnumerateArray()
                .Select(item => new PullRequestLine(
                    item.GetProperty("number").GetInt32(),
                    PullRequestPolicy.Safe(item.GetProperty("title").GetString(), 160),
                    item.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login)
                        ? login.GetString() ?? ""
                        : "",
                    item.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
                    item.TryGetProperty("reviewDecision", out var review) && review.ValueKind == JsonValueKind.String
                        ? review.GetString() ?? ""
                        : "",
                    item.TryGetProperty(state == "merged" ? "mergedAt" : "updatedAt", out var at)
                    && at.ValueKind == JsonValueKind.String
                        ? at.GetString() ?? ""
                        : ""
                )),
        ];
    }

    public async Task<CodeReview[]> MyReviewsAsync(
        GitHubIdentity account,
        CancellationToken token,
        bool personalRepositoriesOnly = false
    )
    {
        var candidates = await SuggestionsAsync(account, token);
        var reviews = new List<CodeReview>();
        foreach (var candidate in candidates.GetProperty("candidates").EnumerateArray())
        {
            var name = candidate.GetProperty("repository").GetString()!;
            // A shift's personal-repository scope is checked before requesting repository or PR details.
            if (personalRepositoriesOnly && !name.StartsWith(account.Login + "/", StringComparison.OrdinalIgnoreCase))
                continue;
            var remote = await RepositoryAsync(name, token);
            var repository = new ReviewRepository("", account.Id, remote.Id, remote.Name, "", "");
            var review = await ReviewAsync(repository, candidate.GetProperty("number").GetInt32(), token);
            if (review.Open && review.Author.Equals(account.Login, StringComparison.OrdinalIgnoreCase))
                reviews.Add(review);
        }
        return [.. reviews];
    }

    public async Task<JsonElement> SuggestionsAsync(GitHubIdentity account, CancellationToken token)
    {
        var result = await ApiAsync(
            "search/issues?q="
                + Uri.EscapeDataString("is:pr is:open author:" + account.Login)
                + "&sort=updated&per_page=12",
            token
        );
        return JsonSerializer.SerializeToElement(
            new
            {
                total = result.GetProperty("total_count").GetInt32(),
                candidates = result
                    .GetProperty("items")
                    .EnumerateArray()
                    .Select(item => new
                    {
                        repository = item.GetProperty("repository_url")
                            .GetString()!
                            .Replace("https://api.github.com/repos/", ""),
                        number = item.GetProperty("number").GetInt32(),
                        title = item.GetProperty("title").GetString(),
                    })
                    .ToArray(),
            }
        );
    }

    public virtual async Task<CodeReview[]> ReviewsAsync(
        ReviewRepository repository,
        GitHubIdentity account,
        CancellationToken token
    )
    {
        var search = await ApiAsync(
            "search/issues?q="
                + Uri.EscapeDataString("is:pr is:open author:" + account.Login + " repo:" + repository.Name)
                + "&sort=updated&per_page=8",
            token
        );
        var reviews = new List<CodeReview>();
        foreach (var item in search.GetProperty("items").EnumerateArray())
        {
            var review = await ReviewAsync(repository, item.GetProperty("number").GetInt32(), token);
            if (GitHubReviewPolicy.Owned(account, repository, review))
                reviews.Add(review);
        }
        return [.. reviews];
    }

    // Exact registered repository plus connected-account assignment; metadata only, never issue bodies.
    public virtual async Task<AssignedIssue[]> AssignedIssuesAsync(
        ReviewRepository repository,
        GitHubIdentity account,
        CancellationToken token
    )
    {
        if (repository.AccountId != account.Id || !GitHubReviewPolicy.ValidRepository(repository.Name))
            throw new ReviewAccessException("Choose a repository registered to the connected account.", 403);
        var result = await ApiAsync(
            "search/issues?q="
                + Uri.EscapeDataString("is:issue is:open assignee:" + account.Login + " repo:" + repository.Name)
                + "&sort=updated&per_page=8",
            token
        );
        return result
            .GetProperty("items")
            .EnumerateArray()
            .Take(8)
            .Where(item =>
                !item.TryGetProperty("pull_request", out _)
                && item.GetProperty("state").GetString() == "open"
                && item.GetProperty("repository_url")
                    .GetString()!
                    .Equals("https://api.github.com/repos/" + repository.Name, StringComparison.OrdinalIgnoreCase)
                && item.GetProperty("assignees")
                    .EnumerateArray()
                    .Any(person =>
                        person
                            .GetProperty("login")
                            .GetString()!
                            .Equals(account.Login, StringComparison.OrdinalIgnoreCase)
                    )
            )
            .Select(item => (item, title: ContentGuard.Clean(item.GetProperty("title").GetString(), 180)))
            .Where(pair => pair.title.Flags.Length == 0 && pair.title.Text.Length > 0)
            .Select(pair => new AssignedIssue(
                pair.item.GetProperty("number").GetInt32(),
                pair.title.Text,
                pair.item.GetProperty("updated_at").GetString()!
            ))
            .Where(item => item.Number > 0)
            .ToArray();
    }

    public async Task<CodeReview> ReviewAsync(ReviewRepository repository, int number, CancellationToken token)
    {
        if (number <= 0)
            throw new ReviewAccessException("Choose a valid PR number.", 400);
        var parts = repository.Name.Split('/');
        var body = JsonSerializer.Serialize(
            new
            {
                query = ReviewQuery,
                variables = new
                {
                    owner = parts[0],
                    name = parts[1],
                    number,
                },
            }
        );
        var result = await JsonAsync(["api", "graphql", "--hostname", "github.com", "--input", "-"], body, token);
        if (
            result.TryGetProperty("errors", out _)
            || !result.TryGetProperty("data", out var data)
            || data.GetProperty("repository").ValueKind == JsonValueKind.Null
        )
            throw new ReviewAccessException("GitHub could not provide this review.", 502);
        var repo = data.GetProperty("repository");
        if (repo.GetProperty("databaseId").GetInt64() != repository.GitHubId)
            throw new ReviewAccessException(
                "The GitHub repository identity changed. Remove its allowlist entry and add it again."
            );
        var pr = repo.GetProperty("pullRequest");
        if (pr.ValueKind == JsonValueKind.Null)
            throw new ReviewAccessException("This PR was not found.", 404);
        var connection = pr.GetProperty("reviewThreads");
        var threads = connection
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(thread =>
            {
                var comments = thread.GetProperty("comments");
                return new ReviewThread(
                    thread.GetProperty("id").GetString()!,
                    thread.GetProperty("path").GetString()!,
                    thread.GetProperty("line").ValueKind == JsonValueKind.Number
                        ? thread.GetProperty("line").GetInt32()
                        : null,
                    thread.GetProperty("isResolved").GetBoolean(),
                    thread.GetProperty("isOutdated").GetBoolean(),
                    !comments.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(),
                    comments
                        .GetProperty("nodes")
                        .EnumerateArray()
                        .Select(comment => new ReviewComment(
                            comment.GetProperty("id").GetString()!,
                            Login(comment),
                            comment.GetProperty("body").GetString()!,
                            comment.GetProperty("updatedAt").GetString()!
                        ))
                        .ToArray()
                );
            })
            .ToArray();
        var headRepo = pr.GetProperty("headRepository");
        return new(
            number,
            pr.GetProperty("title").GetString()!,
            pr.GetProperty("headRefOid").GetString()!,
            Login(pr),
            pr.GetProperty("state").GetString() == "OPEN",
            headRepo.ValueKind == JsonValueKind.Null ? 0 : headRepo.GetProperty("databaseId").GetInt64(),
            !connection.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(),
            threads,
            repository.Name
        );
    }

    public Task<string> HeadAsync(ReviewRepository repository, CancellationToken token) =>
        CommandAsync("git", repository.RootPath, ["rev-parse", "HEAD"], null, token);

    public async Task InspectAsync(
        ReviewRepository repository,
        string? expectedHead,
        bool requireClean,
        CancellationToken token
    )
    {
        if (string.IsNullOrWhiteSpace(repository.RootPath))
            throw new ReviewAccessException(
                "Choose a local checkout for this repository before scoping or starting a fix."
            );
        var root = (
            await CommandAsync("git", repository.RootPath, ["rev-parse", "--show-toplevel"], null, token)
        ).Trim();
        if (
            !Path.GetFullPath(root)
                .TrimEnd('\\', '/')
                .Equals(
                    repository.RootPath.TrimEnd('\\', '/'),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                )
        )
            throw new ReviewAccessException("Register the exact local Git repository root.");
        // Junctions would let a later filesystem change silently broaden the approved root.
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ReviewAccessException("Use a local repository root without junctions or symbolic links.");
        var remote = (await CommandAsync("git", root, ["remote", "get-url", "origin"], null, token)).Trim();
        if (
            !string.Equals(
                GitHubReviewPolicy.RemoteRepository(remote),
                repository.Name,
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new ReviewAccessException("The local origin must match the allowlisted GitHub repository.");
        if (
            expectedHead is not null
            && (await CommandAsync("git", root, ["rev-parse", "HEAD"], null, token)).Trim() != expectedHead
        )
            throw new ReviewAccessException(
                "The local checkout is not at this PR revision. Prepare a clean checkout at the selected head before scoping."
            );
        if (
            requireClean
            && (
                await CommandAsync("git", root, ["status", "--porcelain=v1", "--untracked-files=all"], null, token)
            ).Length != 0
        )
            throw new ReviewAccessException(
                "The selected checkout has local changes. Use a clean checkout before scoping or starting a fix."
            );
    }

    private Task<JsonElement> ApiAsync(string endpoint, CancellationToken token) =>
        JsonAsync(["api", "--hostname", "github.com", "--method", "GET", endpoint], null, token);

    private async Task<JsonElement> JsonAsync(string[] args, string? input, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(await CommandAsync(Executable, null, args, input, token));
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ReviewAccessException("GitHub returned an unrecognized response.", 502);
        }
    }

    private static string Login(JsonElement value) =>
        value.GetProperty("author").ValueKind == JsonValueKind.Null
            ? "deleted-user"
            : value.GetProperty("author").GetProperty("login").GetString()!;

    // Every git and gh command runs as a declared Developer tool (docs/adr/0007-tool-registry.md). Reads need no
    // authority; writes run under the approved workflow or fix loop in scope (DeveloperAuthority). Test fixtures
    // override this seam.
    protected virtual async Task<string> CommandAsync(
        string executable,
        string? root,
        string[] args,
        string? input,
        CancellationToken token
    )
    {
        var tools =
            _tools
            ?? throw new ReviewAccessException("GitHub and Git tools are not configured in this workspace.", 503);
        var github = executable != "git";
        try
        {
            string result;
            if (github)
            {
                var contract =
                    GitHubCommands.Classify(args, input)
                    ?? throw new ReviewAccessException("This GitHub operation is not supported.", 409);
                result = await tools.InvokeAsync(contract, new(args, input), token, Authority(contract));
                GitHubHealth.MarkOk();
            }
            else
            {
                var contract =
                    GitCommands.Classify(args)
                    ?? throw new ReviewAccessException("This Git operation is not supported.", 409);
                result = await tools.InvokeAsync(contract, new(root, args), token, Authority(contract));
            }
            return result;
        }
        catch (ToolFailureException failure)
        {
            switch (failure.Kind)
            {
                case "signed-out":
                    GitHubHealth.MarkSignedOut(PullRequestPolicy.Safe(failure.Detail ?? "", 300));
                    throw new GitHubSignInException(GitHubHealth.Message);
                case "clone-failed":
                    throw new ReviewAccessException(RepositoryCheckoutResolver.CloneFailure(failure.Detail ?? ""), 503);
                default:
                    throw new ReviewAccessException(failure.Message, failure.Status);
            }
        }
        catch (ToolRefusedException refused)
        {
            throw new ReviewAccessException(refused.Message, 403);
        }
    }

    private static ToolAuthority? Authority(IToolContract contract) =>
        contract.Risk == ToolRisk.Read ? null : DeveloperAuthority.Current;

    internal Task<string> GitAsync(string root, string[] args, CancellationToken token) =>
        CommandAsync("git", root, args, null, token);
}
