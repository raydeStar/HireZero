namespace HireZero.Tools.Jev;

// A Jev (TypeSafe systemone) structured-choice request: the JSON body with model, state and questions.
public sealed record JevRequest(string Json);

public sealed record JevStatus(bool Configured);

public static class JevTools
{
    // Read-only, but the request text leaves this computer for the classifier the owner configured with a key.
    public static readonly ToolContract<JevRequest, ServiceReply> Classify = new(
        "jev.classify",
        ToolRisk.Read,
        "Send bounded request text to the owner's configured Jev classifier and read its structured choice."
    );

    public static readonly ToolContract<JevStatus, JevStatus> Status = new(
        "jev.status",
        ToolRisk.Read,
        "Whether a Jev key is configured on this computer."
    );
}
