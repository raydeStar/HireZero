using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Integrations.Jev;

// Transport only: the host must persist exact owner consent and consume it before calling SendAsync.
// This is deliberately not a tool provider: an ordinary employee action cannot bypass metered admission.
public sealed partial class OpenRouterDecisions : IDisposable
{
    public const int MaxStateBytes = 8192;
    public const int MaxRequestBytes = 16384;
    public const int MaxResponseBytes = 32768;
    public const string Endpoint = "https://openrouter.ai/api/alpha/decisions";
    private readonly string _companyId;
    private readonly Func<string?> _key;
    private readonly HttpClient _http;

    public OpenRouterDecisions(
        string companyId,
        Func<string?> key,
        HttpMessageHandler? handler = null,
        ModelVendorPolicy? vendorPolicy = null
    )
    {
        (vendorPolicy ?? ModelVendorPolicy.FromEnvironment()).Require("openrouter");
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentNullException.ThrowIfNull(key);
        _companyId = companyId;
        _key = key; // Required company-bound reader; never consult environment or another company's key.
        _http = new HttpClient(
            handler ?? new SocketsHttpHandler { AllowAutoRedirect = false },
            disposeHandler: handler is null
        )
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public JevChoiceRequest Prepare(
        string model,
        string stateJson,
        string instructions,
        IReadOnlyDictionary<string, string> criteria
    )
    {
        if (
            model is null
            || !VersionedModel().IsMatch(model)
            || string.IsNullOrWhiteSpace(instructions)
            || instructions.Length > 1000
            || criteria is null
            || criteria.Count is < 2 or > 8
            || criteria.Any(c =>
                !ChoiceName().IsMatch(c.Key) || string.IsNullOrWhiteSpace(c.Value) || c.Value.Length > 300
            )
            || stateJson is null
            || Encoding.UTF8.GetByteCount(stateJson) > MaxStateBytes
        )
            throw new ArgumentException("Choose a versioned Jev decision model and a bounded choice question.");
        using var state = JsonDocument.Parse(stateJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (state.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.String))
            throw new ArgumentException("Decision state must be text, an object or an array.");
        UniqueProperties(state.RootElement);
        var choices = criteria.OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value);
        var json = JsonSerializer.Serialize(
            new
            {
                model,
                state = state.RootElement,
                questions = new
                {
                    answer = new
                    {
                        type = "choice",
                        instructions,
                        criteria = choices,
                    },
                },
                provider = new { allow_fallbacks = false },
            }
        );
        if (Encoding.UTF8.GetByteCount(json) > MaxRequestBytes)
            throw new ArgumentException("The serialized decision request exceeds its input limit.");
        return new(_companyId, model, json, choices.Keys.ToArray());
    }

    // Rebuild a saved packet through the same validator; the reviewed bytes must match exactly.
    public JevChoiceRequest Restore(string model, string json, string reviewedDigest)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxRequestBytes)
            throw new ArgumentException("The saved decision packet exceeds its input limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
        var root = document.RootElement;
        var answer = root.GetProperty("questions").GetProperty("answer");
        var restored = Prepare(
            model,
            root.GetProperty("state").GetRawText(),
            answer.GetProperty("instructions").GetString()!,
            answer.GetProperty("criteria").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
        );
        if (restored.Json != json || restored.Digest != reviewedDigest)
            throw new ArgumentException("The saved decision packet no longer matches its exact review.");
        return restored;
    }

    public async Task<JevDecisionResult> SendAsync(
        JevChoiceRequest request,
        string reviewedDigest,
        CancellationToken token
    )
    {
        if (request.CompanyId != _companyId || request.Digest != reviewedDigest)
            return JevDecisionResult.Refused("The exact company-bound request must be reviewed before dispatch.");
        string? key;
        // A second call must remain uncertain even if the credential was revoked or its token was canceled.
        // Serialize preflight with the latch so neither can turn a previously dispatched call into zero usage.
        lock (request.DispatchGate)
        {
            if (request.Dispatched)
                return JevDecisionResult.Unknown("This request object was already dispatched. It was not sent again.");
            if (token.IsCancellationRequested)
                return JevDecisionResult.Refused("The decision request was canceled before dispatch.");
            key = _key();
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
                return JevDecisionResult.Refused("This company has no usable OpenRouter credential.");
            request.Dispatched = true;
        }
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(request.Json, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        // ResponseHeadersRead ends HttpClient's timeout at the headers; keep a deadline over the body too.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 400 or 401 or 402 or 403 or 404 or 413 or 422 or 429)
                return JevDecisionResult.Refused(
                    $"OpenRouter refused the decision request (HTTP {(int)response.StatusCode})."
                );
            if (!response.IsSuccessStatusCode)
                return JevDecisionResult.Unknown(
                    $"OpenRouter returned HTTP {(int)response.StatusCode}; the outcome is unknown."
                );
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                return JevDecisionResult.Unknown(
                    "The decision response exceeded its size limit; the outcome is unknown."
                );
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[MaxResponseBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), token);
                if (read == 0)
                    break;
                count += read;
            }
            if (count > MaxResponseBytes)
                return JevDecisionResult.Unknown(
                    "The decision response exceeded its size limit; the outcome is unknown."
                );
            return Parse(buffer.AsMemory(0, count), request);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            // Losing the reply cannot tell us whether the provider worked. No second order leaves the kitchen.
            return JevDecisionResult.Unknown("The decision reply was lost or timed out. Do not replay the request.");
        }
    }

    private static JevDecisionResult Parse(ReadOnlyMemory<byte> bytes, JevChoiceRequest request)
    {
        ModelUsage? usage = null;
        decimal? reportedCost = null;
        string? receipt = null,
            model = null;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            UniqueProperties(root);
            var id =
                root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
                    ? idValue.GetString()
                    : null;
            if (id is not null && ReceiptId().IsMatch(id))
                receipt = "openrouter-decisions:" + id;
            model =
                root.TryGetProperty("model", out var modelValue) && modelValue.ValueKind == JsonValueKind.String
                    ? modelValue.GetString()
                    : null;
            var u = root.GetProperty("usage");
            decimal? cost =
                u.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : null;
            if (cost is < 0)
                throw new JsonException();
            reportedCost = cost;
            var input = u.GetProperty("input_tokens").GetInt64();
            var output = u.GetProperty("output_tokens").GetInt64();
            if (input < 0 || output < 0 || input > long.MaxValue - output)
                throw new JsonException();
            usage = new(input, output, BilledUsd: cost);
            if (receipt is null || cost is null || model is null || !MatchesModel(request.Model, model))
                throw new JsonException();
            var answers = root.GetProperty("answers");
            if (answers.EnumerateObject().Count() != 1)
                throw new JsonException();
            var answer = answers.GetProperty("answer");
            var choice = answer.GetProperty("choice").GetString();
            if (
                answer.GetProperty("type").GetString() != "choice"
                || choice is null
                || !request.Choices.Contains(choice)
            )
                throw new JsonException();
            var confidence = Probability(answer.GetProperty("confidence"));
            var probabilities = answer
                .GetProperty("probabilities")
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => Probability(p.Value));
            if (
                !probabilities.Keys.Order(StringComparer.Ordinal).SequenceEqual(request.Choices)
                || Math.Abs(probabilities.Values.Sum() - 1) > 0.001
            )
                throw new JsonException();
            return new("completed", choice, confidence, probabilities, usage, receipt, model, null, reportedCost);
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
                        or OverflowException
                        or ArgumentException
            )
        {
            return new(
                usage?.BilledUsd is not null && receipt is not null ? "answer_withheld" : "unknown",
                null,
                null,
                null,
                usage,
                receipt,
                model,
                "The decision response did not satisfy its reviewed contract. Valid accounting fields are retained; do not replay it.",
                reportedCost
            );
        }
    }

    private static bool MatchesModel(string requested, string resolved) =>
        requested == resolved
        || resolved.StartsWith(requested + "-", StringComparison.Ordinal)
            && resolved.Length == requested.Length + 9
            && resolved[^8..].All(char.IsAsciiDigit);

    private static double Probability(JsonElement value)
    {
        var number = value.GetDouble();
        if (!double.IsFinite(number) || number is < 0 or > 1)
            throw new JsonException();
        return number;
    }

    private static void UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            {
                if (!names.Add(p.Name))
                    throw new JsonException("Duplicate JSON properties are ambiguous.");
                UniqueProperties(p.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                UniqueProperties(item);
    }

    [GeneratedRegex(@"\Atypesafe/jev-[0-9]+\.[0-9]+(?:-[0-9]{8})?\z")]
    private static partial Regex VersionedModel();

    [GeneratedRegex(@"\A[a-z][a-z0-9-]{0,31}\z")]
    private static partial Regex ChoiceName();

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,200}\z")]
    private static partial Regex ReceiptId();

    public void Dispose() => _http.Dispose();
}

public sealed class JevChoiceRequest
{
    public string CompanyId { get; }
    public string Model { get; }
    public string Json { get; }
    public string Digest { get; }
    internal IReadOnlyList<string> Choices { get; }

    // A small second latch, not a durable receipt. The host still owns restart-safe admission.
    internal object DispatchGate { get; } = new();
    internal bool Dispatched { get; set; }

    internal JevChoiceRequest(string companyId, string model, string json, string[] choices)
    {
        CompanyId = companyId;
        Model = model;
        Json = json;
        Digest = CompanyJson.Digest(
            new
            {
                companyId,
                model,
                json,
            }
        );
        Choices = Array.AsReadOnly(choices);
    }
}

public sealed record JevDecisionResult(
    string State,
    string? Choice,
    double? Confidence,
    IReadOnlyDictionary<string, double>? Probabilities,
    ModelUsage? Usage,
    string? Receipt,
    string? ResolvedModel,
    string? Error,
    decimal? ReportedCostUsd = null
)
{
    internal static JevDecisionResult Refused(string error) =>
        new("not_sent", null, null, null, new(0, 0, BilledUsd: 0), null, null, error, 0);

    internal static JevDecisionResult Unknown(string error) =>
        new("unknown", null, null, null, null, null, null, error);
}
