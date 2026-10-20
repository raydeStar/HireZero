using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HireZero.Integrations.ModelApis;

// Employees' model through a provider's HTTP API with an API key (docs/adr/0036-api-model-provider.md): Anthropic for
// anthropic/* routes, OpenAI for openai/* routes, and OpenRouter for any route when its key is set. It follows the same
// one-job measured path as the pinned Codex runtime: no automatic work, one owner-authorized job at a time, never
// retried. Unlike Codex it enforces money: the job's allowance is a cap in US cents, checked against the worst case
// (every input byte a token, plus the full output limit, at the route's reviewed price) before anything is sent.
public sealed class ApiModelProvider : IModelProvider, IMeasuredModelProvider, IDisposable
{
    public const string AnthropicKey = "HIREZERO_ANTHROPIC_API_KEY";
    public const string OpenAIKey = "HIREZERO_OPENAI_API_KEY";
    public const string OpenRouterKey = "HIREZERO_OPENROUTER_API_KEY";
    public const string Identity = "api-models-v1";

    // An upper bound on the tokens a chat API adds around the system and user text (role markers and separators).
    public const int FramingTokens = 256;

    private readonly Func<string, string?> _secret;
    private readonly Func<string, ModelPrice?> _price;
    private readonly HttpClient _http;

    public ApiModelProvider(
        Func<string, string?>? secret = null,
        HttpMessageHandler? handler = null,
        Func<string, ModelPrice?>? price = null
    )
    {
        _secret = secret ?? SecretSource.Default.Read;
        _price = price ?? ModelRoutes.Price;
        _http = new HttpClient(
            handler
                ?? new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                },
            disposeHandler: handler is null
        )
        {
            Timeout = TimeSpan.FromSeconds(55),
        };
    }

    public string RuntimeIdentity => Identity;

    // Automatic work stays off, as with Codex: every call is one owner-authorized measured job.
    public bool Qualified => false;

    public string Status =>
        "API model selected ("
        + (string.Join(", ", Keys()) is { Length: > 0 } keys ? keys : "no API key set")
        + "). Automatic model work is off; each job runs once after you authorize it, capped at its allowance in US cents.";

    public ModelInputAccounting CountInput(ModelCall call) =>
        throw new ModelUnavailableException(
            "API models run only as one-job measured work; there is no automatic admission counter."
        );

    public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
        throw new ModelUnavailableException(Status);

    // OpenRouter's key under either name, as the OpenRouter choice tool reads it.
    private string? OpenRouter() => _secret(OpenRouterKey) is { Length: > 0 } key ? key : _secret("OPENROUTER_API_KEY");

    private IEnumerable<string> Keys() =>
        new[]
        {
            string.IsNullOrEmpty(_secret(AnthropicKey)) ? null : "anthropic",
            string.IsNullOrEmpty(_secret(OpenAIKey)) ? null : "openai",
            string.IsNullOrEmpty(OpenRouter()) ? null : "openrouter",
        }.OfType<string>();

    // The API that serves a route: the vendor's own when its key is set, otherwise OpenRouter, which takes vendor/model ids.
    public (string Api, string Model, string Key)? Endpoint(string route)
    {
        var vendor = ModelRoutes.Provider(route);
        if (vendor == "anthropic" && _secret(AnthropicKey) is { Length: > 0 } anthropic)
            return ("anthropic", ModelRoutes.Native(route), anthropic);
        if (vendor == "openai" && _secret(OpenAIKey) is { Length: > 0 } openai)
            return ("openai", ModelRoutes.Native(route), openai);
        if (OpenRouter() is { Length: > 0 } openrouter)
            return ("openrouter", route, openrouter);
        return null;
    }

    // The most a job can cost: every application byte counted as a token, the framing bound, and the full output limit.
    public static decimal WorstCaseUsd(ModelPrice price, int applicationBytes, int maxOutputTokens) =>
        ((applicationBytes + FramingTokens) * price.InputPerMillion + maxOutputTokens * price.OutputPerMillion)
        / 1_000_000m;

    // Refused before anything is sent: no usage, no receipt.
    private static ModelTransportException NotSent(string message) => new(message, 0, null, new ModelUsage(0, 0));

    public async Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
    {
        var input = CompanyJson.Encode(call.Context);
        var bytes = Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(call.Instructions);
        if (
            job.State != "dispatched"
            || job.RuntimeIdentity != RuntimeIdentity
            || !CompanyMeasuredPolicy.Known(job.Policy)
            || job.CreditAllowance is <= 0 or > 1000
            || job.Route != call.ModelRoute
            || job.ContextDigest != CompanyJson.Digest(call.Context)
            || job.InstructionsDigest != ContentGuard.Digest(call.Instructions)
            || bytes != job.ApplicationBytes
            || bytes > job.Policy.ApplicationBytes
        )
            throw new WorkspaceException("The exact consumed measured authorization is required.");
        var route = call.ModelRoute!;
        var price =
            _price(route)
            ?? throw NotSent(
                $"The route {route} has no reviewed price, so its spending cannot be capped. Add it to the model prices."
            );
        // A measured call carries no output limit of its own (the store builds it with 0); its policy's output target is
        // the limit, both for the worst case and for the request, so nothing is sent with a zero or unbounded limit.
        var maxOutput = call.MaxOutputTokens > 0 ? call.MaxOutputTokens : job.Policy.OutputTarget;
        var worst = WorstCaseUsd(price, bytes, maxOutput);
        var allowance = job.CreditAllowance / 100m;
        if (worst > allowance)
            throw NotSent(
                $"This job could cost up to ${worst:0.0000} on {route}, more than its allowance of {job.CreditAllowance:0.##} cents. Raise the allowance or narrow the context."
            );
        var endpoint =
            Endpoint(route) ?? throw NotSent($"No API key serves {route}. Set its vendor's key or an OpenRouter key.");

        var (url, body) =
            endpoint.Api == "anthropic"
                ? (
                    "https://api.anthropic.com/v1/messages",
                    new JsonObject
                    {
                        ["model"] = endpoint.Model,
                        ["max_tokens"] = maxOutput,
                        ["system"] = call.Instructions,
                        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = input }),
                    }
                )
                : (
                    endpoint.Api == "openai"
                        ? "https://api.openai.com/v1/chat/completions"
                        : "https://openrouter.ai/api/v1/chat/completions",
                    new JsonObject
                    {
                        ["model"] = endpoint.Model,
                        ["max_completion_tokens"] = maxOutput,
                        ["messages"] = new JsonArray(
                            new JsonObject { ["role"] = "system", ["content"] = call.Instructions },
                            new JsonObject { ["role"] = "user", ["content"] = input }
                        ),
                    }
                );
        // Through OpenRouter (docs/adr/0042-openrouter-low-effort-and-a-price-ceiling.md): reasoning stays at low effort,
        // and the reviewed price is a ceiling OpenRouter enforces, so a route whose price varies by request (a router
        // such as Jev) can never be billed above what the worst case assumed.
        if (endpoint.Api == "openrouter")
        {
            body["reasoning"] = new JsonObject { ["effort"] = "low" };
            body["provider"] = new JsonObject
            {
                ["max_price"] = new JsonObject
                {
                    ["prompt"] = price.InputPerMillion,
                    ["completion"] = price.OutputPerMillion,
                },
            };
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (endpoint.Api == "anthropic")
        {
            request.Headers.Add("x-api-key", endpoint.Key);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
            request.Headers.Authorization = new("Bearer", endpoint.Key);

        // From here the request may have reached the provider: anything unconfirmed propagates as unknown and is never
        // retried. A clear 4xx refusal was not processed, so it is reported with no usage.
        using var response = await _http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if ((int)response.StatusCode is >= 400 and < 500)
            throw NotSent(
                $"The {endpoint.Api} API refused the request (HTTP {(int)response.StatusCode}{ErrorType(text)})."
            );
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"The {endpoint.Api} API failed (HTTP {(int)response.StatusCode}); the outcome is unknown."
            );

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var (reply, stop, usage, id) = endpoint.Api == "anthropic" ? Anthropic(root) : OpenAI(root);
        var receipt = endpoint.Api + ":" + id;
        if (!usage.Consistent(usage.InputTokens + usage.OutputTokens))
            throw new ModelTransportException(
                "The provider reported inconsistent usage.",
                null,
                receipt,
                null,
                accountingViolation: true
            );
        var total = usage.InputTokens + usage.OutputTokens;
        // The input bound is part of the spending cap; a provider that added more than it allows is an accounting fault.
        if (usage.InputTokens > bytes + FramingTokens)
            throw new ModelTransportException(
                "The provider counted more input than the bound allows.",
                total,
                receipt,
                usage,
                accountingViolation: true
            );
        if (stop != "complete")
            throw new ModelTransportException(stop, total, receipt, usage);
        return new(reply, total, receipt, usage);
    }

    private static (string Text, string Stop, ModelUsage Usage, string Id) Anthropic(JsonElement root)
    {
        var text = string.Concat(
            root.GetProperty("content")
                .EnumerateArray()
                .Where(c => c.GetProperty("type").GetString() == "text")
                .Select(c => c.GetProperty("text").GetString())
        );
        var stopReason = root.TryGetProperty("stop_reason", out var s) ? s.GetString() : null;
        var stop = stopReason switch
        {
            "end_turn" or "stop_sequence" => "complete",
            "max_tokens" => "The reply reached its output limit and was withheld as truncated.",
            "tool_use" => "The model asked for a tool; employees have none, so the reply was withheld.",
            _ => "The reply ended unexpectedly (" + stopReason + ") and was withheld.",
        };
        var u = root.GetProperty("usage");
        return (
            text,
            stop,
            new(u.GetProperty("input_tokens").GetInt64(), u.GetProperty("output_tokens").GetInt64()),
            root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""
        );
    }

    private static (string Text, string Stop, ModelUsage Usage, string Id) OpenAI(JsonElement root)
    {
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var text =
            message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "";
        var finish = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() : null;
        var stop =
            message.TryGetProperty("tool_calls", out var tools)
            && tools.ValueKind == JsonValueKind.Array
            && tools.GetArrayLength() > 0
                ? "The model asked for a tool; employees have none, so the reply was withheld."
                : finish switch
                {
                    "stop" => "complete",
                    "length" => "The reply reached its output limit and was withheld as truncated.",
                    "content_filter" => "The provider filtered the reply; it was withheld.",
                    _ => "The reply ended unexpectedly (" + finish + ") and was withheld.",
                };
        var u = root.GetProperty("usage");
        long? reasoning =
            u.TryGetProperty("completion_tokens_details", out var d)
            && d.ValueKind == JsonValueKind.Object
            && d.TryGetProperty("reasoning_tokens", out var r)
            && r.ValueKind == JsonValueKind.Number
                ? r.GetInt64()
                : null;
        return (
            text,
            stop,
            new(u.GetProperty("prompt_tokens").GetInt64(), u.GetProperty("completion_tokens").GetInt64(), reasoning),
            root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""
        );
    }

    private static string ErrorType(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var error = document.RootElement.TryGetProperty("error", out var e) ? e : default;
            var type =
                error.ValueKind == JsonValueKind.Object && error.TryGetProperty("type", out var t) ? t.GetString()
                : error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c) ? c.ToString()
                : null;
            return type is { Length: > 0 and <= 60 } ? ", " + type : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public void Dispose() => _http.Dispose();
}
