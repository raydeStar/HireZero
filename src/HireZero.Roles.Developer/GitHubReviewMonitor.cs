using System.Collections.Concurrent;

namespace HireZero.Roles.Developer;

public sealed class GitHubReviewMonitor(GitHubReviewStore store, GitHubReviewClient github, GitHubFixCoordinator coordinator) : BackgroundService
{
    private readonly ConcurrentDictionary<string, string> _status = new();
    public string Status(string id) => _status.GetValueOrDefault(id, "Review observation has not run; diagnosis and repair require scoped owner approval.");
    public async Task CheckAsync(string id, CancellationToken token)
    {
        await coordinator.Gate.WaitAsync(token);
        try
        {
            var repository = await coordinator.AuthorizedAsync(id, token);
            if (!store.Automatic(id)) return;
            await github.InspectAsync(repository, null, false, token);
            var head = (await github.HeadAsync(repository, token)).Trim();
            var reviews = await github.ReviewsAsync(repository, await github.IdentityAsync(token), token);
            var findings = reviews.Where(item => item.Complete && item.HeadSha == head).Sum(review => review.Threads.Count(GitHubReviewPolicy.Actionable));
            _status[id] = reviews.Any(review => review.HeadSha == head) ? "Observed " + findings + " review findings; diagnosis and repair wait for scoped owner approval."
                : "The prepared checkout does not match a current open PR revision. Prepare the intended PR checkout before automatic fixes can run.";
        }
        catch (ReviewAccessException error) { _status[id] = error.Message; }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or IOException)
        { _status[id] = "Automatic review checking could not be validated. Refresh the repository and inspect SemaForge."; }
        finally { coordinator.Gate.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HIREZERO_GITHUB_REVIEW_WATCH"), "true", StringComparison.OrdinalIgnoreCase)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!store.HasAutomatic()) continue;
            GitHubIdentity account;
            try { account = await github.IdentityAsync(stoppingToken); }
            catch (ReviewAccessException) { continue; }
            foreach (var repository in store.Repositories(account.Id).Where(item => store.Automatic(item.Id)))
                await CheckAsync(repository.Id, stoppingToken);
        }
    }
}
