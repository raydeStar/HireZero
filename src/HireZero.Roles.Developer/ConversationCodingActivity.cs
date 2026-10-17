using HireZero.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HireZero.Roles.Developer;

// The coding runs behind a conversation's PR fixes, as a quiet step log: a command shows once when it finishes
// (its "starting" line only while it is still the latest thing happening).
public sealed class ConversationCodingActivity(GitHubReviewStore store, IServiceProvider services)
    : IConversationActivity
{
    public ActivityStream[] For(string requestId)
    {
        if (services.GetService<ICodingRunner>() is not { } coding)
            return [];
        return
        [
            .. store
                .Workflows()
                .Where(workflow => workflow.RequestId == requestId && workflow.CodingRunId is not null)
                .Select(workflow => (workflow, run: coding.Find(workflow.CodingRunId!)))
                .Where(item => item.run is not null)
                .DistinctBy(item => item.run!.Id)
                .Take(3)
                .Select(item => Outcome(item.workflow, item.run!)),
        ];
    }

    // Sol finishing isn't the end: the fix is then checked, committed and pushed, or it pauses for the owner. The stream
    // reports that outcome so "completed" never reads as done when it's actually waiting on you.
    public static ActivityStream Outcome(PullRequestWorkflow workflow, CodingRun run)
    {
        var steps = Steps(run).ToList();
        if (run.State != "completed")
            return new("Coding run", run.State, run.CreatedAt, [.. steps]);
        var (state, note) = workflow.State switch
        {
            "validating" or "committing" or "pushing" => (
                "running",
                "Checking Sol's work before it's committed and pushed…"
            ),
            "published" or "merge-pending" or "merged" => (
                "pushed",
                "Pushed " + (workflow.CommitSha is { Length: >= 8 } sha ? sha[..8] + " " : "") + "to the PR."
            ),
            "canceled" => ("canceled", "Canceled; nothing was committed."),
            "coding" => ("running", "Sol finished; checking the result…"),
            _ => ("needs you", "Paused, needs you: " + FirstSentence(workflow.Error ?? "see the fix card above.")),
        };
        steps.Insert(
            0,
            new(
                DateTimeOffset.UtcNow.ToString("O") is var now && state == "running" ? now : run.UpdatedAt,
                note,
                run.Id,
                state == "needs you" ? "warning" : null
            )
        );
        return new(
            (workflow.Round is { } round ? "Round " + round + " · " : "")
                + "Fix "
                + workflow.Repository.Split('/')[^1]
                + " #"
                + workflow.Number,
            state,
            run.CreatedAt,
            [.. steps]
        );
    }

    static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 && end < 220 ? text[..(end + 1)]
            : text.Length <= 220 ? text
            : text[..219] + "…";
    }

    public static ActivityEntry[] Steps(CodingRun run)
    {
        var last = run.Events.Length - 1;
        var steps = run
            .Events.Where(
                (item, index) => item.Type != "command-started" || index == last && run.State is "running" or "queued"
            )
            .Select(item => new ActivityEntry(item.At, item.Message, run.Id, item.Type == "warning" ? "warning" : null))
            .Reverse()
            .Take(80)
            .ToList();
        if (run.Error is { } error && run.State is "failed" or "interrupted")
            steps.Insert(0, new(run.UpdatedAt, "Stopped: " + error, run.Id, "warning"));
        return [.. steps];
    }
}
