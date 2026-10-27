using System.Collections.Concurrent;
using HireZero.Core;
using HireZero.Integrations.Sandbox;

// The fixture returns bounded fictional receipts; it never evaluates the submitted JavaScript.
internal sealed class SandboxFixtureExecutor : ISandboxExecutor, ISandboxRuntimeAvailability, IExtendedSandboxRuntime
{
    public string Identity { get; init; } = "fictional-browser-sandbox-v1";
    public int Executions;
    public int ReleaseFailures;
    public string Mode = "complete";
    public TimeSpan WaitTime = TimeSpan.FromSeconds(20);
    public volatile bool ExecutionEnabled = true;
    public bool ExtendedEnabled;
    public bool ExtendedScriptsAvailable => ExecutionEnabled && ExtendedEnabled;
    public SandboxAvailability Availability =>
        new(
            ExecutionEnabled,
            ExecutionEnabled ? "fixture" : "operator-disabled",
            ExtendedScripts: ExtendedScriptsAvailable
        );

    public Task RefreshAsync(CancellationToken token) => Task.CompletedTask;

    public SandboxRequest? LastRequest;
    public string? OutputText;
    private readonly ConcurrentDictionary<string, SandboxResult> _results = new();

    private static string Key(string company, string job) => company + ":" + job;

    public async Task<SandboxResult> ExecuteAsync(
        SandboxRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request.Validate();
        if (!ExecutionEnabled || request.Tier is not null && !ExtendedScriptsAvailable)
            return new(
                "not-sent",
                null,
                "",
                "",
                [],
                0,
                new(true, "not-created", "unsent:" + request.JobId),
                "fixture-disabled"
            );
        if (!stillAuthorized())
            throw new InvalidOperationException("The fictional butler requires authority.");
        Interlocked.Increment(ref Executions);
        LastRequest = request;
        var mode = Mode;
        if (mode == "wait")
        {
            try
            {
                await Task.Delay(WaitTime, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        var result =
            mode == "unknown"
                ? new SandboxResult(
                    "unknown",
                    null,
                    "",
                    "",
                    [],
                    null,
                    new(false, "awaiting-release", "fictional-" + request.JobId),
                    "fictional-uncertain-receipt",
                    true
                )
                : new SandboxResult(
                    "completed",
                    0,
                    "Fictional script receipt; no code was executed.",
                    "",
                    request
                        .Outputs.Select(name => new SandboxFile(
                            name,
                            OutputText ?? "Fictional report from " + request.Inputs.Length + " selected input files."
                        ))
                        .ToArray(),
                    12,
                    new(false, "awaiting-release", "fictional-" + request.JobId),
                    CreationConfirmed: true
                );
        _results[Key(request.CompanyId, request.JobId)] = result;
        return result;
    }

    public Task<SandboxResult> RecoverAsync(SandboxRequest request, CancellationToken token) =>
        Task.FromResult(
            _results.TryGetValue(Key(request.CompanyId, request.JobId), out var result)
                ? result with
                {
                    WallMilliseconds = null,
                }
                : new(
                    "unknown",
                    null,
                    "",
                    "",
                    [],
                    null,
                    new(false, "awaiting-reconciliation", "fictional-" + request.JobId),
                    "fictional-missing-receipt"
                )
        );

    public Task<SandboxRelease> ReleaseAsync(
        string companyId,
        string jobId,
        bool creationConfirmed,
        CancellationToken token
    )
    {
        var confirmed =
            (creationConfirmed || _results.ContainsKey(Key(companyId, jobId)))
            && Interlocked.Decrement(ref ReleaseFailures) < 0;
        return Task.FromResult(
            new SandboxRelease(confirmed, confirmed ? "released" : "release-unconfirmed", "fictional-" + jobId)
        );
    }
}
