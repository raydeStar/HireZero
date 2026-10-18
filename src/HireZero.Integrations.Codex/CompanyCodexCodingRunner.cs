using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Codex;

// One live coding run for one approved execution grant (docs/adr/0014-pinned-codex-runtime.md), on the pinned Codex
// runtime with the owner's dedicated sign-in home. The sandbox lets the run write only the approved checkout and its
// own scratch folder, read the owner's named toolchain folders, and reach no network. Everything up to the turn is a
// preflight: any doubt refuses the run before anything is sent. Once sent, an uncertain outcome is never replayed.
public sealed class CompanyCodexCodingRunner : ICodingRunner, ICodingRunLifecycle, IDisposable
{
    public const string Profile = "hirezero_execution";
    private const int MessageLimit = 8000;

    private const string BaseInstructions =
        "You are the HireZero Developer working in one owner-approved checkout. Change only the files the task needs. "
        + "You may run local build and test commands. Do not commit, push, install packages, use the network or touch "
        + "anything outside the checkout. Finish with a short summary of what you changed and how you checked it.";

    private static readonly string[] CodingItems =
    [
        "agentMessage",
        "reasoning",
        "userMessage",
        "commandExecution",
        "fileChange",
    ];

    private readonly CompanyCodexOptions _options;
    private readonly CompanyModelRouting _routing;
    private readonly string[] _readRoots;
    private readonly CompanyExecutionGrant _grant;
    private readonly Func<bool> _mayStart;
    private readonly string? _pin;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancel = new();
    private CodingRun? _run;
    private Task? _work;
    private volatile bool _terminated;

    internal CompanyCodexCodingRunner(
        CompanyCodexOptions options,
        CompanyModelRouting routing,
        string[] readRoots,
        CompanyExecutionGrant grant,
        Func<bool> mayStart,
        string? pin
    )
    {
        _options = options;
        _routing = routing;
        _readRoots = readRoots;
        _grant = grant;
        _mayStart = mayStart;
        _pin = pin;
    }

    public CodingRun Start(CodingRunRequest request)
    {
        lock (_gate)
            if (_run is not null)
                throw new CodingStartRefusedException("This grant's run has already started.");
        var model = CompanyModelRouting.Native(_routing.CodingModel);
        var limits = _grant.Bounds;
        if (
            !SamePath(request.Root, _grant.Workspace)
            || request.ResumeThreadId is not null
            || request.Persistent
            || request.AllowNetwork
            || request.Environment is not null
            || request.OutputSchemaJson is not null
            || request.Model is null
            || CompanyModelRouting.Native(request.Model) != model
            || request.Effort != limits.Effort
            || request.AllowWrite != _grant.AllowWrite
        )
            throw new CodingStartRefusedException("The run request does not match its approved grant.");
        var refusal =
            CodexRuntimePin.Refusal(_options.Executable, _pin)
            ?? (
                CompanyCodingFactory.Identity(_options.Executable) != _options.Identity
                    ? "The Codex executable identity changed."
                    : null
            );
        if (refusal is not null)
            throw new CodingStartRefusedException(refusal);

        var scratch = Path.Combine(Path.GetFullPath(_options.ScratchRoot), "coding-" + Guid.NewGuid().ToString("N"));
        var temp = Path.Combine(scratch, "temp");
        Directory.CreateDirectory(temp);
        // Git inside the sandbox must not read the owner's profile configuration, which the sandbox denies and git
        // treats as fatal (live probe, 2026-10-18): it gets an empty configuration in the run's scratch folder.
        var gitConfig = Path.Combine(temp, "gitconfig");
        File.WriteAllText(gitConfig, "");
        Directory.CreateDirectory(Path.Combine(scratch, "config"));
        var filesystem = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.GetFullPath(_grant.Workspace)] = _grant.AllowWrite ? "write" : "read",
            [scratch] = "write",
        };
        foreach (var root in _readRoots)
            filesystem[root] = "read";
        CodexAppServer? server = null;
        string thread;
        string? beforeBalance;
        try
        {
            server = new CodexAppServer(
                _options.Executable,
                _options.Home,
                _options.SyntheticMode,
                scratch,
                CodexAppServer.Arguments(Profile, filesystem, CodexAppServer.CodingFeatures),
                new Dictionary<string, string>
                {
                    ["TEMP"] = temp,
                    ["TMP"] = temp,
                    ["GIT_CONFIG_GLOBAL"] = gitConfig,
                    ["GIT_CONFIG_NOSYSTEM"] = "1",
                    ["XDG_CONFIG_HOME"] = Path.Combine(scratch, "config"),
                },
                lineLimit: 4 * 1024 * 1024
            );
            using var preflight = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            preflight.CancelAfter(TimeSpan.FromSeconds(60));
            (thread, beforeBalance) = Preflight(server, filesystem, model, request, preflight.Token)
                .GetAwaiter()
                .GetResult();
            if (!_mayStart())
                throw new WorkspaceException("The grant or checkout changed before the turn.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Stop(server, scratch);
            throw new CodingStartRefusedException(
                error is WorkspaceException
                    ? error.Message
                    : "The Codex preflight failed (" + error.GetType().Name + ")."
            );
        }
        var now = DateTimeOffset.UtcNow.ToString("O");
        lock (_gate)
            _run = new(
                "codex-" + Guid.NewGuid().ToString("N"),
                "running",
                _grant.Workspace,
                request.Model,
                limits.Effort,
                model,
                null,
                null,
                [],
                now,
                now,
                thread
            );
        _work = Task.Run(() => TurnAsync(server, scratch, thread, model, request.Prompt, beforeBalance));
        return _run;
    }

    private async Task<(string Thread, string? Balance)> Preflight(
        CodexAppServer server,
        IReadOnlyDictionary<string, string> filesystem,
        string model,
        CodingRunRequest request,
        CancellationToken token
    )
    {
        await server.Initialize(token);
        var account = await server.Request("account/read", new { refreshToken = false }, token);
        if (!account.TryGetProperty("account", out var a) || CodexAppServer.Text(a, "type") != "chatgpt")
            throw new WorkspaceException(
                "The dedicated Codex home must be signed in with ChatGPT. No API-key fallback."
            );
        var limits = await server.Request(
            "account/rateLimits/read",
            new { supportsLunaReserve = false, excludeResetCreditDetails = true },
            token
        );
        var before = Account(limits);
        if (before.Exhausted)
            throw new WorkspaceException("Included usage is unavailable or exhausted. No run was sent.");
        var list = await server.Request("model/list", new { limit = 100, includeHidden = true }, token);
        if (
            list.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null
            || !list.GetProperty("data")
                .EnumerateArray()
                .Any(m => (CodexAppServer.Text(m, "model") ?? CodexAppServer.Text(m, "id")) == model)
        )
            throw new WorkspaceException("The coding model is absent from this account's catalog; no substitution.");
        await server.VerifyPermissions(Profile, filesystem, token);
        var response = await server.Request(
            "thread/start",
            new
            {
                model,
                modelProvider = "openai",
                cwd = Path.GetFullPath(request.Root),
                approvalPolicy = "never",
                permissions = Profile,
                ephemeral = true,
                allowProviderModelFallback = false,
                // Environments are omitted so the default environment gives the model its file and shell tools
                // (an empty list disables them); the workspace root is exactly the approved checkout.
                runtimeWorkspaceRoots = new[] { Path.GetFullPath(request.Root) },
                serviceName = "hirezero_execution",
                baseInstructions = BaseInstructions,
                developerInstructions = "",
            },
            token
        );
        if (
            CodexAppServer.Text(response, "model") != model
            || CodexAppServer.Text(response, "modelProvider") != "openai"
            || !response.TryGetProperty("instructionSources", out var sources)
            || sources.GetArrayLength() != 0
            || !response.TryGetProperty("thread", out var thread)
            || thread.GetProperty("ephemeral").ValueKind != JsonValueKind.True
            || !response.TryGetProperty("activePermissionProfile", out var active)
            || CodexAppServer.Text(active, "id") != Profile
            || CodexAppServer.Text(active, "extends") is not null
            || CodexAppServer.Text(response, "approvalPolicy") != "never"
            || !response.TryGetProperty("sandbox", out var sandbox)
            || CodexAppServer.Flag(sandbox, "networkAccess") != false
        )
            throw new WorkspaceException(
                "Codex thread isolation is unconfirmed: exact model, ephemeral state, the approved profile without network and zero ambient instruction sources are required. No turn was sent."
            );
        return (
            CodexAppServer.Text(thread, "id") ?? throw new WorkspaceException("Missing thread identity."),
            before.Balance
        );
    }

    private async Task TurnAsync(
        CodexAppServer server,
        string scratch,
        string thread,
        string model,
        string prompt,
        string? beforeBalance
    )
    {
        var limits = _grant.Bounds;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(limits.Seconds + 30));
        string? turn = null;
        var message = new StringBuilder();
        var finalMessage = "";
        try
        {
            var started = await server.Request(
                "turn/start",
                new
                {
                    threadId = thread,
                    model,
                    effort = limits.Effort,
                    summary = "none",
                    approvalPolicy = "never",
                    runtimeWorkspaceRoots = new[] { Path.GetFullPath(_grant.Workspace) },
                    serviceTierForTurn = "default",
                    permissions = Profile,
                    input = new[] { new { type = "text", text = prompt } },
                },
                deadline.Token
            );
            turn =
                CodexAppServer.Text(started.GetProperty("turn"), "id")
                ?? throw new WorkspaceException("Missing turn identity.");
            while (true)
            {
                var e = await server.Read(deadline.Token);
                var method = CodexAppServer.Text(e, "method");
                if (e.TryGetProperty("id", out var requestId))
                {
                    await server.Deny(requestId, deadline.Token);
                    throw new WorkspaceException(
                        "The runtime asked for an approval or permission change; the run stopped."
                    );
                }
                if (method == "error")
                    throw new WorkspaceException("Codex reported an error; no replay.");
                if (!e.TryGetProperty("params", out var p))
                    continue;
                if (method == "account/rateLimits/updated" && Account(p).Exhausted)
                    throw new WorkspaceException("Included usage was exhausted during the run; no continuation.");
                if (method == "account/updated" && CodexAppServer.Text(p, "authMode") is { } mode && mode != "chatgpt")
                    throw new WorkspaceException("The runtime's account mode changed; no provider fallback.");
                if (
                    CodexAppServer.Text(p, "threadId") != thread
                    || CodexAppServer.Text(p, "turnId") is { } eventTurn && eventTurn != turn
                )
                    continue;
                if (method is "item/started" or "item/completed")
                {
                    var item = p.GetProperty("item");
                    var kind = CodexAppServer.Text(item, "type");
                    if (kind is null || !CodingItems.Contains(kind))
                        throw new WorkspaceException(
                            "A " + (kind ?? "unknown") + " item is outside the coding scope; the run stopped."
                        );
                    if (method == "item/completed")
                        Record(item, kind, ref finalMessage, message);
                }
                if (method == "item/agentMessage/delta" && message.Length < MessageLimit)
                    message.Append(CodexAppServer.Text(p, "delta"));
                if (method == "thread/tokenUsage/updated")
                {
                    var total = p.GetProperty("tokenUsage").GetProperty("total").GetProperty("totalTokens").GetInt64();
                    Event(BudgetLedger.CodingUsage, "Reported usage.", new { tokens = total });
                    // A soft stop: usage is reported after the fact, so a turn can overshoot before this fires.
                    if (total >= limits.Tokens)
                        throw new WorkspaceException("Reported usage reached the approved limit; the run stopped.");
                }
                if (method == "turn/completed")
                {
                    var completed = p.GetProperty("turn");
                    if (CodexAppServer.Text(completed, "id") != turn)
                        continue;
                    if (CodexAppServer.Text(completed, "status") != "completed")
                        throw new WorkspaceException("The turn did not complete.");
                    var after = Account(
                        await server.Request(
                            "account/rateLimits/read",
                            new { supportsLunaReserve = false, excludeResetCreditDetails = true },
                            deadline.Token
                        )
                    );
                    if (beforeBalance is not null && after.Balance is not null && beforeBalance != after.Balance)
                        throw new WorkspaceException(
                            "The account's credit balance changed during the run; reconcile account activity first."
                        );
                    var result = finalMessage.Length > 0 ? finalMessage : message.ToString();
                    Finish("completed", result.Length <= MessageLimit ? result : result[..MessageLimit], null);
                    return;
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (turn is not null)
                await Interrupt(server, thread, turn);
            Finish(
                _cancel.IsCancellationRequested ? "canceled" : "failed",
                null,
                error is WorkspaceException ? error.Message
                    : error is OperationCanceledException ? "The run reached its approved time."
                    : "The run stopped unexpectedly (" + error.GetType().Name + ")."
            );
        }
        finally
        {
            Stop(server, scratch);
            _terminated = true;
        }
    }

    private void Record(JsonElement item, string kind, ref string finalMessage, StringBuilder message)
    {
        if (kind == "agentMessage")
        {
            finalMessage = CodexAppServer.Text(item, "text") ?? message.ToString();
            message.Clear();
        }
        else if (kind == "commandExecution")
            Event(
                "command-completed",
                "A command ran.",
                new
                {
                    command = Bounded(CodexAppServer.Text(item, "command")),
                    exitCode = item.TryGetProperty("exitCode", out var code) && code.TryGetInt32(out var n)
                        ? n
                        : (int?)null,
                }
            );
        else if (kind == "fileChange")
            Event("file-changed", "Files changed.", new { paths = Paths(item) });
    }

    private static string[] Paths(JsonElement item)
    {
        if (CodexAppServer.Text(item, "path") is { } single)
            return [Bounded(single)!];
        return item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array
            ? [.. changes.EnumerateArray().Select(c => Bounded(CodexAppServer.Text(c, "path")) ?? "").Take(50)]
            : [];
    }

    private static string? Bounded(string? text) => text is null || text.Length <= 400 ? text : text[..400];

    private static async Task Interrupt(CodexAppServer server, string thread, string turn)
    {
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await server.Request("turn/interrupt", new { threadId = thread, turnId = turn }, grace.Token);
        }
        catch { }
    }

    private static void Stop(CodexAppServer? server, string scratch)
    {
        try
        {
            server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch { }
        try
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Event(string type, string text, object data)
    {
        lock (_gate)
            if (_run is not null)
                _run = _run with
                {
                    Events =
                    [
                        .. _run.Events.TakeLast(199),
                        new(type, text, JsonSerializer.Serialize(data), DateTimeOffset.UtcNow.ToString("O")),
                    ],
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                };
    }

    private void Finish(string state, string? result, string? error)
    {
        lock (_gate)
            if (_run is not null)
                _run = _run with
                {
                    State = state,
                    ResultJson = result,
                    Error = error,
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                };
    }

    private sealed record AccountState(bool Exhausted, string? Balance);

    private static AccountState Account(JsonElement j)
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
        return new(
            CodexAppServer.Flag(j, "ordinaryUsageAllowed") == false
                || CodexAppServer.Flag(limits, "spendControlReached") == true
                || CodexAppServer.Text(limits, "rateLimitReachedType") is not null,
            CodexAppServer.Text(credits, "balance")
        );
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );

    public CodingRun? Find(string id)
    {
        lock (_gate)
            return _run?.Id == id ? _run : null;
    }

    public void Cancel(string id)
    {
        if (_run?.Id == id)
            _cancel.Cancel();
    }

    public bool Terminated(string runId) => _run?.Id == runId && _terminated;

    public void Dispose()
    {
        _cancel.Cancel();
        try
        {
            _work?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException) { }
    }
}
