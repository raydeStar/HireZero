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
        void Record(string outcome, string? detail = null) =>
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
            if (verifier is null || !verifier.Authorizes(invocation))
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
