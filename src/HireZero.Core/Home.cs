using System.Text;

namespace HireZero.Core;

// Hired roles say when a message in the owner's Home chat is real work that deserves its own task thread, and give it a
// stable key (for example "github:Example/ExampleWidgetProject#216") so asking again reuses the open thread.
public interface ITaskThreadSignal
{
    string? TaskKey(string text);
}

// Home: the owner's one perpetual chat. Work spun off from it lives in task threads; their notices and first answers come
// back here as short follow-ups with a link, so Home reads like a running log of what's happening.
public sealed class HomeFeed(RequestStore requests, WorkspaceOwner owner)
{
    public void Attach() => requests.NoticeAdded += Mirror;

    public void TaskFinished(AgentTask task)
    {
        if (task.RequestId is not { } id || requests.IsHome(id))
            return;
        requests.AddOwnerAgentNotice(
            id,
            task.State == "done"
                ? "Completed: " + task.Title + ". Open this conversation for the result."
                : "Blocked: " + task.Title + ". Open this conversation for the recorded error.",
            "agent-outcome:" + task.Id + ":" + task.State
        );
    }

    // An owner-only notice on any other thread (a fix stopped, someone asked for access…) is echoed into Home.
    void Mirror(string requestId, string body, string noticeId)
    {
        if (noticeId.StartsWith("home:", StringComparison.Ordinal))
            return;
        var home = requests.Home(owner.Login);
        if (home.Id == requestId || requests.FindRequest(requestId) is not { } item)
            return;
        foreach (
            var target in new[] { home, requests.FindHome(WorkspaceOwner.LocalIdentity) }
                .OfType<WorkRequest>()
                .DistinctBy(value => value.Id)
        )
            if (target.Id != requestId)
                requests.AddOwnerAgentNotice(
                    target.Id,
                    "**" + item.DisplayTitle + "**: " + Excerpt(body) + " [Open](request:" + requestId + ")",
                    "home:" + target.Id + ":" + noticeId
                );
    }

    // A task thread started from Home answered for the first time: say so in Home.
    public void TaskReplied(string requestId, string answer)
    {
        if (
            requests.HomeTask(requestId) is not { } homeId
            || requests.Messages(requestId).Count(message => message.Author == "workspace-agent") != 1
        )
            return;
        var item = requests.FindRequest(requestId)!;
        requests.AddOwnerAgentNotice(
            homeId,
            "**" + item.DisplayTitle + "** has an answer: " + Excerpt(answer) + " [Open](request:" + requestId + ")",
            "home:first-reply:" + requestId
        );
    }

    // What's open, for the Home chat's answers ("how's #216 going?"): newest activity first, with each thread's latest notice.
    public string Digest(IEnumerable<string> busy)
    {
        var running = busy.ToHashSet(StringComparer.Ordinal);
        var lines = new StringBuilder();
        foreach (
            var item in requests
                .Conversations(null, null, 40)
                .Where(item => !item.Home && item.Status != "archived")
                .Take(12)
        )
        {
            var latest = requests.LatestOwnerNotice(item.Id);
            lines
                .Append("- ")
                .Append(item.Title)
                .Append(" [request:")
                .Append(item.Id)
                .Append("] · ")
                .Append(running.Contains(item.Id) ? "agent working now" : item.Status)
                .Append(" · last activity ")
                .Append(item.LastAt[..Math.Min(16, item.LastAt.Length)].Replace('T', ' '))
                .Append(" UTC")
                .Append(latest is null ? "" : " · latest: " + Excerpt(latest))
                .Append('\n');
        }
        return lines.Length == 0 ? "" : ContentGuard.AsData("open work", ContentGuard.Clean(lines.ToString(), 6000));
    }

    static string Excerpt(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        var end = flat.IndexOfAny(['.', '!', '?'], Math.Min(60, flat.Length));
        var cut =
            end > 0 && end < 240 ? flat[..(end + 1)]
            : flat.Length <= 240 ? flat
            : flat[..239].TrimEnd() + "…";
        return cut;
    }
}
