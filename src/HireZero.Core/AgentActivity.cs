namespace HireZero.Core;

public sealed record ActivityEntry(string At, string Text, string? TaskId, string? Detail = null);
// One piece of work behind a conversation (an agent reply, a coding run) and its steps, newest first.
public sealed record ActivityStream(string Label, string State, string StartedAt, ActivityEntry[] Steps);
// Hired roles add the long-running work they started for a conversation (a coding run, for example).
public interface IConversationActivity { ActivityStream[] For(string requestId); }

// What the agent is doing right now, step by step, for the Cockpit and the waiting chat line.
// Kept in memory only (the last 50 steps); anything may call Note, and the step is tagged with the task
// that is running on this async flow, so deep code (planners, the model gateway) needs no plumbing.
public static class AgentActivity
{
    private static readonly AsyncLocal<string?> CurrentTask = new();
    private static readonly LinkedList<ActivityEntry> Entries = new();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Leases = [];
    public static bool IsRunning(string taskId) { lock (Gate) return Leases.ContainsKey(taskId); }
    // Each task's own steps (the last 200 for each of the 40 latest tasks), so a conversation's log isn't crowded out.
    private static readonly Dictionary<string, LinkedList<ActivityEntry>> ByTask = [];
    private static readonly LinkedList<string> TaskOrder = new();

    public static IDisposable Begin(string taskId)
    {
        var previous = CurrentTask.Value; CurrentTask.Value = taskId;
        lock (Gate) Leases[taskId] = Leases.GetValueOrDefault(taskId) + 1;
        return new Scope(() => { CurrentTask.Value = previous; lock (Gate) { if (--Leases[taskId] == 0) Leases.Remove(taskId); } });
    }
    public static void Note(string text, string? detail = null)
    {
        text = ContentGuard.Mask(text); detail = detail is null ? null : ContentGuard.Mask(detail);
        lock (Gate)
        {
            var entry = new ActivityEntry(DateTimeOffset.UtcNow.ToString("O"), text.Length > 160 ? text[..160] : text, CurrentTask.Value,
                detail is null ? null : detail.Length > 400 ? detail[..400] : detail);
            Entries.AddFirst(entry);
            while (Entries.Count > 50) Entries.RemoveLast();
            if (entry.TaskId is not { } task) return;
            if (!ByTask.TryGetValue(task, out var steps))
            {
                ByTask[task] = steps = new(); TaskOrder.AddFirst(task);
                while (TaskOrder.Count > 40) { ByTask.Remove(TaskOrder.Last!.Value); TaskOrder.RemoveLast(); }
            }
            steps.AddFirst(entry);
            while (steps.Count > 200) steps.RemoveLast();
        }
    }
    public static ActivityEntry[] Recent(int count) { lock (Gate) return [.. Entries.Take(count)]; }
    public static ActivityEntry? LatestFor(string taskId) { lock (Gate) return ByTask.TryGetValue(taskId, out var steps) ? steps.First?.Value : null; }
    public static ActivityEntry[] ForTask(string taskId, int count) { lock (Gate) return ByTask.TryGetValue(taskId, out var steps) ? [.. steps.Take(count)] : []; }

    private sealed class Scope(Action end) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) end(); }
    }
}
