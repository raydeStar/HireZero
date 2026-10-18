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
public sealed class CompanyCodexConnection(CompanyCodexOptions options, CompanyModelRouting? routing = null)
    : IModelProvider,
        IMeasuredModelProvider
{
    private const string PermissionProfile = "hirezero_qualification";
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CompanyModelRouting _routing = Servable(routing ?? new());

    // This connection serves OpenAI routes on the Codex runtime only.
    private static CompanyModelRouting Servable(CompanyModelRouting routing)
    {
        routing.Validate();
        if (
            routing.Runtime != "codex"
            || new[] { routing.TextModel, routing.CodingModel }.Any(r => ModelRoutes.Provider(r) != "openai")
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

    private async Task<CompanyCodexInspection> Inspect(Rpc rpc, CancellationToken token)
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
            || job.Policy != CompanyMeasuredPolicy.Current
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
        if (call.ModelRoute is not null && call.ModelRoute != _routing.TextModel)
            throw new WorkspaceException("Text dispatch cannot select the coding route.");
        var bytes = Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(call.Instructions);
        if (
            measured is null
                && (
                    call.Instructions.Length > QualificationInstructionCharacterLimit
                    || Encoding.UTF8.GetByteCount(input) > QualificationContextByteLimit
                    || call.Context.Input.Length > 1000
                )
            || measured is not null && (bytes != measured.ApplicationBytes || bytes > measured.Policy.ApplicationBytes)
            || string.IsNullOrWhiteSpace(call.Context.EmployeeId)
            || call.Context.Id.Length == 0
        )
            throw new WorkspaceException("Narrow the fictional qualification context.");
        await _serial.WaitAsync(token);
        string? thread = null,
            turn = null;
        var started = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        Rpc? rpc = null;
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
                CompanyModelRouting.Native(_routing.TextModel),
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
            rpc = Start();
            var inspection = await Inspect(rpc, deadline.Token);
            var model = CompanyModelRouting.Native(_routing.TextModel);
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
            await rpc.VerifyPermissions(deadline.Token);
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
                    effort = "low",
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
            var output = new StringBuilder();
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
                    if (method == "item/completed" && kind == "agentMessage" && output.Length == 0)
                        output.Append(Text(item, "text"));
                }
                if (method == "item/agentMessage/delta")
                    output.Append(Text(p, "delta"));
                if (output.Length > (measured?.Policy.VisibleCharacterStop ?? 2000))
                    throw new ModelTransportException(
                        "Local output character allowance exceeded; native work stopped."
                    );
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
                    if (Text(completed, "status") != "completed" || receipt is null || output.Length == 0)
                        throw new ModelTransportException("Completion or complete usage was not confirmed.");
                    completedReceipt = receipt;
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
                        Body = stop is null ? output.ToString() : "",
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
                    "The native turn completed but final account reconciliation failed; observed usage is retained. No replay.",
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

    private Rpc Start()
    {
        if (
            !Path.IsPathFullyQualified(options.Executable)
            || CompanyCodingFactory.Identity(options.Executable) != options.Identity
        )
            throw new WorkspaceException("Pinned Codex executable identity changed.");
        var root = Path.GetFullPath(options.ScratchRoot);
        for (var p = new DirectoryInfo(root); p is not null; p = p.Parent)
            if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException("Runtime scratch cannot traverse a reparse point.");
        Directory.CreateDirectory(root);
        var work = Path.Combine(root, "text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return new(options, work);
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

    private sealed class Rpc : IAsyncDisposable
    {
        private readonly Process _process;
        private int _id;
        private readonly Queue<JsonElement> _events = new();
        public string WorkingDirectory { get; }

        public Rpc(CompanyCodexOptions options, string work)
        {
            // app-server stdio is UTF-8, independent of the Windows console code page.
            WorkingDirectory = work;
            var info = new ProcessStartInfo(options.Executable)
            {
                WorkingDirectory = work,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            var environment = info
                .Environment.Where(x =>
                    new[]
                    {
                        "PATH",
                        "SystemRoot",
                        "WINDIR",
                        "USERPROFILE",
                        "HOMEDRIVE",
                        "HOMEPATH",
                        "APPDATA",
                        "LOCALAPPDATA",
                        "TEMP",
                        "TMP",
                        "CODEX_HOME",
                    }.Contains(x.Key, StringComparer.OrdinalIgnoreCase)
                )
                .ToArray();
            info.Environment.Clear();
            foreach (var (key, value) in environment)
                info.Environment[key] = value;
            if (options.Home is not null)
                info.Environment["CODEX_HOME"] = Path.GetFullPath(options.Home);
            if (options.SyntheticMode is not null)
                info.Environment["HIREZERO_FIXTURE_MODE"] = options.SyntheticMode;
            foreach (var a in Arguments(work))
                info.ArgumentList.Add(a);
            _process = Process.Start(info) ?? throw new WorkspaceException("Codex did not start.");
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginErrorReadLine();
        }

        private static IEnumerable<string> Arguments(string work)
        {
            yield return "app-server";
            yield return "--stdio";
            yield return "--strict-config";
            if (OperatingSystem.IsWindows())
            {
                yield return "-c";
                yield return "windows.sandbox=\"mxc\"";
            }
            // The pinned native deserializer rejects legacy readOnly.access, even though the
            // generated schema permits unknown properties. Configure restricted reads by name.
            yield return "-c";
            yield return "permissions={"
                + PermissionProfile
                + "={filesystem={"
                + JsonSerializer.Serialize(work)
                + "=\"read\"},network={enabled=false}}}";
            yield return "-c";
            yield return "default_permissions=\"" + PermissionProfile + "\"";
            foreach (
                var value in new[]
                {
                    "model_provider=\"openai\"",
                    "forced_login_method=\"chatgpt\"",
                    "mcp_servers={}",
                    "plugins={}",
                    "project_doc_max_bytes=0",
                    "developer_instructions=\"\"",
                    "include_environment_context=false",
                    "web_search=\"disabled\"",
                    "skills.bundled.enabled=false",
                    "skills.include_instructions=false",
                    "memories.generate_memories=false",
                    "memories.use_memories=false",
                    "tools.update_plan.enabled=false",
                    "tools.experimental_request_user_input.enabled=false",
                }
            )
            {
                yield return "-c";
                yield return value;
            }
            foreach (
                var feature in new[]
                {
                    "shell_tool",
                    "shell_snapshot",
                    "apps",
                    "plugins",
                    "remote_plugin",
                    "memories",
                    "multi_agent",
                    "hooks",
                    "computer_use",
                    "browser_use",
                    "image_generation",
                    "view_image",
                    "request_permissions_tool",
                    "skill_search",
                    "goals",
                    "code_mode_host",
                    "workspace_dependencies",
                    "sleep_tool",
                    "unbounded_connection_retries",
                }
            )
            {
                yield return "--disable";
                yield return feature;
            }
        }

        public async Task Initialize(CancellationToken token)
        {
            await Request(
                "initialize",
                new
                {
                    clientInfo = new { name = "hirezero_company", version = "0.1.0" },
                    capabilities = new { experimentalApi = true },
                },
                token
            );
            await Write(new { method = "initialized", @params = new { } }, token);
        }

        public async Task VerifyPermissions(CancellationToken token)
        {
            var config = (
                await Request("config/read", new { cwd = WorkingDirectory, includeLayers = false }, token)
            ).GetProperty("config");
            // An allowed named profile does not enable the Windows execution backend. With
            // no backend, command/exec can read outside the declared restricted roots.
            // This necessary configuration gate is not a substitute for behavioral qualification.
            if (
                OperatingSystem.IsWindows()
                && (!config.TryGetProperty("windows", out var windows) || Text(windows, "sandbox") != "mxc")
            )
                throw new WorkspaceException(
                    "Native Windows sandbox enforcement is not configured as explicit MXC for restricted reads. No model turn was sent."
                );
            if (
                Text(config, "default_permissions") != PermissionProfile
                || !config.TryGetProperty("permissions", out var profiles)
                || !profiles.TryGetProperty(PermissionProfile, out var profile)
                || profile.ValueKind != JsonValueKind.Object
                || profile
                    .EnumerateObject()
                    .Any(p => p.Name is not ("filesystem" or "network") && p.Value.ValueKind != JsonValueKind.Null)
                || !profile.TryGetProperty("filesystem", out var fs)
                || fs.ValueKind != JsonValueKind.Object
                || fs.EnumerateObject().Count(p => p.Value.ValueKind != JsonValueKind.Null) != 1
                || Text(fs, WorkingDirectory) != "read"
                || !profile.TryGetProperty("network", out var network)
                || Flag(network, "enabled") != false
                || network.EnumerateObject().Any(p => p.Name != "enabled" && p.Value.ValueKind != JsonValueKind.Null)
            )
                throw new WorkspaceException(
                    "Codex restricted permissions are unconfirmed: only the empty scratch root may be read, with no network or inherited profile."
                );
            var available = await Request("permissionProfile/list", new { cwd = WorkingDirectory, limit = 100 }, token);
            if (
                available.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null
                || !available
                    .GetProperty("data")
                    .EnumerateArray()
                    .Any(p => Text(p, "id") == PermissionProfile && Flag(p, "allowed") == true)
            )
                throw new WorkspaceException(
                    "Codex did not allow the selected restricted permissions profile. No model turn was sent."
                );
        }

        private async Task Write(object value, CancellationToken token) =>
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token);

        public async Task<JsonElement> Request(string method, object parameters, CancellationToken token)
        {
            var id = ++_id;
            await Write(
                new
                {
                    id,
                    method,
                    @params = parameters,
                },
                token
            );
            while (true)
            {
                var j = await ReadWire(token);
                if (j.TryGetProperty("id", out var n) && n.TryGetInt32(out var value) && value == id)
                {
                    if (j.TryGetProperty("error", out var error))
                    {
                        var code =
                            error.TryGetProperty("code", out var rawCode) && rawCode.TryGetInt32(out var errorCode)
                                ? errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : "unknown";
                        throw new WorkspaceException(
                            "Codex refused the supported protocol request: "
                                + method
                                + " (code "
                                + code
                                + "). "
                                + SafeDiagnostic(error)
                                + " No automatic replay."
                        );
                    }
                    return j.GetProperty("result").Clone();
                }
                if (j.TryGetProperty("id", out var serverId))
                {
                    await Deny(serverId, token);
                    throw new WorkspaceException("Codex requested an unapproved action during preflight.");
                }
                if (_events.Count >= 256)
                    throw new WorkspaceException("Too many runtime events.");
                _events.Enqueue(j);
            }
        }

        private static string SafeDiagnostic(JsonElement error)
        {
            // Emit only exact public protocol literals, never arbitrary server text or error.data.
            var message = Text(error, "message");
            foreach (
                var known in new[]
                {
                    "readOnly.access is no longer supported; use permissionProfile for restricted reads",
                    "workspaceWrite.readOnlyAccess is no longer supported; use permissionProfile for restricted reads",
                    "`permissions` cannot be combined with `sandboxPolicy`",
                    "`permissions` cannot be combined with `sandbox`",
                }
            )
                if (message == known || message == "Invalid request: " + known)
                    return known + ".";
            return "Server message/details withheld.";
        }

        public Task Deny(JsonElement id, CancellationToken token) =>
            Write(
                new
                {
                    id = id.Clone(),
                    error = new
                    {
                        code = -32601,
                        message = "HireZero qualification declines all tools, approvals and authentication changes.",
                    },
                },
                token
            );

        public Task<JsonElement> Read(CancellationToken token) =>
            _events.Count > 0 ? Task.FromResult(_events.Dequeue()) : ReadWire(token);

        private async Task<JsonElement> ReadWire(CancellationToken token)
        {
            var line =
                await _process.StandardOutput.ReadLineAsync(token)
                ?? throw new ModelTransportException("Codex exited without a confirmed outcome.");
            if (line.Length > 131072)
                throw new ModelTransportException("Runtime event size exceeded its local allowance.");
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                _process.Dispose();
                if (Directory.Exists(WorkingDirectory) && !Directory.EnumerateFileSystemEntries(WorkingDirectory).Any())
                    Directory.Delete(WorkingDirectory);
            }
        }
    }
}
