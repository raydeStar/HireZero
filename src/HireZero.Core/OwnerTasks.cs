using Microsoft.Data.Sqlite;
using static HireZero.Core.OwnerTaskText;

namespace HireZero.Core;

public sealed record OwnerTask(string Id, string Title, string Source, string? Url, string? RequestId, string Status,
    string UpdatedAt, string Owner, string Priority, string[] Blockers, string NextAction, string Approval,
    string LocalState = "open", string[]? Provenance = null, string? ExecutionState = null, string? Kind = null, string? Detail = null, string? Start = null);
public interface IOwnerTaskContributor { OwnerTask[] Tasks(); }
public sealed record OwnerTaskChange(string State, string Priority);

// Presentation metadata is local to HireZero. Dismiss/resolve never calls a connector or changes source status.
public sealed class OwnerTaskStore(string path)
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS owner_task_views(id TEXT PRIMARY KEY,state TEXT NOT NULL,priority TEXT NOT NULL)";
        command.ExecuteNonQuery(); return connection;
    }
    public OwnerTask Apply(OwnerTask task)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT state,priority FROM owner_task_views WHERE id=$id"; command.Parameters.AddWithValue("$id", task.Id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? task with { LocalState = reader.GetString(0), Priority = reader.GetString(1) } : task;
    }
    public void Save(string id, OwnerTaskChange input)
    {
        if (input.State is not ("open" or "dismissed" or "resolved") || input.Priority is not ("high" or "normal" or "low"))
            throw new WorkspaceException("Choose a local state and priority.", 400);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO owner_task_views VALUES($id,$state,$priority) ON CONFLICT(id) DO UPDATE SET state=$state,priority=$priority";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$state", input.State); command.Parameters.AddWithValue("$priority", input.Priority);
        command.ExecuteNonQuery();
    }
}
public static class OwnerTaskText
{
    // One plain line for a hover: whitespace collapsed, bounded.
    // What a conversation is asking for, from its title and opening text: a PR or build fix, a question, or a general request.
    public static string RequestKind(WorkRequest item)
    {
        var text = item.Title + " " + item.Description;
        bool Says(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
        var pr = Says(@"/pull/\d+|\bPR\s*#?\d+|#\d+|pull request");
        if (pr && Says(@"\b(build|ci|check|pipeline)\b") && Says(@"\b(fail|failing|failure|broken|red)\b")) return "Build fix";
        if (pr && Says(@"\b(fix|repair|address|resolve|bugs?)\b")) return "PR fix";
        if (pr) return "PR review";
        if (Says(@"\b(connect|hook up|set up|configure)\b")) return "Setup";
        return item.Title.TrimEnd().EndsWith('?') ? "Question" : "Request";
    }

    public static string? Brief(string? text, int limit = 280)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= limit ? flat : flat[..(limit - 1)].TrimEnd() + "…";
    }
}
public sealed class OwnerTaskBoard(RequestStore requests, AgentStore agents, TriageStore triage, WorkspaceOwner owner,
    OwnerTaskStore views, IEnumerable<IOwnerTaskContributor> contributors)
{
    public OwnerTask[] Tasks()
    {
        var queue = agents.OpenTasks();
        var local = requests.Requests(null).Where(item => item.Status != "archived").Select(item =>
        {
            var decision = triage.Find(item.Id)?.Decision;
            // Waiting on the owner only when a message is actually waiting for the go-ahead; answered conversations are not.
            var pending = !requests.CanUseAgent(item, owner) && requests.PendingReplyKey(item.Id) is not null;
            var active = queue.FirstOrDefault(task => task.RequestId == item.Id);
            return new OwnerTask("request:" + item.Id, item.DisplayTitle, "HireZero request", null, item.Id,
                active?.State ?? item.Status, active?.UpdatedAt ?? requests.Messages(item.Id).LastOrDefault()?.CreatedAt ?? item.CreatedAt, owner.Login, "normal",
                decision == "declined" ? ["Declined; agent work is blocked."] : pending ? ["Agent work needs owner approval."] : [],
                pending ? "Give the go-ahead or reply in the conversation." : "Open the conversation if you want to continue it.", pending ? "needs-owner" : "request-allowed",
                Kind: RequestKind(item) + (item.Requester == owner.Login || item.Requester == WorkspaceOwner.LocalIdentity ? "" : " from " + item.Requester.Split('@')[0]),
                Detail: Brief(item.Description), Start: pending && decision != "declined" ? "allow" : null);
        });
        // A workflow linked to a request replaces its generic row while retaining both origins.
        // A conversation is a task only while it waits on the owner or has agent work queued or running; answered ones live in the sidebar.
        // Closed-out work (PR merged or closed, canceled) is history in its conversation, not a task.
        var roleTasks = contributors.SelectMany(source => source.Tasks()).Where(task => task.Status is not ("pr-closed" or "canceled" or "merged")).ToArray();
        local = local.Where(task => task.Approval == "needs-owner" || queue.Any(item => item.RequestId == task.RequestId) || roleTasks.Any(role => role.RequestId == task.RequestId));
        return local.Concat(roleTasks)
            .GroupBy(item => item.RequestId is { } id ? "request:" + id : item.Id)
            .Select(group => views.Apply(group.Last() with { Id = group.Key, Provenance = [.. group.Select(item => item.Source).Distinct()] }))
            .OrderBy(item => item.LocalState != "open").ThenBy(item => item.Priority == "high" ? 0 : item.Priority == "low" ? 2 : 1)
            .ThenByDescending(item => item.UpdatedAt).Take(200).ToArray();
    }
}
