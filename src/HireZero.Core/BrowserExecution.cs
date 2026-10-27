namespace HireZero.Core;

// Recovered worker counters are diagnostics, not host-measured usage. The butler cannot invoice an assumption.
public sealed record BrowserExecution(
    string State,
    BrowserResult? Result,
    BrowserNetworkUsage? Network,
    long? WallMilliseconds,
    SandboxRelease Release,
    string? Reason = null,
    bool CreationConfirmed = false,
    bool BrowserExited = false
);

public interface IBrowserExecutor
{
    string Identity { get; }
    Task<BrowserExecution> ExecuteAsync(BrowserRequest request, Func<bool> stillAuthorized, CancellationToken token);
    Task<BrowserExecution> RecoverAsync(BrowserRequest request, CancellationToken token);
    Task<SandboxRelease> ReleaseAsync(
        BrowserRequest request,
        string resourceId,
        bool creationConfirmed,
        CancellationToken token
    );
}

public sealed record BrowserAvailability(bool Available, string Status, DateTimeOffset? QualifiedUntil = null);

public interface IBrowserRuntimeAvailability
{
    BrowserAvailability Availability { get; }
    Task RefreshAsync(CancellationToken token);
}
