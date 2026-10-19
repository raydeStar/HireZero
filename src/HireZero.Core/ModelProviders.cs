namespace HireZero.Core;

// The model provider contract (docs/adr/0008-model-routes.md, docs/ADDING_A_MODEL_PROVIDER.md). A provider is an
// integration that turns one admitted call into one reply under the company's gates:
// - CountInput counts everything the provider will send, runtime framing included; unknown overhead is unknown, so
//   admission refuses rather than guessing.
// - CompleteAsync returns complete usage and a receipt, or throws ModelTransportException carrying whatever usage and
//   receipt are known. Tool requests, truncated or invalid output are withheld, never returned as a reply.
// - Cancellation must stop the provider's own call; the scheduler drains for a bounded time and holds the provider
//   when termination is unconfirmed. Calls are never retried here.
// IMeasuredModelProvider adds the one-job path: a pinned runtime identity and a job whose digests the provider
// checks again before dispatch.

public sealed record ModelCall(
    string DispatchId,
    CompanyContext Context,
    string Instructions,
    int MaxInputTokens,
    int MaxOutputTokens,
    string? ModelRoute = null
);

public sealed record ModelReply(string Body, long? TotalTokens, string? Receipt = null, ModelUsage? Usage = null);

public sealed class ModelTransportException(
    string message,
    long? knownUsage = null,
    string? receipt = null,
    ModelUsage? usage = null,
    bool accountingViolation = false
) : Exception(message)
{
    public long? KnownUsage { get; } = knownUsage;
    public string? Receipt { get; } = receipt;
    public ModelUsage? Usage { get; } = usage;
    public bool AccountingViolation { get; } = accountingViolation;
}

public interface IModelProvider
{
    bool Qualified { get; }
    string Status { get; }

    // Counts all provider input, including runtime-added framing/instructions. No observed offset.
    ModelInputAccounting CountInput(ModelCall call);
    int CountTokens(ModelCall call) => CountInput(call).TotalTokens();
    Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token);
}

public interface IMeasuredModelProvider
{
    string RuntimeIdentity { get; }
    Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token);
}

public sealed class UnavailableModelProvider : IModelProvider
{
    public bool Qualified => false;
    public string Status =>
        "Live context isolation and usage accounting have not been qualified. No model request is sent.";

    public int CountTokens(ModelCall call) => throw new ModelUnavailableException(Status);

    public ModelInputAccounting CountInput(ModelCall call) => throw new ModelUnavailableException(Status);

    public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
        throw new ModelUnavailableException(Status);
}

// Application instructions/context and runtime-added framing/instructions share the same
// provider input allowance. Unknown overhead is not zero, and observations are not bounds.
public sealed record ModelInputAccounting(int ApplicationTokens, int? RuntimeOverheadTokens, string CounterIdentity)
{
    public int TotalTokens()
    {
        if (RuntimeOverheadTokens is null)
            throw new ModelUnavailableException(
                "Runtime input overhead has no compatible exact counter; admission is unavailable."
            );
        if (
            ApplicationTokens < 0
            || RuntimeOverheadTokens < 0
            || string.IsNullOrWhiteSpace(CounterIdentity)
            || CounterIdentity.Length > 160
            || ApplicationTokens > int.MaxValue - RuntimeOverheadTokens.Value
        )
            throw new WorkspaceException("Invalid complete input accounting.");
        return ApplicationTokens + RuntimeOverheadTokens.Value;
    }
}

public sealed record CompanyInputAdmission(
    ModelInputAccounting Accounting,
    string ContextDigest,
    string InstructionsDigest
);

// Output includes any reasoning tokens. A missing reasoning sub-breakdown is not recorded as zero.

public sealed record ModelUsage(long InputTokens, long OutputTokens, long? ReasoningTokens = null)
{
    public bool Consistent(long? total) =>
        InputTokens >= 0
        && OutputTokens >= 0
        && (ReasoningTokens is null || ReasoningTokens >= 0 && ReasoningTokens <= OutputTokens)
        && InputTokens <= long.MaxValue - OutputTokens
        && total == InputTokens + OutputTokens;
}
