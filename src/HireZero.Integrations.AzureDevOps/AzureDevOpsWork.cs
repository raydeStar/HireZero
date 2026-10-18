using System.Collections.Concurrent;

namespace HireZero.Integrations.AzureDevOps;

// What Azure DevOps knows about a project in the owner's working context: the repository's open and recently
// completed pull requests. A boss sees only the owner's own; a visitor sees counts.
public sealed class AzureDevOpsProjectContext(AzureDevOpsClient client) : IProjectContextContributor
{
    private readonly ConcurrentDictionary<
        string,
        (DateTimeOffset At, AdoPullRequest[] Open, AdoPullRequest[] Completed)
    > _cache = new(StringComparer.OrdinalIgnoreCase);
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<ContextSection[]> ContributeAsync(ProjectRef project, Audience audience, CancellationToken token)
    {
        if (project.Origin is not { Host: AzureDevOpsOriginFormat.Host, Project: { } adoProject } origin)
            return [];
        var key = origin.Organization + "/" + adoProject + "/" + origin.Name;
        if (!_cache.TryGetValue(key, out var cached) || DateTimeOffset.UtcNow - cached.At > CacheFor)
            _cache[key] = cached = (
                DateTimeOffset.UtcNow,
                await client.PullRequestsAsync(
                    origin.Organization,
                    "active",
                    10,
                    token,
                    project: adoProject,
                    repository: origin.Name
                ),
                await client.PullRequestsAsync(
                    origin.Organization,
                    "completed",
                    5,
                    token,
                    project: adoProject,
                    repository: origin.Name
                )
            );
        if (audience == Audience.Visitor)
            return
            [
                new(
                    "Pull requests",
                    cached.Open.Length + " open PR(s); " + cached.Completed.Length + " completed recently."
                ),
            ];
        if (audience == Audience.Boss)
        {
            var me = (await client.MeAsync(origin.Organization, token)).Id;
            var sections = new List<ContextSection>();
            var open = cached.Open.Where(pr => pr.AuthorId == me).ToList();
            var done = cached.Completed.Where(pr => pr.AuthorId == me).ToList();
            if (open.Count > 0)
                sections.Add(
                    new(
                        "Pull requests in progress",
                        string.Join(
                            "\n",
                            open.Select(pr =>
                                "- !" + pr.Id + " " + pr.Title + (pr.Draft ? " (draft)" : "") + " into " + pr.Target
                            )
                        )
                    )
                );
            if (done.Count > 0)
                sections.Add(
                    new(
                        "Pull requests completed",
                        string.Join(
                            "\n",
                            done.Select(pr =>
                                "- !" + pr.Id + " " + pr.Title + " into " + pr.Target + ", completed " + Day(pr.Closed)
                            )
                        )
                    )
                );
            return [.. sections];
        }
        return
        [
            new(
                "Open PRs on Azure DevOps",
                cached.Open.Length == 0
                    ? "None."
                    : string.Join(
                        "\n",
                        cached.Open.Select(pr =>
                            "- !"
                            + pr.Id
                            + " "
                            + pr.Title
                            + " by "
                            + pr.Author
                            + " ("
                            + pr.Source
                            + " into "
                            + pr.Target
                            + ")"
                            + (pr.Draft ? ", draft" : "")
                            + (pr.MergeStatus is "conflicts" ? ", merge conflicts" : "")
                            + Votes(pr.Votes)
                        )
                    )
            ),
            .. cached.Completed.Length == 0
                ? Array.Empty<ContextSection>()
                :
                [
                    new ContextSection(
                        "Recently completed PRs",
                        string.Join(
                            "\n",
                            cached.Completed.Select(pr =>
                                "- !"
                                + pr.Id
                                + " "
                                + pr.Title
                                + " by "
                                + pr.Author
                                + " into "
                                + pr.Target
                                + ", completed "
                                + Day(pr.Closed)
                            )
                        )
                    ),
                ],
        ];
    }

    static string Votes(int[] votes) =>
        votes.Length == 0 ? ""
        : votes.Contains(-10) ? ", rejected by a reviewer"
        : votes.Contains(-5) ? ", waiting for the author"
        : votes.Count(vote => vote >= 5) is var approvals and > 0
            ? ", " + approvals + " approval" + (approvals == 1 ? "" : "s")
        : ", no approvals yet";

    static string Day(string? at) =>
        DateTimeOffset.TryParse(at, out var time) ? time.ToString("yyyy-MM-dd") : "recently";
}

// The owner's open Azure DevOps PRs across the organizations they work in, checked every ten minutes (on shift or not)
// for what needs the author. The brief and the Cockpit read the last result; nothing here changes Azure DevOps.
public sealed class AzureDevOpsWatcher(
    AzureDevOpsClient client,
    WorkingContext working,
    ILogger<AzureDevOpsWatcher> log
) : BackgroundService, IBriefContributor
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(10);
    private volatile AdoAttention[] _latest = [];
    public string? Status { get; private set; }
    public string? CheckedAt { get; private set; }
    public AdoAttention[] Latest => _latest;

    public string[] Organizations(bool configuredOnly = false)
    {
        var configured = (Environment.GetEnvironmentVariable("HIREZERO_AZURE_DEVOPS_ORGS") ?? "").Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return
        [
            .. configured
                .Concat(
                    configuredOnly
                        ? []
                        : working
                            .Projects()
                            .Select(project => project.Origin)
                            .Where(origin => origin?.Host == AzureDevOpsOriginFormat.Host)
                            .Select(origin => origin!.Organization)
                )
                .Where(AzureDevOpsClient.ValidOrganization)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5),
        ];
    }

    public async Task<AdoAttention[]> CheckAsync(CancellationToken token, bool configuredOnly = false)
    {
        var results = new List<AdoAttention>();
        foreach (var organization in Organizations(configuredOnly))
        {
            var me = await client.MeAsync(organization, token);
            foreach (
                var pr in (await client.PullRequestsAsync(organization, "active", 25, token, creatorId: me.Id)).Take(25)
            )
                results.Add(await client.AttentionAsync(pr, token));
        }
        _latest = [.. results];
        CheckedAt = DateTimeOffset.UtcNow.ToString("O");
        Status =
            "Checked "
            + results.Count
            + " open Azure DevOps PR(s); "
            + results.Count(item => item.Problems.Length > 0)
            + " need you.";
        return _latest;
    }

    public BriefItem[] NeedsYou() =>
        [
            .. _latest
                .Where(item => item.Problems.Length > 0 && !item.PullRequest.Draft)
                .Take(8)
                .Select(item => new BriefItem(
                    "azure-devops",
                    item.PullRequest.Repository + " !" + item.PullRequest.Id + ": " + item.PullRequest.Title,
                    string.Join("; ", item.Problems) + ".",
                    null,
                    item.PullRequest.Url
                )),
        ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable("HIREZERO_AZURE_DEVOPS_WATCH"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Status = "Background watch is off. Select organizations and explicitly enable it before polling.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIREZERO_AZURE_DEVOPS_ORGS")))
        {
            Status = "Background watch needs explicitly configured organizations.";
            return;
        }
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken, configuredOnly: true);
            }
            catch (Exception error)
                when (error
                        is WorkspaceException
                            or HttpRequestException
                            or TaskCanceledException
                            or System.Text.Json.JsonException
                            or InvalidOperationException
                )
            {
                if (stoppingToken.IsCancellationRequested)
                    break;
                Status = error is WorkspaceException known ? known.Message : "Azure DevOps couldn't be reached.";
                log.LogWarning("Azure DevOps check failed: {Reason}", Status);
            }
            await Task.Delay(Every, stoppingToken);
        }
    }
}

// The owner's Azure DevOps pull requests for the boss's work log.
public sealed class AzureDevOpsWorkLog(AzureDevOpsClient client, AzureDevOpsWatcher watcher) : IWorkLogSource
{
    public async Task<WorkLogEntry[]> EntriesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        var entries = new List<WorkLogEntry>();
        foreach (var organization in watcher.Organizations())
        {
            var me = (await client.MeAsync(organization, token)).Id;
            foreach (var pr in await client.PullRequestsBetweenAsync(organization, me, "created", from, to, token))
                if (DateTimeOffset.TryParse(pr.Created, out var at) && at >= from && at < to)
                    entries.Add(
                        new(
                            at.ToString("O"),
                            "pr-opened",
                            pr.Repository,
                            "Opened !"
                                + pr.Id
                                + " "
                                + pr.Title
                                + (pr.Draft ? " (draft)" : "")
                                + " in "
                                + pr.Repository
                                + " (into "
                                + pr.Target
                                + ")",
                            pr.Url,
                            new GitOrigin(AzureDevOpsOriginFormat.Host, organization, pr.Project, pr.Repository, true)
                        )
                    );
            foreach (var pr in await client.PullRequestsBetweenAsync(organization, me, "closed", from, to, token))
                if (DateTimeOffset.TryParse(pr.Closed, out var at) && at >= from && at < to)
                    entries.Add(
                        new(
                            at.ToString("O"),
                            "pr-merged",
                            pr.Repository,
                            "Completed !"
                                + pr.Id
                                + " "
                                + pr.Title
                                + " in "
                                + pr.Repository
                                + " (into "
                                + pr.Target
                                + ")",
                            pr.Url,
                            new GitOrigin(AzureDevOpsOriginFormat.Host, organization, pr.Project, pr.Repository, true)
                        )
                    );
        }
        return [.. entries];
    }

    public async Task<WorkLogDetail?> DetailAsync(WorkLogEntry entry, CancellationToken token)
    {
        if (entry.Origin is not { Host: AzureDevOpsOriginFormat.Host } origin || entry.Url is not { } url)
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            url,
            @"/pullrequest/(\d+)$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(50)
        );
        if (!match.Success)
            return null;
        var data = await client.GetAsync(
            origin.Organization,
            "_apis/git/pullrequests/" + match.Groups[1].Value + "?api-version=7.1",
            token
        );
        var pr = AzureDevOpsClient.Parse(origin.Organization, data);
        static string When(string? at) =>
            DateTimeOffset.TryParse(at, out var time) ? time.ToLocalTime().ToString("ddd MMM d, h:mm tt") : at ?? "";
        var status = data.TryGetProperty("status", out var value) ? value.GetString() ?? "" : "";
        var facts = new List<WorkLogFact>
        {
            new("State", (pr.Draft ? "draft, " : "") + status),
            new("Branches", pr.Source + " into " + pr.Target),
            new("Opened", When(pr.Created)),
        };
        if (pr.Closed is not null)
            facts.Add(new(status == "completed" ? "Completed" : "Closed", When(pr.Closed)));
        var approvals = pr.Votes.Count(vote => vote >= 5);
        facts.Add(
            new(
                "Review",
                pr.Votes.Contains(-10) ? "rejected by a reviewer"
                    : pr.Votes.Contains(-5) ? "waiting for the author"
                    : approvals > 0 ? approvals + " approval" + (approvals == 1 ? "" : "s")
                    : "no approvals yet"
            )
        );
        if (status == "active")
        {
            var attention = await client.AttentionAsync(pr, token);
            facts.Add(
                new(
                    "Checks",
                    attention.FailingPolicies.Length > 0 ? "failing: " + string.Join(", ", attention.FailingPolicies)
                        : attention.RunningPolicies.Length > 0
                            ? "waiting: " + string.Join(", ", attention.RunningPolicies)
                        : "nothing failing"
                )
            );
            if (attention.OpenThreads > 0)
                facts.Add(
                    new("Comments", attention.OpenThreads + " open thread" + (attention.OpenThreads == 1 ? "" : "s"))
                );
        }
        return new(
            entry.Text,
            [.. facts],
            WorkLogText.Excerpt(
                data.TryGetProperty("description", out var description) ? description.GetString() : null
            ),
            pr.Url
        );
    }
}

public static class AzureDevOpsServices
{
    public static IServiceCollection AddAzureDevOps(this IServiceCollection services)
    {
        GitRemote.Register(new AzureDevOpsOriginFormat());
        services
            .AddHttpClient("azure-devops", client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<AzureDevOpsClient>();
        services.AddSingleton<IProjectContextContributor, AzureDevOpsProjectContext>();
        services.AddSingleton<AzureDevOpsWatcher>();
        services.AddSingleton<IBriefContributor>(provider => provider.GetRequiredService<AzureDevOpsWatcher>());
        services.AddSingleton<IWorkLogSource, AzureDevOpsWorkLog>();
        services.AddHostedService(provider => provider.GetRequiredService<AzureDevOpsWatcher>());
        return services;
    }

    // Owner only: the latest check of the owner's open PRs, and a way to run it now.
    public static void MapAzureDevOps(this WebApplication app)
    {
        app.MapGet(
            "/api/azure-devops/pulls",
            (HttpContext context, AzureDevOpsWatcher watcher) =>
                !WorkspaceAccess.IsOwner(context)
                    ? Results.StatusCode(403)
                    : Results.Ok(
                        new
                        {
                            status = watcher.Status,
                            checkedAt = watcher.CheckedAt,
                            organizations = watcher.Organizations(),
                            pulls = watcher.Latest.Select(View),
                        }
                    )
        );
        app.MapPost(
                "/api/azure-devops/pulls/check",
                async (HttpContext context, AzureDevOpsWatcher watcher, CancellationToken token) =>
                    !WorkspaceAccess.IsOwner(context)
                        ? Results.StatusCode(403)
                        : Results.Ok(
                            new
                            {
                                status = (await watcher.CheckAsync(token)).Length + " checked",
                                pulls = watcher.Latest.Select(View),
                            }
                        )
            )
            .RequireRateLimiting("agent");
    }

    static object View(AdoAttention item) =>
        new
        {
            id = item.PullRequest.Id,
            title = item.PullRequest.Title,
            project = item.PullRequest.Project,
            repository = item.PullRequest.Repository,
            source = item.PullRequest.Source,
            target = item.PullRequest.Target,
            draft = item.PullRequest.Draft,
            mergeStatus = item.PullRequest.MergeStatus,
            url = item.PullRequest.Url,
            failingPolicies = item.FailingPolicies,
            runningPolicies = item.RunningPolicies,
            openThreads = item.OpenThreads,
            problems = item.Problems,
        };
}
