using System.Collections.Concurrent;

namespace HireZero.Core;

// Only message ids admitted by the current local-owner HTTP request can cause a model call.
public sealed class FreshOwnerReplies(RequestStore requests, AgentStore agents, IModelGateway model, string owner) : IAgentTaskHandler
{
    private readonly ConcurrentDictionary<string, ConversationMessage> _admitted = new();
    private readonly ConcurrentDictionary<string, ConversationMessage> _freshRequests = new();
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    public bool IsFreshRequest(WorkRequest request) => HasAdmission(request.Id) || DateTimeOffset.TryParse(request.CreatedAt, out var created) && created >= _started;
    public string Kind => "reply";
    public AgentTask Admit(WorkRequest request, ConversationMessage message)
    {
        if (!request.Requester.Equals(owner, StringComparison.OrdinalIgnoreCase) || RequestStore.AgentRequestClosed(request.Status) || message.Author != owner)
            throw new WorkspaceException("Start a fresh owner conversation to chat.", 409);
        if (ContentGuard.Clean(message.Body, 4000).Flags.Length > 0) throw new WorkspaceException("Remove credential-like content before asking the model.", 400);
        if (message.RequestId != request.Id || !_freshRequests.Values.Any(item => item.Id == message.Id) &&
            (!DateTimeOffset.TryParse(message.CreatedAt, out var created) || created < _started))
            throw new WorkspaceException("Historical messages cannot authorize a reply. Send a fresh message.", 409);
        _admitted[message.Id] = message;
        var task = agents.EnqueueReply(request.Id, request.DisplayTitle, "fresh-owner:" + message.Id);
        _freshRequests[request.Id] = message; return task;
    }

    public bool HasAdmission(string requestId) => _freshRequests.ContainsKey(requestId);
    public void Retry(string requestId)
    {
        if (!_freshRequests.TryGetValue(requestId, out var message) || requests.FindRequest(requestId) is not { } request)
            throw new WorkspaceException("Send a fresh message to request a reply.", 409);
        if (agents.Latest(requestId)?.State == "failed") Admit(request, message);
    }

    public async Task<AgentTaskResult> RunAsync(AgentTask task, Func<bool> canceled, CancellationToken token)
    {
        var current = agents.Find(task.Id) ?? task;
        var marker = current.DedupeKey?.LastIndexOf(":fresh-owner:", StringComparison.Ordinal) ?? -1;
        var messageId = marker < 0 ? "" : current.DedupeKey![(marker + ":fresh-owner:".Length)..];
        if (!_admitted.TryRemove(messageId, out var message) || task.RequestId != message.RequestId)
            return new("Historical work was not resumed; send a fresh message to chat.");
        if (canceled() || requests.FindRequest(message.RequestId) is not { } request || request.Requester != owner || RequestStore.AgentRequestClosed(request.Status))
            return new("Canceled; no model was invoked.");
        var instructions = "You are HireZero, a helpful conversational assistant. This session is text-only. You have no tools, repository access, coding runner, publication capability or task dispatcher. Answer the owner's message. You may explain or discuss a proposed task, but never claim to have inspected, changed, executed, pushed, sent or merged anything. Those operations are held. Do not invent current repository facts. Keep replies concise.";
        var answer = await model.CompleteAsync(IModelGateway.OwnerCell, instructions, message.Body, token, maxTokens: 500);
        var clean = ContentGuard.Clean(answer, 8000);
        if (clean.Flags.Length > 0) answer = "The response was withheld because it contained credential-like content.";
        else answer = clean.Text;
        if (!canceled() && requests.Messages(message.RequestId).LastOrDefault()?.Id == message.Id)
            requests.AddMessage(message.RequestId, "workspace-agent", answer, "fresh-owner-answer:" + message.Id);
        return new(canceled() ? "Canceled; the reply was discarded." : "Replied.");
    }
}
