using System.Text.Json;

namespace HireZero.Roles.Developer;

public sealed record PullRequestSignal(string Repository, int Number, string HeadSha, string Title, string Reason);

// Code-only sensing: the owner's open PRs with failing checks or unresolved review threads, skipping any
// that already have a live fix strategy for that revision. Each finding proposes one diagnosis per revision.
public sealed class GitHubShiftSensor(
    GitHubReviewClient github,
    GitHubReviewStore store,
    ShiftProposalStore? proposals = null,
    AgentStore? agents = null,
    WorkspaceOwner? owner = null
) : IShiftSensor
{
    public async Task<(ProactiveWork[] Work, string Note)> SenseAsync(CancellationToken token)
    {
        // Assigned-issue reads/publication are omitted from this activation candidate pending independent review.
        _ = proposals;
        _ = owner;
        var shift = agents?.Shift();
        if (shift is { On: false })
            return ([], "Repository discovery is off shift.");
        var account = await github.IdentityAsync(token);
        var work = new List<ProactiveWork>();
        var live = store.Workflows(account.Id).Where(w => w.State is not ("merged" or "canceled")).ToArray();
        var repositories = store
            .Repositories(account.Id)
            .DistinctBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        if (repositories.Length == 0)
        {
            return (
                [],
                "No repositories are configured for shift discovery. Select exact repositories in Settings; no organization-wide discovery ran."
            );
        }
        var candidates = new List<(ReviewRepository Repository, CodeReview Review)>();
        var failed = 0;

        foreach (var repository in repositories)
        {
            token.ThrowIfCancellationRequested();
            if (agents is not null && shift is not null && !agents.WithCurrentShift(shift, () => { }))
                return ([], "Shift changed or expired; repository discovery stopped.");
            try
            {
                foreach (var review in (await github.ReviewsAsync(repository, account, token)).Take(8))
                    candidates.Add((repository, review));
            }
            catch (Exception error)
                when (error is WorkspaceException or HttpRequestException or JsonException or InvalidOperationException)
            {
                failed++;
            }
        }
        foreach (var (repository, review) in candidates.Take(24))
        {
            if (agents is not null && shift is not null && !agents.WithCurrentShift(shift, () => { }))
                return ([], "Shift changed or expired; repository discovery stopped.");
            if (!GitHubReviewPolicy.Owned(account, repository, review))
                continue;
            PullRequestEvidence evidence;
            try
            {
                evidence = await github.EvidenceAsync(repository.Name, review.Number, false, token);
            }
            catch (Exception error)
                when (error is WorkspaceException or HttpRequestException or JsonException or InvalidOperationException)
            {
                failed++;
                continue;
            }
            if (
                !evidence.Repository.Equals(repository.Name, StringComparison.OrdinalIgnoreCase)
                || evidence.RepositoryId != repository.GitHubId
            )
                continue;
            if (
                !evidence.Open
                || evidence.Draft
                || !evidence.Author.Equals(account.Login, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            if (
                live.Any(w =>
                    w.Repository.Equals(evidence.Repository, StringComparison.OrdinalIgnoreCase)
                    && w.Number == evidence.Number
                    && w.HeadSha == evidence.HeadSha
                )
            )
                continue;
            var failing = evidence.Checks.Count(check =>
                check.State is "failure" or "error" or "timed_out" or "action_required"
            );
            var threads = evidence.Threads.Count(thread => !thread.Resolved && !thread.Outdated);
            if (failing == 0 && threads == 0)
                continue;
            var reason = string.Join(
                " and ",
                new[]
                {
                    failing > 0 ? failing + " failing check" + (failing == 1 ? "" : "s") : null,
                    threads > 0 ? threads + " unresolved thread" + (threads == 1 ? "" : "s") : null,
                }.OfType<string>()
            );
            var signal = new PullRequestSignal(
                evidence.Repository,
                evidence.Number,
                evidence.HeadSha,
                evidence.Title,
                reason
            );
            work.Add(
                new(
                    DiagnosePullRequestHandler.TaskKind,
                    DiagnosePullRequestHandler.Key(signal),
                    "Diagnose " + signal.Repository + " #" + signal.Number + ": " + reason,
                    JsonSerializer.Serialize(signal, DiagnosePullRequestHandler.Json)
                )
            );
        }
        var allowed = store
            .Repositories(account.Id)
            .Select(repository => (repository.Name.ToLowerInvariant(), repository.GitHubId))
            .ToHashSet();
        work = work.Where(item =>
            {
                var signal = JsonSerializer.Deserialize<PullRequestSignal>(
                    item.Target!,
                    DiagnosePullRequestHandler.Json
                )!;
                return repositories.Any(repository =>
                    repository.Name.Equals(signal.Repository, StringComparison.OrdinalIgnoreCase)
                    && allowed.Contains((repository.Name.ToLowerInvariant(), repository.GitHubId))
                );
            })
            .ToList();
        return (
            [.. work],
            "Checked up to 12 explicitly configured repositories, up to 8 owner-authored PRs per repository and 24 evidence packets: "
                + work.Count
                + " PRs need attention; assigned-issue discovery is omitted pending review; "
                + failed
                + " checks unavailable. Work and personal repositories use the same exact scope; no organization-wide scan ran."
        );
    }
}

// Opens a conversation for a sensed PR and proposes a fix strategy there; coding still waits for approval.
public sealed class DiagnosePullRequestHandler(RequestStore requests, WorkspaceOwner owner) : IAgentTaskHandler
{
    public const string TaskKind = "diagnose-pr";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Key(PullRequestSignal signal) =>
        "pr:" + signal.Repository.ToLowerInvariant() + "#" + signal.Number + "@" + signal.HeadSha;

    public string Kind => TaskKind;

    public Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token)
    {
        var signal = JsonSerializer.Deserialize<PullRequestSignal>(task.Target!, Json)!;
        var url = "https://github.com/" + signal.Repository + "/pull/" + signal.Number;
        var request = requests.CreateRequest(
            owner.Login,
            signal.Repository,
            "PR fix: " + signal.Repository + " #" + signal.Number,
            "Observed "
                + signal.Reason
                + " on "
                + url
                + " ("
                + signal.Title
                + "). Diagnosis needs owner approval for this revision.",
            "shift:" + Key(signal)
        );
        if (canceled())
        {
            requests.ArchiveRequest(request.Id);
            return Task.FromResult(new AgentTaskResult("Canceled; the observation was archived.", request.Id));
        }
        requests.AddOwnerAgentNotice(
            request.Id,
            "Observed "
                + signal.Reason
                + " at revision "
                + signal.HeadSha
                + ". Ask explicitly to diagnose this revision before a model or repair workflow runs.",
            "shift:" + Key(signal) + ":approval"
        );
        return Task.FromResult(
            new AgentTaskResult("Recorded PR evidence; diagnosis waits for your approval.", request.Id)
        );
    }
}

// Owner conversations about GitHub (connect a repository, inspect a PR) are answered with real evidence.
public sealed class RepositoryChatResponder(
    DeveloperPrompts prompts,
    GitHubReviewClient github,
    GitHubReviewStore reviews,
    GitHubChatClassifier classifier,
    RequestStore requests
) : IConversationResponder
{
    public async Task<string?> RespondAsync(
        WorkRequest request,
        IReadOnlyList<ConversationMessage> messages,
        bool ownerConversation,
        string? context,
        CancellationToken token
    )
    {
        if (!ownerConversation)
            return null;
        // With a known project in view, broad questions get the context-aware answer; this responder keeps PR-specific asks
        // (a numbered PR to diagnose or fix, or connecting and automating a repository).
        var latest =
            messages.LastOrDefault(note => note.Author != "workspace-agent" && note.Visibility != "owner")?.Body
            ?? request.Title + " " + request.Description;
        if (context is not null && !GitHubRepositoryChat.Specific(latest))
            return null;
        return await GitHubRepositoryChat.ProposeAsync(
            request,
            messages,
            prompts,
            github,
            reviews,
            requests,
            classifier,
            token,
            deferBroad: context is not null
        );
    }
}

// The developer's voice for everything else in a development conversation.
public sealed class DeveloperConversationResponder(DeveloperPrompts prompts) : IConversationResponder
{
    public async Task<string?> RespondAsync(
        WorkRequest request,
        IReadOnlyList<ConversationMessage> messages,
        bool ownerConversation,
        string? context,
        CancellationToken token
    ) =>
        await prompts.ReplyAsync(
            ownerConversation ? IModelGateway.OwnerCell : request.Requester,
            request,
            messages,
            token,
            context,
            ownerConversation
        );
}

// The daily brief's "Needs you" items from this role: PR fixes waiting on the owner's approval, answer or merge.
public sealed class DeveloperBrief(GitHubReviewStore store) : IBriefContributor
{
    // Workflows come newest first; only the latest one per PR counts.
    public BriefItem[] NeedsYou() =>
        [
            .. (
                GitHubHealth.SignedOutSince is null
                    ? []
                    : new[] { new BriefItem("github", "Reconnect GitHub", GitHubHealth.Message) }
            ),
            .. store
                .Workflows()
                .DistinctBy(workflow => (workflow.Repository.ToLowerInvariant(), workflow.Number))
                .Select(workflow =>
                    (
                        workflow,
                        detail: workflow.State switch
                        {
                            "proposed" => "A fix strategy is waiting for your approval.",
                            "publication-proposed" =>
                                "Validated changes are waiting for your approval to commit and push.",
                            "needs-input" => "The fix has a question for you.",
                            "published" => "The fix is pushed. Merge or close the PR when you're ready.",
                            "needs-attention"
                            or "handoff-attention"
                            or "validation-attention"
                            or "business-rule-attention"
                            or "merge-attention" => "The fix stopped and needs a look"
                                + (workflow.Error is { Length: > 0 } error ? ": " + error : "."),
                            _ => null,
                        }
                    )
                )
                .Where(item => item.detail is not null)
                .Take(8)
                .Select(item => new BriefItem(
                    "pull-request",
                    item.workflow.Repository + " #" + item.workflow.Number,
                    item.detail!,
                    item.workflow.RequestId,
                    "https://github.com/" + item.workflow.Repository + "/pull/" + item.workflow.Number
                )),
        ];
}

// The owner's GitHub pull requests for the boss's work log.
public sealed class GitHubWorkLog(GitHubReviewClient github) : IWorkLogSource
{
    public Task<WorkLogEntry[]> EntriesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token) =>
        GitHubHealth.SignedOutSince is not null
            ? Task.FromResult<WorkLogEntry[]>([])
            : github.MyPullRequestsAsync(from, to, token);

    public Task<WorkLogDetail?> DetailAsync(WorkLogEntry entry, CancellationToken token) =>
        entry.Origin?.Host == GitOrigin.GitHub && entry.Url is { } url
            ? github.PullRequestDetailAsync(url, entry.Text, token)
            : Task.FromResult<WorkLogDetail?>(null);
}

// What the developer role knows about a project: HireZero's PR fix work and the repository's open and recently merged PRs.
public sealed class DeveloperProjectContext(GitHubReviewStore store, GitHubReviewClient github)
    : IProjectContextContributor
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (DateTimeOffset At, PullRequestLine[] Open, PullRequestLine[] Merged)
    > _cache = new(StringComparer.OrdinalIgnoreCase);
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<ContextSection[]> ContributeAsync(ProjectRef project, Audience audience, CancellationToken token)
    {
        if (project.Repository is not { } repository || project.Origin?.Host == GitOrigin.AzureDevOps)
            return [];
        var sections = new List<ContextSection>();
        static int Progress(PullRequestWorkflow w) =>
            w.State switch
            {
                "merged" => 5,
                "published" => 4,
                "pushing" or "committing" => 3,
                "coding" or "validating" => 2,
                "proposed" => 0,
                _ => 1,
            };
        var fixes = store
            .Workflows()
            .Where(w => w.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase))
            .GroupBy(w => w.Number)
            .Select(group => group.OrderByDescending(Progress).First())
            .Take(6)
            .ToList();
        if (fixes.Count > 0 && audience == Audience.Owner)
            sections.Add(
                new(
                    "PR fixes HireZero worked on",
                    string.Join(
                        "\n",
                        fixes.Select(w =>
                            "- #"
                            + w.Number
                            + ": "
                            + w.State.Replace('-', ' ')
                            + (w.CommitSha is { Length: > 0 } sha ? ", pushed " + sha[..7] : "")
                            + ". Strategy: "
                            + PullRequestPolicy.Safe(w.Strategy.Summary, 300)
                        )
                    )
                )
            );
        if (GitHubHealth.SignedOutSince is not null)
            return [.. sections];
        if (!_cache.TryGetValue(repository, out var cached) || DateTimeOffset.UtcNow - cached.At > CacheFor)
            _cache[repository] = cached = (
                DateTimeOffset.UtcNow,
                await github.PullRequestsAsync(repository, "open", 10, token),
                await github.PullRequestsAsync(repository, "merged", 5, token)
            );
        if (audience == Audience.Visitor)
        {
            sections.Add(
                new("Pull requests", cached.Open.Length + " open PR(s); " + cached.Merged.Length + " merged recently.")
            );
            return [.. sections];
        }
        if (audience == Audience.Boss)
        {
            var me = (await github.IdentityAsync(token)).Login;
            var opened = cached.Open.Where(pr => pr.Author.Equals(me, StringComparison.OrdinalIgnoreCase)).ToList();
            var merged = cached.Merged.Where(pr => pr.Author.Equals(me, StringComparison.OrdinalIgnoreCase)).ToList();
            if (opened.Count > 0)
                sections.Add(
                    new(
                        "Pull requests in progress",
                        string.Join(
                            "\n",
                            opened.Select(pr => "- #" + pr.Number + " " + pr.Title + (pr.Draft ? " (draft)" : ""))
                        )
                    )
                );
            if (merged.Count > 0)
                sections.Add(
                    new(
                        "Pull requests merged",
                        string.Join(
                            "\n",
                            merged.Select(pr => "- #" + pr.Number + " " + pr.Title + ", merged " + Day(pr.At))
                        )
                    )
                );
            return [.. sections];
        }
        sections.Add(
            new(
                "Open PRs on GitHub",
                cached.Open.Length == 0
                    ? "None."
                    : string.Join(
                        "\n",
                        cached.Open.Select(pr =>
                            "- #"
                            + pr.Number
                            + " "
                            + pr.Title
                            + " by "
                            + pr.Author
                            + (pr.Draft ? " (draft)" : "")
                            + (pr.Review.Length > 0 ? ", review " + pr.Review.ToLowerInvariant().Replace('_', ' ') : "")
                            + ", updated "
                            + Day(pr.At)
                        )
                    )
            )
        );
        if (cached.Merged.Length > 0)
            sections.Add(
                new(
                    "Recently merged PRs",
                    string.Join(
                        "\n",
                        cached.Merged.Select(pr =>
                            "- #" + pr.Number + " " + pr.Title + " by " + pr.Author + ", merged " + Day(pr.At)
                        )
                    )
                )
            );
        return [.. sections];
    }

    static string Day(string at) =>
        DateTimeOffset.TryParse(at, out var time) ? time.ToString("yyyy-MM-dd") : "recently";
}
