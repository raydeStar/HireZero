using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Codex;

public sealed record CompanyCodexOptions(
    string Executable,
    string Identity,
    string ScratchRoot,
    string? Home = null,
    string? SyntheticMode = null,
    decimal QualificationCreditBudget = 0
);

public sealed record CompanyCodexBilling(
    bool? IncludedUsageAllowed,
    bool? HasCredits,
    string? CreditBalance,
    bool? SpendControlReached,
    string? RateLimitReachedType,
    int? PrimaryUsedPercent,
    DateTimeOffset ObservedAt
);

public sealed record CompanyCodexInspection(
    string? AccountType,
    string? Plan,
    bool IncludedUsageAllowed,
    string[] Models,
    CompanyCodexBilling? Billing = null
);

public sealed record CompanyCodexReceipt(
    string EmployeeId,
    string ContextId,
    string Model,
    string ThreadId,
    string TurnId,
    string Body,
    long InputTokens,
    long OutputTokens,
    long? ReasoningTokens,
    long TotalTokens,
    CompanyCodexBilling? Before = null,
    CompanyCodexBilling? After = null,
    string? StopReason = null,
    int? ContextUtf8Bytes = null,
    int? InstructionUtf8Bytes = null
);

// Native app-server sign-in reuse only. No credential files, bearer tokens, account changes or daemon control.
// The current strict scheduler profile requires hard token admission, an application policy
// this native protocol cannot supply. Explicit one-job measured admission is separate from
// strict qualification; neither entry point is an automatic worker fallback.
public sealed class CompanyCodexConnection(
    CompanyCodexOptions options,
    CompanyModelRouting? routing = null,
    ModelVendorPolicy? vendorPolicy = null
) : IModelProvider, IMeasuredModelProvider
{
    private const string PermissionProfile = "hirezero_qualification";
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CompanyModelRouting _routing = Servable(
        routing ?? new(),
        vendorPolicy ?? ModelVendorPolicy.FromEnvironment()
    );

    // This connection serves OpenAI routes on the Codex runtime only.
    private static CompanyModelRouting Servable(CompanyModelRouting routing, ModelVendorPolicy policy)
    {
        policy.Require("codex");
        routing.Validate();
        if (
            routing.Runtime != "codex"
            || new[] { routing.TextModel, routing.CodingModel }.Any(r => ModelRoutes.Provider(r) != "openai")
            || routing.AnalysisModel is { } analysis && ModelRoutes.Provider(analysis) != "openai"
        )
            throw new WorkspaceException("Select explicit OpenAI model routes on the Codex runtime.");
        return routing;
    }

    private int _sent;
    private bool _stopped;
    private readonly HashSet<string> _measuredAttempts = new(StringComparer.Ordinal);
    public string RuntimeIdentity { get; } =
        CompanyJson.Digest(
            new
            {
                native = options.Identity,
                connector = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        File.ReadAllBytes(typeof(CompanyCodexConnection).Assembly.Location)
                    )
                ),
            }
        );

    // Historical qualification planning reserve, NOT a price estimate or proven maximum charge.
    // The former 126.75 calculation assumed full context + output and 30 billable attempts;
    // those assumptions do not establish actual native retry billing. See OAUTH_QUEUE_DECISION.md.
    public const decimal QualificationPlanningCreditsPerTurn = 150;
    public const int QualificationContextByteLimit = 4000,
        QualificationInstructionCharacterLimit = 1000;
    public bool Qualified => false;
    public string Status =>
        "Codex selected. Automatic model work is off. An eligible task may run only after explicit one-job measured authorization; token/time/output targets are local soft stops.";

    public int CountTokens(ModelCall call) =>
        throw new ModelUnavailableException(
            "Codex app-server has no compatible exact input counter; character counts are not token bounds."
        );

    public ModelInputAccounting CountInput(ModelCall call) =>
        throw new ModelUnavailableException(
            "Codex application tokens and runtime-added input have no compatible exact preflight counter. Observed usage is not an admission bound."
        );

    public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
        throw new ModelUnavailableException(Status);

    public async Task<CompanyCodexInspection> InspectAsync(CancellationToken token = default)
    {
        await _serial.WaitAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var rpc = Start();
            return await Inspect(rpc, deadline.Token);
        }
        finally
        {
            _serial.Release();
        }
    }

    private async Task<CompanyCodexInspection> Inspect(CodexAppServer rpc, CancellationToken token)
    {
        _routing.Validate();
        await rpc.Initialize(token);
        var account = await rpc.Request("account/read", new { refreshToken = false }, token);
        string? type = null,
            plan = null;
        if (account.TryGetProperty("account", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            type = Text(a, "type");
            plan = Text(a, "planType");
        }
        var list = await rpc.Request("model/list", new { limit = 100, includeHidden = true }, token);
        if (list.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null)
            throw new WorkspaceException(
                "Model catalog requires another page; qualification refuses incomplete discovery."
            );
        var models = list.GetProperty("data")
            .EnumerateArray()
            .Select(m => Text(m, "model") ?? Text(m, "id") ?? "")
            .ToArray();
        var allowed = false;
        CompanyCodexBilling? billing = null;
        if (type == "chatgpt")
        {
            var limits = await rpc.Request(
                "account/rateLimits/read",
                new { supportsLunaReserve = false, excludeResetCreditDetails = true },
                token
            );
            allowed =
                limits.TryGetProperty("ordinaryUsageAllowed", out var ordinary)
                && ordinary.ValueKind == JsonValueKind.True;
            billing = Billing(limits);
        }
        return new(type, plan, allowed, models, billing);
    }

    // Caller supplies fictional data only. This is an explicit qualification entry point, never a worker fallback.
    // Four attempts maximum per connection; any uncertain outcome permanently closes this qualification instance.
    // Byte/character/deadline checks below are conservative local controls, NOT upstream token/spending ceilings.
    public Task<CompanyCodexReceipt> CompletePilotAsync(ModelCall call, CancellationToken token = default) =>
        CompleteNativeAsync(call, null, token);

    public async Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
    {
        if (
            job.State != "dispatched"
            || job.RuntimeIdentity != RuntimeIdentity
            || !CompanyMeasuredPolicy.Known(job.Policy)
            || job.CreditAllowance is <= 0 or > 1000
            || job.Route != call.ModelRoute
            || job.ContextDigest != CompanyJson.Digest(call.Context)
            || job.InstructionsDigest != ContentGuard.Digest(call.Instructions)
        )
            throw new WorkspaceException("The exact consumed measured authorization is required.");
        var receipt = await CompleteNativeAsync(call, job, token);
        var usage = new ModelUsage(receipt.InputTokens, receipt.OutputTokens, receipt.ReasoningTokens);
        if (receipt.StopReason is not null)
            throw new ModelTransportException(
                receipt.StopReason,
                receipt.TotalTokens,
                "codex:" + receipt.TurnId,
                usage,
                accountingViolation: true
            );
        return new(receipt.Body, receipt.TotalTokens, "codex:" + receipt.TurnId, usage);
    }

    private async Task<CompanyCodexReceipt> CompleteNativeAsync(
        ModelCall call,
        CompanyMeasuredJob? measured,
        CancellationToken token
    )
    {
        var input = CompanyJson.Encode(call.Context);
        if (
            call.ModelRoute is not null
            && call.ModelRoute != _routing.TextModel
            && call.ModelRoute != _routing.AnalysisModel
        )
            throw new WorkspaceException("Text dispatch cannot select the coding route.");
        var bytes = Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(call.Instructions);
        // Owner-only reports have no employee. Only their explicit measured policies get that place setting.
        var ownerReport =
            (
                (
                    CompanyMeasuredPolicy.Matches(measured?.Policy, CompanyMeasuredPolicy.BusinessProfile)
                    && call.Context.Scope == "business-profile"
                )
                || (
                    CompanyMeasuredPolicy.Matches(measured?.Policy, CompanyMeasuredPolicy.MemoryAnalysis)
                    && call.Context.Scope == "memory-analysis"
                )
            )
            && call.Context.EmployeeId == ""
            && call.Context.RoleId == ""
            && call.Context.ThreadId == "";
        if (
            measured is null
                && (
                    call.Instructions.Length > QualificationInstructionCharacterLimit
                    || Encoding.UTF8.GetByteCount(input) > QualificationContextByteLimit
                    || call.Context.Input.Length > 1000
                )
            || measured is not null && (bytes != measured.ApplicationBytes || bytes > measured.Policy.ApplicationBytes)
            || string.IsNullOrWhiteSpace(call.Context.EmployeeId) && !ownerReport
            || (
                CompanyMeasuredPolicy.Matches(measured?.Policy, CompanyMeasuredPolicy.BusinessProfile)
                || CompanyMeasuredPolicy.Matches(measured?.Policy, CompanyMeasuredPolicy.MemoryAnalysis)
            ) && !ownerReport
            || call.Context.Id.Length == 0
        )
            throw new WorkspaceException("Narrow the fictional qualification context.");
        await _serial.WaitAsync(token);
        string? thread = null,
            turn = null;
        var started = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(measured?.Policy.LocalDeadlineSeconds ?? 60));
        CodexAppServer? rpc = null;
        CompanyCodexReceipt? completedReceipt = null,
            observedReceipt = null;
        CompanyCodexReceipt ReadUsage(JsonElement parameters)
        {
            var usage = parameters.GetProperty("tokenUsage").GetProperty("total");
            var i = Number(usage, "inputTokens");
            var o = Number(usage, "outputTokens");
            long? r =
                usage.TryGetProperty("reasoningOutputTokens", out var reasoning)
                && reasoning.ValueKind != JsonValueKind.Null
                    ? Number(usage, "reasoningOutputTokens")
                    : null;
            var total = Number(usage, "totalTokens");
            if (!new ModelUsage(i, o, r).Consistent(total))
                throw new ModelTransportException(
                    "Complete runtime usage is inconsistent.",
                    total,
                    "codex:" + turn,
                    new(i, o, r),
                    accountingViolation: true
                );
            return new(
                call.Context.EmployeeId,
                call.Context.Id,
                CompanyModelRouting.Native(call.ModelRoute ?? _routing.TextModel),
                thread!,
                turn!,
                "",
                i,
                o,
                r,
                total
            );
        }
        try
        {
            if (_stopped || measured is null && _sent >= 4)
                throw new WorkspaceException("Native work stopped or its qualification allowance is exhausted.");
            if (measured is not null && !_measuredAttempts.Add(call.DispatchId))
                throw new WorkspaceException("This measured attempt already consumed its one native request.");
            if (
                measured is null
                && (
                    options.QualificationCreditBudget is <= 0 or > 1000
                    || (_sent + 1) * QualificationPlanningCreditsPerTurn > options.QualificationCreditBudget
                )
            )
                throw new WorkspaceException(
                    "The explicit qualification allowance cannot cover another historical planning buffer; this is not a proven charge bound."
                );
            rpc = Start(measuredRequest: measured is not null);
            var inspection = await Inspect(rpc, deadline.Token);
            var model = CompanyModelRouting.Native(call.ModelRoute ?? _routing.TextModel);
            if (
                inspection.AccountType != "chatgpt"
                || !inspection.IncludedUsageAllowed
                || inspection.Billing is { SpendControlReached: true }
                || inspection.Billing?.RateLimitReachedType is not null
            )
                throw new WorkspaceException(
                    "Existing ChatGPT sign-in with included usage permission is required. No API-key or provider fallback."
                );
            if (
                !inspection.Models.Contains(model)
                || !inspection.Models.Contains(CompanyModelRouting.Native(_routing.CodingModel))
            )
                throw new WorkspaceException(
                    "A selected model is absent from this account's catalog; no substitution."
                );
            await rpc.VerifyPermissions(PermissionProfile, ReadOnly(rpc.WorkingDirectory), deadline.Token);
            // The reviewed policy sets the effort; qualification turns stay low. The pinned runtime applies the
            // thread's effort and ignores the turn's own, so it is set on both and confirmed before any turn.
            var effort = measured?.Policy.Effort ?? "low";
            var response = await rpc.Request(
                "thread/start",
                new
                {
                    model,
                    modelProvider = "openai",
                    cwd = rpc.WorkingDirectory,
                    approvalPolicy = "never",
                    permissions = PermissionProfile,
                    ephemeral = true,
                    allowProviderModelFallback = false,
                    environments = Array.Empty<object>(),
                    runtimeWorkspaceRoots = Array.Empty<string>(),
                    serviceName = "hirezero_qualification",
                    // Measured instructions are completely included in the reviewed application-byte packet.
                    baseInstructions = measured is null
                        ? "You are a text-only employee assistant. Use only the supplied context. Never call tools, access files, retrieve other history or perform actions. Reply in at most 80 words."
                        : "",
                    developerInstructions = call.Instructions,
                    config = new { model_reasoning_effort = effort },
                },
                deadline.Token
            );
            if (
                Text(response, "model") != model
                || Text(response, "modelProvider") != "openai"
                || !response.TryGetProperty("instructionSources", out var sources)
                || sources.GetArrayLength() != 0
                || response.GetProperty("thread").GetProperty("ephemeral").ValueKind != JsonValueKind.True
                || !response.TryGetProperty("activePermissionProfile", out var active)
                || Text(active, "id") != PermissionProfile
                || Text(active, "extends") is not null
                || Text(response, "approvalPolicy") != "never"
                || Text(response.GetProperty("sandbox"), "type") != "readOnly"
                || response.GetProperty("sandbox").GetProperty("networkAccess").ValueKind != JsonValueKind.False
            )
                throw new WorkspaceException(
                    "Codex thread isolation is unconfirmed: exact route, ephemeral state, read-only sandbox and zero ambient instruction sources are required. No model turn was sent."
                );
            if (Text(response, "reasoningEffort") != effort)
                throw new WorkspaceException(
                    "The runtime did not confirm the reviewed reasoning effort. No model turn was sent."
                );
            thread =
                response.GetProperty("thread").GetProperty("id").GetString()
                ?? throw new WorkspaceException("Missing thread identity.");
            if (measured is not null && DateTimeOffset.UtcNow >= measured.ExpiresAt)
                throw new WorkspaceException("The measured authorization expired before native dispatch.");
            if (measured is null)
                _sent++;
            started = true;
            var result = await rpc.Request(
                "turn/start",
                new
                {
                    threadId = thread,
                    model,
                    effort,
                    summary = "none",
                    approvalPolicy = "never",
                    environments = Array.Empty<object>(),
                    runtimeWorkspaceRoots = Array.Empty<string>(),
                    serviceTierForTurn = "default",
                    permissions = PermissionProfile,
                    input = new[] { new { type = "text", text = input } },
                },
                deadline.Token
            );
            turn =
                result.GetProperty("turn").GetProperty("id").GetString()
                ?? throw new WorkspaceException("Missing turn identity.");
            var output = new CodexTextOutput(measured?.Policy.VisibleCharacterStop ?? 2000);
            CompanyCodexReceipt? receipt = null;
            while (true)
            {
                var e = await rpc.Read(deadline.Token);
                var method = Text(e, "method");
                if (e.TryGetProperty("id", out var requestId))
                {
                    await rpc.Deny(requestId, deadline.Token);
                    throw new ModelTransportException(
                        "Runtime requested a tool, approval or authentication change; qualification stopped."
                    );
                }
                if (method == "error")
                    throw new ModelTransportException("Codex reported an error; no replay.");
                if (!e.TryGetProperty("params", out var p))
                    continue;
                if (
                    method == "account/rateLimits/updated"
                    && (
                        Flag(p, "ordinaryUsageAllowed") == false
                        || Billing(p) is { SpendControlReached: true }
                        || Billing(p).RateLimitReachedType is not null
                    )
                )
                    throw new ModelTransportException(
                        "Runtime reported quota or spending exhaustion; no continuation or replay."
                    );
                if (method == "account/updated" && Text(p, "authMode") is { } mode && mode != "chatgpt")
                    throw new ModelTransportException("Runtime account mode changed; no provider fallback.");
                if (Text(p, "threadId") != thread || Text(p, "turnId") is { } eventTurn && eventTurn != turn)
                    continue;
                if (method is "item/started" or "item/completed")
                {
                    var item = p.GetProperty("item");
                    var kind = Text(item, "type");
                    if (kind is not ("agentMessage" or "reasoning" or "userMessage"))
                        throw new ModelTransportException(
                            "A non-text runtime item was withheld; qualification stopped."
                        );
                    if (kind == "agentMessage")
                        output.ObserveItem(item, completed: method == "item/completed");
                }
                if (method == "item/agentMessage/delta")
                    output.ObserveDelta(p);
                if (method == "thread/tokenUsage/updated")
                {
                    receipt = observedReceipt = ReadUsage(p);
                    if (
                        measured is not null
                        && (
                            receipt.TotalTokens > measured.Policy.PlanningTokens
                            || receipt.OutputTokens > measured.Policy.OutputTarget
                        )
                    )
                        throw new ModelTransportException(
                            "Observed usage exceeded its soft planning tripwire; native work stopped."
                        );
                }
                if (method == "turn/completed")
                {
                    var completed = p.GetProperty("turn");
                    if (Text(completed, "id") != turn)
                        continue;
                    if (Text(completed, "status") != "completed" || receipt is null)
                        throw new ModelTransportException("Completion or complete usage was not confirmed.");
                    completedReceipt = receipt;
                    var body = output.Complete();
                    var after = Billing(
                        await rpc.Request(
                            "account/rateLimits/read",
                            new { supportsLunaReserve = false, excludeResetCreditDetails = true },
                            deadline.Token
                        )
                    );
                    var stop =
                        receipt.TotalTokens > (measured?.Policy.PlanningTokens ?? 5000)
                        || measured is not null && receipt.OutputTokens > measured.Policy.OutputTarget
                            ? "Observed usage exceeded its soft planning tripwire; reconcile before further work."
                        : after.IncludedUsageAllowed != true
                        || after.SpendControlReached == true
                        || after.RateLimitReachedType is not null
                            ? "Included usage is exhausted or unavailable."
                        : inspection.Billing?.CreditBalance is { } beforeBalance
                        && after.CreditBalance is { } afterBalance
                        && decimal.TryParse(
                            beforeBalance,
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var beforeValue
                        )
                        && decimal.TryParse(
                            afterBalance,
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var afterValue
                        )
                        && beforeValue != afterValue
                            ? "Observed credit balance changed; reconcile account activity before continuing."
                        : null;
                    if (stop is not null)
                        _stopped = true;
                    return receipt with
                    {
                        Body = stop is null ? body : "",
                        Before = inspection.Billing,
                        After = after,
                        StopReason = stop,
                        ContextUtf8Bytes = Encoding.UTF8.GetByteCount(input),
                        InstructionUtf8Bytes = Encoding.UTF8.GetByteCount(call.Instructions),
                    };
                }
            }
        }
        catch (Exception failure)
        {
            _stopped = true;
            if (completedReceipt is null && rpc is not null && started && thread is not null && turn is not null)
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await rpc.Request("turn/interrupt", new { threadId = thread, turnId = turn }, grace.Token);
                    while (true)
                    {
                        var e = await rpc.Read(grace.Token);
                        if (e.TryGetProperty("id", out var denied))
                        {
                            await rpc.Deny(denied, grace.Token);
                            continue;
                        }
                        if (
                            !e.TryGetProperty("params", out var p)
                            || Text(p, "threadId") != thread
                            || Text(p, "turnId") is { } eventTurn && eventTurn != turn
                        )
                            continue;
                        if (Text(e, "method") == "thread/tokenUsage/updated")
                            observedReceipt = ReadUsage(p);
                        if (Text(e, "method") == "turn/completed" && Text(p.GetProperty("turn"), "id") == turn)
                        {
                            // An interrupted terminal has no final-usage marker. Even a queued usage update
                            // may predate interrupt: retain it as partial until an ordinary completion.
                            if (Text(p.GetProperty("turn"), "status") == "completed")
                                completedReceipt = observedReceipt;
                            break;
                        }
                    }
                }
                catch { }
            }
            // Retain the inconsistent provider numbers without treating them as a settled bill.
            if (failure is ModelTransportException { Usage: not null, AccountingViolation: true })
                throw;
            if (completedReceipt is { } known)
                throw new ModelTransportException(
                    "The native turn completed but its result could not be accepted; observed usage is retained. No replay.",
                    known.TotalTokens,
                    "codex:" + known.TurnId,
                    new(known.InputTokens, known.OutputTokens, known.ReasoningTokens)
                );
            if (observedReceipt is { } partial)
                throw new ModelTransportException(
                    "Native outcome is unknown; last observed usage is retained without claiming final billing. No replay.",
                    null,
                    "codex:" + partial.TurnId,
                    new(partial.InputTokens, partial.OutputTokens, partial.ReasoningTokens)
                );
            throw;
        }
        finally
        {
            try
            {
                if (rpc is not null)
                    await rpc.DisposeAsync();
            }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                _stopped = true;
                var retained = completedReceipt ?? observedReceipt;
                throw new ModelTransportException(
                    "Owned native process cleanup is unconfirmed; observed usage is retained and continuation is held.",
                    completedReceipt?.TotalTokens,
                    retained is null ? null : "codex:" + retained.TurnId,
                    retained is null
                        ? null
                        : new(retained.InputTokens, retained.OutputTokens, retained.ReasoningTokens),
                    accountingViolation: true
                );
            }
            finally
            {
                _serial.Release();
            }
        }
    }

    private static Dictionary<string, string> ReadOnly(string work) => new() { [work] = "read" };

    private CodexAppServer Start(bool measuredRequest = false)
    {
        if (
            !Path.IsPathFullyQualified(options.Executable)
            || !File.Exists(options.Executable)
            || CompanyCodingFactory.Identity(options.Executable) != options.Identity
        )
        {
            const string reason =
                "The pinned Codex runtime is missing or changed. Restore the configured runtime or review a new pin. No model request was sent.";
            // No process exists yet. The missing butler cannot have run up a bill.
            if (measuredRequest)
                throw new ModelTransportException(reason, 0, usage: new(0, 0));
            throw new WorkspaceException(reason);
        }
        var root = Path.GetFullPath(options.ScratchRoot);
        for (var p = new DirectoryInfo(root); p is not null; p = p.Parent)
            if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException("Runtime scratch cannot traverse a reparse point.");
        Directory.CreateDirectory(root);
        var work = Path.Combine(root, "text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return new(
            options.Executable,
            options.Home,
            options.SyntheticMode,
            work,
            CodexAppServer.Arguments(PermissionProfile, ReadOnly(work), CodexAppServer.TextOnlyFeatures)
        );
    }

    private static long Number(JsonElement j, string key) =>
        j.TryGetProperty(key, out var value) && value.TryGetInt64(out var n) && n >= 0
            ? n
            : throw new ModelTransportException("Complete runtime usage is missing.");

    private static bool? Flag(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object && j.TryGetProperty(key, out var value)
            ? value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False
                    ? false
                    : null
            : null;

    private static CompanyCodexBilling Billing(JsonElement j)
    {
        var limits =
            j.TryGetProperty("rateLimitsByLimitId", out var buckets)
            && buckets.ValueKind == JsonValueKind.Object
            && buckets.TryGetProperty("codex", out var codex)
                ? codex
            : j.TryGetProperty("rateLimits", out var legacy) ? legacy
            : default;
        var credits =
            limits.ValueKind == JsonValueKind.Object
            && limits.TryGetProperty("credits", out var c)
            && c.ValueKind == JsonValueKind.Object
                ? c
                : default;
        int? used =
            limits.ValueKind == JsonValueKind.Object
            && limits.TryGetProperty("primary", out var primary)
            && primary.ValueKind == JsonValueKind.Object
            && primary.TryGetProperty("usedPercent", out var percent)
            && percent.TryGetInt32(out var n)
                ? n
                : null;
        return new(
            Flag(j, "ordinaryUsageAllowed"),
            Flag(credits, "hasCredits"),
            Text(credits, "balance"),
            Flag(limits, "spendControlReached"),
            Text(limits, "rateLimitReachedType"),
            used,
            DateTimeOffset.UtcNow
        );
    }

    private static string? Text(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object
        && j.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
