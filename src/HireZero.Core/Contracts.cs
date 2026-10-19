namespace HireZero.Core;

// A message the owner can read and act on. Role code throws it (or a subclass) for expected refusals.
public class WorkspaceException(string message, int status = 409) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class ModelUnavailableException(string message) : Exception(message);

public sealed record WorkspaceOwner(string Login)
{
    public const string LocalIdentity = "local-owner@workspace.invalid";
}
