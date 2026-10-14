namespace HireZero.Core;

// A message the owner can read and act on. Role code throws it (or a subclass) for expected refusals.
public class WorkspaceException(string message, int status = 409) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class ModelUnavailableException(string message) : Exception(message);

// The one way core and roles reach a language model. "cell" isolates conversations per login;
// OwnerCell is the owner's private cell. Implemented by an integration (OpenClaw today).
public interface IModelGateway
{
    const string OwnerCell = "owner-private";
    string DefaultModel { get; }
    string Reasoning { get; }
    void Prepare(string cell);
    void Stop(string cell);
    Task<string> CompleteAsync(string cell, string instructions, string prompt, CancellationToken token, string? model = null, int maxTokens = 900);
}

public sealed record WorkspaceOwner(string Login)
{
    public const string LocalIdentity = "local-owner@workspace.invalid";
    public bool Owns(WorkRequest request) => request.Requester == Login || request.Requester == LocalIdentity;
}

// A role may answer a conversation before the generic reply (the developer role answers GitHub requests).
// Return null to pass. Responders run in registration order; only owner conversations reach owner-only responders.
public interface IConversationResponder
{
    // context: what HireZero knows about the projects this conversation mentions, already cleaned and fenced
    // for the asker's audience (full for the owner, an outline for visitors), or null when none matched.
    Task<string?> RespondAsync(WorkRequest request, IReadOnlyList<ConversationMessage> messages, bool ownerConversation, string? context, CancellationToken token);
}
