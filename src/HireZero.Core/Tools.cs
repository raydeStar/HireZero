using System.Collections.Concurrent;
using System.Text.Json;

namespace HireZero.Core;

// Tools are how employees reach the outside world (docs/adr/0007-tool-registry.md). Contracts live in
// src/HireZero.Tools.* assemblies; integrations register handlers; roles call them through an IToolbox that checks
// the module's manifest, the risk gate and records every call.
public interface IToolContract
{
    string Id { get; }
    ToolRisk Risk { get; }
    string Description { get; }
    Type InputType { get; }
    Type OutputType { get; }
}

public sealed record ToolContract<TInput, TOutput>(string Id, ToolRisk Risk, string Description) : IToolContract
{
    public Type InputType => typeof(TInput);
    public Type OutputType => typeof(TOutput);
}

// Who is calling: the employee module, and when known the employee and task the call serves.
public sealed record ToolCaller(string ModuleId, string? EmployeeId = null, string? TaskId = null);

// Evidence that a Write, External or Spend call is allowed, such as an execution grant id or an owner approval id.
public sealed record ToolAuthority(string Kind, string Id);

public sealed record ToolInvocation(
    IToolContract Contract,
    ToolCaller Caller,
    string InputJson,
    string InputDigest,
    ToolAuthority? Authority
);

// Decides whether an authority covers one exact invocation. External and Spend approvals must be consumed so they
// cannot be reused.
public interface IToolAuthorityVerifier
{
    string Kind { get; }
    bool Authorizes(ToolInvocation invocation);
}

public sealed record ToolCallRecord(
    DateTimeOffset At,
    string ToolId,
    string Risk,
    string ModuleId,
    string? EmployeeId,
    string? TaskId,
    string InputDigest,
    string? Authority,
    string Outcome,
    string? Detail
);

public interface IToolAudit
{
    void Record(ToolCallRecord record);
}

// Keeps the most recent calls in memory, for profiles without a persistent audit store and for tests.
public sealed class MemoryToolAudit(int capacity = 500) : IToolAudit
{
    private readonly ConcurrentQueue<ToolCallRecord> _records = new();

    public ToolCallRecord[] Records => [.. _records];

    public void Record(ToolCallRecord record)
    {
        _records.Enqueue(record);
        while (_records.Count > capacity && _records.TryDequeue(out _)) { }
    }
}

public sealed class ToolRefusedException(string message) : WorkspaceException(message, 403);

// A tool ran but its external effect failed or is unknown. Kind is stable for callers to map ("signed-out",
// "clone-failed", "too-large", "failed", "unavailable", "refused"); Detail is a bounded, unsanitized excerpt of the
// tool's own error output, never shown to the owner unfiltered.
public sealed class ToolFailureException(string kind, string message, int status = 503, string? detail = null)
    : WorkspaceException(message, status)
{
    public string Kind { get; } = kind;
    public string? Detail { get; } = detail;
}

public interface IToolbox
{
    Task<TOutput> InvokeAsync<TInput, TOutput>(
        ToolContract<TInput, TOutput> contract,
        TInput input,
        CancellationToken token = default,
        ToolAuthority? authority = null
    );
}

public sealed class ToolRegistry(IToolAudit audit, IEnumerable<IToolAuthorityVerifier>? verifiers = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, (IToolContract Contract, Delegate Handler)> _tools = new(
        StringComparer.Ordinal
    );
    private readonly IToolAuthorityVerifier[] _verifiers = [.. verifiers ?? []];

    public IToolContract[] Contracts =>
        [.. _tools.Values.Select(t => t.Contract).OrderBy(c => c.Id, StringComparer.Ordinal)];

    public void Register<TInput, TOutput>(
        ToolContract<TInput, TOutput> contract,
        Func<TInput, CancellationToken, Task<TOutput>> handler
    )
    {
        if (!_tools.TryAdd(contract.Id, (contract, handler)))
            throw new InvalidOperationException("The tool '" + contract.Id + "' is already registered.");
    }

    public bool Has(string toolId) => _tools.ContainsKey(toolId);

    // The digest an approval for one exact call is bound to: the tool id and its serialized input.
    public static string Digest<TInput, TOutput>(ToolContract<TInput, TOutput> contract, TInput input) =>
        Digest(contract.Id, JsonSerializer.Serialize(input, Json));

    private static string Digest(string toolId, string inputJson) => ContentGuard.Digest(toolId + "\n" + inputJson);

    // A toolbox limited to the tools the module's manifest declares.
    public IToolbox For(EmployeeManifest manifest, string? employeeId = null, string? taskId = null) =>
        new Toolbox(this, manifest, new(manifest.Id, employeeId, taskId));

    private async Task<TOutput> InvokeAsync<TInput, TOutput>(
        EmployeeManifest manifest,
        ToolCaller caller,
        ToolContract<TInput, TOutput> contract,
        TInput input,
        ToolAuthority? authority,
        CancellationToken token
    )
    {
        var inputJson = JsonSerializer.Serialize(input, Json);
        var invocation = new ToolInvocation(contract, caller, inputJson, Digest(contract.Id, inputJson), authority);
        using var span = CompanyTelemetry.Source.StartActivity("tool " + contract.Id);
        span?.SetTag("hirezero.tool", contract.Id);
        span?.SetTag("hirezero.tool.risk", contract.Risk.ToString().ToLowerInvariant());
        span?.SetTag("hirezero.module", caller.ModuleId);
        span?.SetTag("hirezero.employee", caller.EmployeeId);
        void Record(string outcome, string? detail = null)
        {
            span?.SetTag("hirezero.outcome", outcome);
            if (outcome != "completed")
                span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, outcome);
            audit.Record(
                new(
                    DateTimeOffset.UtcNow,
                    contract.Id,
                    contract.Risk.ToString().ToLowerInvariant(),
                    caller.ModuleId,
                    caller.EmployeeId,
                    caller.TaskId,
                    invocation.InputDigest,
                    authority is null ? null : authority.Kind + ":" + authority.Id,
                    outcome,
                    detail
                )
            );
        }
        ToolRefusedException Refuse(string reason)
        {
            Record("refused", reason);
            return new ToolRefusedException(reason);
        }

        var declared = manifest.Tools.FirstOrDefault(t => t.Id == contract.Id);
        if (declared is null)
            throw Refuse("The " + manifest.Name + " employee has not declared the tool '" + contract.Id + "'.");
        if (declared.Risk != contract.Risk)
            throw Refuse("The declared risk of '" + contract.Id + "' does not match its contract.");
        if (
            !_tools.TryGetValue(contract.Id, out var registered)
            || registered.Handler is not Func<TInput, CancellationToken, Task<TOutput>> handler
        )
            throw Refuse("The tool '" + contract.Id + "' is not available in this workspace.");
        if (contract.Risk != ToolRisk.Read)
        {
            if (authority is null)
                throw Refuse("'" + contract.Id + "' needs owner authority for this exact call.");
            var verifier = _verifiers.FirstOrDefault(v => v.Kind == authority.Kind);
            bool authorized;
            try
            {
                authorized = verifier is not null && verifier.Authorizes(invocation);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A verifier that cannot decide refuses.
                authorized = false;
            }
            if (!authorized)
                throw Refuse("The supplied authority does not cover this exact '" + contract.Id + "' call.");
        }
        try
        {
            var output = await handler(input, token);
            Record("completed");
            return output;
        }
        catch (Exception error)
        {
            // The outcome of a failed call may be uncertain; it is recorded and never retried here.
            Record("failed", error.GetType().Name);
            throw;
        }
    }

    private sealed class Toolbox(ToolRegistry registry, EmployeeManifest manifest, ToolCaller caller) : IToolbox
    {
        public Task<TOutput> InvokeAsync<TInput, TOutput>(
            ToolContract<TInput, TOutput> contract,
            TInput input,
            CancellationToken token = default,
            ToolAuthority? authority = null
        ) => registry.InvokeAsync(manifest, caller, contract, input, authority, token);
    }
}

// Approvals for actions the owner starts directly, such as signing in to a service or installing a pinned package:
// the owner-only endpoint approves the exact call it is about to make, and the tool consumes that approval once.
public sealed class OwnerActionApprovals : IToolAuthorityVerifier
{
    public const string ActionKind = "owner-action";
    private readonly ConcurrentDictionary<string, string> _pending = new(StringComparer.Ordinal);

    public string Kind => ActionKind;

    public ToolAuthority Approve<TInput, TOutput>(ToolContract<TInput, TOutput> contract, TInput input)
    {
        var id = Guid.NewGuid().ToString("N");
        _pending[id] = ToolRegistry.Digest(contract, input);
        return new(ActionKind, id);
    }

    public bool Authorizes(ToolInvocation invocation) =>
        invocation.Authority is { } authority
        && _pending.TryRemove(authority.Id, out var digest)
        && digest == invocation.InputDigest;
}

// One HTTP exchange with a service an integration owns (base address, credentials and transport stay in the
// integration). Integrations classify the call by method and path and refuse anything outside their tool.
public sealed record ServiceCall(string Method, string Path, string? JsonBody = null);

public sealed record ServiceReply(int Status, string Body)
{
    public bool Success => Status is >= 200 and < 300;
}

// An integration's contribution to the registry. Hosts and tests compose the same providers with AddToolRegistry.
public interface IToolProvider
{
    void Register(ToolRegistry registry);

    static IToolProvider From(Action<ToolRegistry> register) => new DelegateProvider(register);

    private sealed class DelegateProvider(Action<ToolRegistry> register) : IToolProvider
    {
        public void Register(ToolRegistry registry) => register(registry);
    }
}

// Owner requests in flight. Middleware opens a scope for each request an owner makes; deterministic code that the
// request runs may write under it, but a task that outlives the request loses it.
public sealed class OwnerRequests : IToolAuthorityVerifier
{
    public const string RequestKind = "owner-request";
    private static readonly AsyncLocal<ToolAuthority?> Ambient = new();
    private readonly ConcurrentDictionary<string, bool> _active = new(StringComparer.Ordinal);

    public string Kind => RequestKind;

    public static ToolAuthority? Current => Ambient.Value;

    public IDisposable Begin(string requestId)
    {
        var id = requestId + ":" + Guid.NewGuid().ToString("N");
        var previous = Ambient.Value;
        _active[id] = true;
        Ambient.Value = new(RequestKind, id);
        return new Scope(() =>
        {
            _active.TryRemove(id, out _);
            Ambient.Value = previous;
        });
    }

    public bool Authorizes(ToolInvocation invocation) =>
        invocation.Authority is { } authority && Current?.Id == authority.Id && _active.ContainsKey(authority.Id);

    private sealed class Scope(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

public static class ToolComposition
{
    // The registry built from every registered IToolProvider, IToolAuthorityVerifier and the IToolAudit (memory by
    // default). Owner requests and owner actions are always available as authorities.
    public static IServiceCollection AddToolRegistry(this IServiceCollection services)
    {
        services.AddSingleton<OwnerRequests>();
        services.AddSingleton<IToolAuthorityVerifier>(provider => provider.GetRequiredService<OwnerRequests>());
        services.AddSingleton<OwnerActionApprovals>();
        services.AddSingleton<IToolAuthorityVerifier>(provider => provider.GetRequiredService<OwnerActionApprovals>());
        services.AddSingleton<MemoryToolAudit>();
        services.AddSingleton(provider =>
        {
            var registry = new ToolRegistry(
                provider.GetService<IToolAudit>() ?? provider.GetRequiredService<MemoryToolAudit>(),
                provider.GetServices<IToolAuthorityVerifier>()
            );
            foreach (var contributor in provider.GetServices<IToolProvider>())
                contributor.Register(registry);
            return registry;
        });
        return services;
    }

    // Opens an owner-request authority scope around every request whose caller is the owner.
    public static void UseOwnerRequestAuthority(this WebApplication app)
    {
        var requests = app.Services.GetRequiredService<OwnerRequests>();
        app.Use(
            async (context, next) =>
            {
                if ((string?)context.Items["role"] != "owner")
                {
                    await next();
                    return;
                }
                using (requests.Begin(context.TraceIdentifier))
                    await next();
            }
        );
    }
}
