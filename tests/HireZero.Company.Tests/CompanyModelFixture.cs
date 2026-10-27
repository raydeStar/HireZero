using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;

// Fictional API traffic stays inside this handler. No company key ever leaves the test process.
public sealed class CompanyModelFixture : HttpMessageHandler
{
    public static CompanyApiModel[] Models() =>
        [
            new("openai/fictional-luna", "Fictional Luna", new(3m, 15m)),
            new("openai/fictional-sol", "Fictional Sol", new(12m, 60m)),
            new("anthropic/fictional-wisp", "Fictional Wisp", new(3m, 15m)),
        ];

    public readonly List<(string Provider, string Key)> Requests = [];
    public readonly List<string> RequestedModels = [];
    public bool Refuse;
    public bool Unknown;
    public TaskCompletionSource? Started;
    public TaskCompletionSource? Release;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var provider = request.RequestUri!.Host;
        if (provider is not ("api.openai.com" or "api.anthropic.com" or "openrouter.ai"))
            throw new InvalidOperationException("The fictional butler refuses an unexpected address.");
        var key =
            request.Headers.Authorization?.Parameter
            ?? (request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : "");
        Requests.Add((provider, key));
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        RequestedModels.Add(body.RootElement.GetProperty("model").GetString()!);
        Started?.TrySetResult();
        if (Release is not null)
            await Release.Task.WaitAsync(token);
        if (Unknown)
            throw new HttpRequestException("Fictional response lost after send.");
        return new(Refuse ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
        {
            Content = new StringContent(
                provider == "api.anthropic.com"
                    ? """{"id":"msg_fictional","content":[{"type":"text","text":"Fictional company draft."}],"stop_reason":"end_turn","usage":{"input_tokens":120,"output_tokens":30}}"""
                    : """{"id":"chatcmpl-fictional","choices":[{"message":{"content":"Fictional company draft."},"finish_reason":"stop"}],"usage":{"prompt_tokens":120,"completion_tokens":30}}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
    }
}
