using HireZero.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireZero.Roles.Developer;

// A fix waiting on the owner stops waiting once its PR is merged or closed on GitHub: the strategy is closed out ("pr-closed")
// so the conversation reads as done instead of "Waiting for you" forever. Read-only toward GitHub; one check per PR, every 15 minutes.
public sealed class ClosedPullRequestSweep(
    GitHubReviewStore store,
    GitHubReviewClient github,
    ILogger<ClosedPullRequestSweep> log
) : BackgroundService
{
    public const string ClosedState = "pr-closed";
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public static bool Waiting(PullRequestWorkflow workflow) =>
        workflow.State is "proposed" or "publication-proposed" or "needs-input" or "needs-evidence" or "read-only"
        || workflow.State.EndsWith("-attention", StringComparison.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
                when (error is GitHubSignInException or ReviewAccessException or InvalidOperationException)
            {
                log.LogInformation("Closed-PR sweep skipped: {Message}", error.Message);
            }
            try
            {
                await Task.Delay(Interval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<int> SweepAsync(CancellationToken token)
    {
        var closed = 0;
        foreach (
            var group in store
                .Workflows()
                .Where(Waiting)
                .GroupBy(item => (item.Repository.ToLowerInvariant(), item.Number))
        )
        {
            var evidence = await github.EvidenceAsync(group.First().Repository, group.Key.Number, false, token);
            if (evidence.Open)
                continue;
            foreach (var workflow in group)
            {
                store.SaveWorkflow(
                    workflow with
                    {
                        State = ClosedState,
                        Error = "The PR was merged or closed on GitHub, so this strategy no longer needs a decision.",
                    }
                );
                closed++;
            }
        }
        if (closed > 0)
            AgentActivity.Note(
                "Closed out "
                    + closed
                    + " fix strateg"
                    + (closed == 1 ? "y" : "ies")
                    + " whose PR was merged or closed."
            );
        return closed;
    }
}
