namespace HireZero.Core;

public static class RequestAgentWork
{
    public static AgentTask? Queue(RequestStore requests, AgentStore agents, AgentWorker? worker, WorkRequest item) => requests.Synchronize(() =>
    {
        if (requests.AgentDeclined(item.Id) || requests.PendingReplyKey(item.Id) is not { } pending) return null;
        var task = agents.EnqueueReply(item.Id, item.DisplayTitle, requests.ReplyQueueKey(item.Id, pending));
        worker?.Wake();
        return task;
    });

    // The notice says what actually happens: a reply is queued only when a message is waiting for one.
    public static void Allow(RequestStore requests, AgentStore agents, AgentWorker? worker, WorkRequest item, WorkspaceOwner? owner = null) => requests.Synchronize(() =>
    {
        requests.AllowAgent(item.Id);
        var queued = Queue(requests, agents, worker, item) is not null;
        var who = owner?.Owns(item) == true ? "your message" : item.Requester.Split('@')[0] + "'s request";
        requests.AddOwnerAgentNotice(item.Id, queued
            ? "You gave the agent the go-ahead. It's working on " + who + " now; the answer will appear here."
            : "You gave the agent the go-ahead. It already answered the latest message, so there's nothing to run yet. Reply here with what you want next and it will act on it.",
            "agent-allowed:" + item.Id + ":" + requests.Messages(item.Id).Count());
        return true;
    });
}
