using System.Collections.Concurrent;
using HireZero.Core;

// Compiled test executable only: no page requests, Chrome processes or containers originate from this executor.
internal sealed class BrowserFixtureExecutor : IBrowserExecutor, IBrowserRuntimeAvailability
{
    public string Identity { get; init; } = "fictional-public-browser-v1";
    public int Executions,
        ReleaseFailures;
    public string Mode = "complete";
    public volatile bool ExecutionEnabled = true;
    public BrowserRequest? LastRequest;
    public BrowserAvailability Availability =>
        new(ExecutionEnabled, ExecutionEnabled ? "fixture" : "operator-disabled");

    public Task RefreshAsync(CancellationToken token) => Task.CompletedTask;

    private readonly ConcurrentDictionary<string, BrowserExecution> _results = new();

    private static string Key(BrowserRequest request) => request.CompanyId + ":" + request.JobId;

    private static string Screenshot()
    {
        using var stream = typeof(BrowserFixtureExecutor).Assembly.GetManifestResourceStream("FictionalBrowserPage")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Convert.ToBase64String(bytes.ToArray());
    }

    public async Task<BrowserExecution> ExecuteAsync(
        BrowserRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request.Validate();
        if (!ExecutionEnabled)
            return new("not-sent", null, null, 0, new(true, "not-created", "unsent:" + request.JobId));
        if (!stillAuthorized())
            throw new InvalidOperationException("The fictional butler requires an approved itinerary.");
        Interlocked.Increment(ref Executions);
        LastRequest = request;
        var mode = Mode;
        if (mode == "wait")
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        var captures = new List<BrowserCapture>();
        var url = request.Steps[0].Value;
        for (var i = 0; i < request.Steps.Length; i++)
        {
            var step = request.Steps[i];
            if (step.Action == "navigate")
                url = step.Value;
            if (step.Action == "read")
                captures.Add(
                    new(
                        i + 1,
                        url,
                        DateTimeOffset.UtcNow,
                        "Fictional Paper Studio",
                        "Fictional website reviews and stationery.",
                        false,
                        [
                            new("Services", request.Origins[0] + "/services"),
                            new("Unsupported action", "javascript:alert('fictional')"),
                        ],
                        request.Screenshots ? new("image/jpeg", Screenshot()) : null
                    )
                );
        }
        var result =
            mode == "unknown"
                ? new BrowserExecution(
                    "unknown",
                    null,
                    null,
                    null,
                    new(false, "awaiting-reconciliation", "fictional-" + request.JobId),
                    "fictional-uncertain-receipt",
                    true
                )
                : new BrowserExecution(
                    "completed",
                    new(
                        request.JobId,
                        "completed",
                        request.Steps.Length,
                        mode == "partial",
                        captures.ToArray(),
                        [],
                        mode == "partial" ? ["Fictional third-party font was blocked."] : [],
                        true,
                        new(1, 50),
                        10
                    ),
                    new(2, 70, 0),
                    30,
                    new(false, "awaiting-release", "fictional-" + request.JobId),
                    CreationConfirmed: true,
                    BrowserExited: true
                );
        _results[Key(request)] = result;
        return result;
    }

    public Task<BrowserExecution> RecoverAsync(BrowserRequest request, CancellationToken token) =>
        Task.FromResult(
            _results.TryGetValue(Key(request), out var result)
                ? result with
                {
                    Network = null,
                    WallMilliseconds = null,
                }
                : new(
                    "unknown",
                    null,
                    null,
                    null,
                    new(false, "awaiting-reconciliation", "fictional-" + request.JobId),
                    "fictional-missing-receipt"
                )
        );

    public Task<SandboxRelease> ReleaseAsync(
        BrowserRequest request,
        string resourceId,
        bool creationConfirmed,
        CancellationToken token
    )
    {
        if (resourceId != "fictional-" + request.JobId)
            return Task.FromResult(new SandboxRelease(false, "scope-refused", resourceId));
        var confirmed =
            (creationConfirmed || _results.ContainsKey(Key(request))) && Interlocked.Decrement(ref ReleaseFailures) < 0;
        if (confirmed)
            _results.TryRemove(Key(request), out _);
        return Task.FromResult(
            new SandboxRelease(confirmed, confirmed ? "released" : "release-unconfirmed", resourceId)
        );
    }
}
