namespace HireZero.Integrations.Sandbox;

public sealed record SandboxAvailability(
    bool Available,
    string Status,
    DateTimeOffset? QualifiedUntil = null,
    bool ExtendedScripts = false
);

public interface ISandboxRuntimeAvailability
{
    SandboxAvailability Availability { get; }
    Task RefreshAsync(CancellationToken token);
}

// Qualification is operator-controlled deployment evidence, not a tenant permission or a signed attestation.
public sealed class QualifiedDockerSandbox : ISandboxExecutor, ISandboxRuntimeAvailability, IExtendedSandboxRuntime
{
    private readonly IDockerCommands _commands;
    private readonly DockerSandbox _runtime;
    private readonly SandboxQualification _binding;
    private readonly Func<SandboxQualification?> _read;
    private readonly Func<string> _executableHash;
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _enabled;
    private readonly SemaphoreSlim _probe = new(1, 1);
    private SandboxAvailability _availability;
    public string Identity => _runtime.Identity;
    public bool ExtendedScriptsAvailable => Availability is { Available: true, ExtendedScripts: true };
    public SandboxAvailability Availability
    {
        get
        {
            var state = Volatile.Read(ref _availability);
            return state.Available && state.QualifiedUntil <= _clock()
                ? new(false, "qualification-expired", state.QualifiedUntil)
                : state;
        }
    }

    public QualifiedDockerSandbox(
        IDockerCommands commands,
        SandboxQualification binding,
        Func<SandboxQualification?> read,
        Func<string> executableHash,
        bool enabled,
        Func<DateTimeOffset>? clock = null
    )
    {
        if (!binding.BoundTo(binding.Image, binding.InstallationId, binding.Endpoint))
            throw new ArgumentException("A valid operator qualification binding is required.");
        _commands = commands;
        _binding = binding;
        _read = read;
        _executableHash = executableHash;
        _enabled = enabled;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _runtime = new(commands, binding.Image, binding.InstallationId, binding.Engine.Id);
        _availability = new(false, enabled ? "checking" : "operator-disabled");
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        await _probe.WaitAsync(token);
        try
        {
            var status = "operator-disabled";
            DateTimeOffset? until = null;
            var extended = false;
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
                    status = receipt.ValidUntil <= _clock() ? "qualification-expired" : "qualification-required";
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
                        {
                            status = await _runtime.AvailableAsync(token) ? "ready" : "image-unavailable";
                            extended = status == "ready" && receipt.ExtendedQualified;
                        }
                    }
                }
            }
            Volatile.Write(ref _availability, new(status == "ready", status, until, extended));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Volatile.Write(ref _availability, new(false, "qualification-required"));
        }
        finally
        {
            _probe.Release();
        }
    }

    public async Task<SandboxResult> ExecuteAsync(
        SandboxRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request.Validate();
        SandboxResult Unsent(string reason) =>
            new("not-sent", null, "", "", [], 0, new(true, "not-created", "unsent:" + request.JobId), reason);
        try
        {
            await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // No provisioning call has occurred. An interrupted readiness check must not strand an allowance.
            return Unsent("authority-changed");
        }
        if (!Availability.Available)
            return Unsent("runtime-not-qualified");
        if (request.Tier is not null && !ExtendedScriptsAvailable)
            return Unsent("extended-runtime-not-qualified");
        return await _runtime.ExecuteAsync(
            request,
            () => Availability.Available && (request.Tier is null || ExtendedScriptsAvailable) && stillAuthorized(),
            token
        );
    }

    // Neither expiry nor disabling new work can excuse abandoning a previously allocated resource.
    public Task<SandboxResult> RecoverAsync(SandboxRequest request, CancellationToken token) =>
        _runtime.RecoverAsync(request, token);

    public Task<SandboxRelease> ReleaseAsync(
        string companyId,
        string jobId,
        bool creationConfirmed,
        CancellationToken token
    ) => _runtime.ReleaseAsync(companyId, jobId, creationConfirmed, token);
}
