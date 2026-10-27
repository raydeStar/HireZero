namespace HireZero.Integrations.Sandbox;

public sealed class QualifiedDockerBrowser : IBrowserExecutor, IBrowserRuntimeAvailability
{
    private readonly IDockerDuplexCommands _commands;
    private readonly DockerBrowser _runtime;
    private readonly BrowserQualification _binding;
    private readonly Func<BrowserQualification?> _read;
    private readonly Func<string> _executableHash;
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _enabled;
    private readonly SemaphoreSlim _probe = new(1, 1);
    private BrowserAvailability _availability;
    public string Identity => _runtime.Identity;
    public BrowserAvailability Availability
    {
        get
        {
            var state = Volatile.Read(ref _availability);
            return state.Available && state.QualifiedUntil <= _clock()
                ? new(false, "qualification-expired", state.QualifiedUntil)
                : state;
        }
    }

    public QualifiedDockerBrowser(
        IDockerDuplexCommands commands,
        BrowserQualification binding,
        Func<BrowserQualification?> read,
        Func<string> executableHash,
        bool enabled,
        Func<BrowserRequest, Func<bool>, IBrowserNetworkSession> network,
        Func<DateTimeOffset>? clock = null
    )
    {
        if (!binding.BoundTo(binding.Image, binding.InstallationId, binding.Endpoint))
            throw new ArgumentException("A complete browser qualification binding is required.");
        _commands = commands;
        _binding = binding;
        _read = read;
        _executableHash = executableHash;
        _enabled = enabled;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _runtime = new(commands, binding.Image, binding.InstallationId, binding.Engine.Id, network);
        _availability = new(false, enabled ? "checking" : "operator-disabled");
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        await _probe.WaitAsync(token);
        try
        {
            var status = "operator-disabled";
            DateTimeOffset? until = null;
            if (_enabled)
            {
                status = "qualification-required";
                var receipt = _read();
                if (
                    receipt is not null
                    && receipt.BoundTo(_binding.Image, _binding.InstallationId, _binding.Endpoint)
                    && receipt.Engine.Id == _binding.Engine.Id
                )
                {
                    until = receipt.ValidUntil;
                    status = until <= _clock() ? "qualification-expired" : "qualification-required";
                    if (
                        receipt.Matches(
                            _binding.Image,
                            _binding.InstallationId,
                            _binding.Endpoint,
                            _executableHash(),
                            _clock()
                        )
                    )
                    {
                        var profile = await DockerSandbox.InspectEngineAsync(_commands, token);
                        status = profile is null ? "engine-unavailable" : "qualification-required";
                        if (profile == receipt.Engine)
                            status = await _runtime.AvailableAsync(token) ? "ready" : "image-unavailable";
                    }
                }
            }
            Volatile.Write(ref _availability, new(status == "ready", status, until));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Volatile.Write(ref _availability, new(false, "qualification-required"));
        }
        finally
        {
            _probe.Release();
        }
    }

    public async Task<BrowserExecution> ExecuteAsync(
        BrowserRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request.Validate();
        BrowserExecution Unsent(string reason) =>
            new("not-sent", null, new(0, 0, 0), 0, new(true, "not-created", "unsent:" + request.JobId), reason);
        try
        {
            await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Unsent("authority-changed");
        }
        if (!Availability.Available)
            return Unsent("runtime-not-qualified");
        return await _runtime.ExecuteAsync(request, () => Availability.Available && stillAuthorized(), token);
    }

    // A closed front door is no excuse to abandon yesterday's resource in the garden.
    public Task<BrowserExecution> RecoverAsync(BrowserRequest request, CancellationToken token) =>
        _runtime.RecoverAsync(request, token);

    public Task<SandboxRelease> ReleaseAsync(
        BrowserRequest request,
        string resourceId,
        bool creationConfirmed,
        CancellationToken token
    ) => _runtime.ReleaseAsync(request, resourceId, creationConfirmed, token);
}
