namespace HireZero.Core;

// A consent lease cancels an in-flight operation when the request is declined or its requester is revoked.
// It does not authorize a repair or publication; those retain their separate scoped approvals.
public sealed class RequestAgentConsent(CancellationTokenSource source, Action release) : IDisposable
{
    public CancellationToken Token => source.Token;
    public void Dispose() { release(); source.Dispose(); }
}
