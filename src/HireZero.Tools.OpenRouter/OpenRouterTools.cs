namespace HireZero.Tools.OpenRouter;

// One structured choice: bounded state (untrusted data), the question, and the allowed answers with their meaning.
public sealed record ChoiceRequest(string StateJson, string Instructions, IReadOnlyDictionary<string, string> Criteria);

// The chosen answer and its confidence, or why there is none. Tokens is the provider's reported usage, when given.
public sealed record ChoiceAnswer(string? Choice, double? Confidence, long? Tokens, string? Error);

public sealed record OpenRouterStatusRequest;

public static class OpenRouterTools
{
    public static readonly ToolContract<OpenRouterStatusRequest, IntegrationStatus> Status = new(
        "openrouter.status",
        ToolRisk.Read,
        "Whether an OpenRouter key and model are configured, and why not."
    );

    // Read-only, but the state leaves this computer for the model the owner chose, which OpenRouter bills per token.
    public static readonly ToolContract<ChoiceRequest, ChoiceAnswer> Choose = new(
        "openrouter.choose",
        ToolRisk.Read,
        "Send bounded text to the owner's chosen OpenRouter model for one structured choice, with a tiny reply budget."
    );
}
