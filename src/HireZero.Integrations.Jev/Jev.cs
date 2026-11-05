using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Tools.Jev;

namespace HireZero.Integrations.Jev;

// Jev (TypeSafe systemone): cheap structured choices. The key stays server-side, read as a secret (docs/SECRETS.md).
public sealed class JevClient(IHttpClientFactory clients, ModelVendorPolicy? vendorPolicy = null)
{
    private readonly ModelVendorPolicy _vendorPolicy = (vendorPolicy ?? ModelVendorPolicy.FromEnvironment()).Require(
        "jev"
    );

    public static string? Key()
    {
        var key = SecretSource.Default.Read("HIREZERO_JEV_API_KEY") ?? SecretSource.Default.Read("TYPESAFE_API_KEY");
        return !string.IsNullOrWhiteSpace(key) && !key.Contains('\r') && !key.Contains('\n') ? key.Trim() : null;
    }

    // One choice question; returns null when Jev is unavailable or answers outside the given choices.
    public async Task<(string Choice, double? Confidence)?> ChooseAsync(
        object state,
        string instructions,
        IReadOnlyDictionary<string, string> criteria,
        CancellationToken token
    )
    {
        if (Key() is not { } key)
            return null;
        try
        {
            using var client = clients.CreateClient("jev");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/systemone");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = JsonContent.Create(
                new
                {
                    model = "jev-latest",
                    state,
                    questions = new
                    {
                        answer = new
                        {
                            type = "choice",
                            instructions,
                            criteria,
                        },
                    },
                }
            );
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return null;
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            if (
                document is null
                || !document.RootElement.TryGetProperty("answers", out var answers)
                || !answers.TryGetProperty("answer", out var answer)
                || !answer.TryGetProperty("choice", out var choice)
                || choice.GetString() is not { } picked
                || !criteria.ContainsKey(picked)
            )
                return null;
            double? confidence =
                answer.TryGetProperty("confidence", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number)
                && double.IsFinite(number)
                && number is >= 0 and <= 1
                    ? number
                    : null;
            return (picked, confidence);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (token.IsCancellationRequested)
                token.ThrowIfCancellationRequested();
            return null;
        }
    }
}

// The jev.* tools. The key stays in this integration; callers send only the request body.
public sealed class JevToolProvider(IHttpClientFactory clients, ModelVendorPolicy? vendorPolicy = null)
    : IToolProvider,
        IIntegrationStatusSource
{
    private readonly ModelVendorPolicy _vendorPolicy = (vendorPolicy ?? ModelVendorPolicy.FromEnvironment()).Require(
        "jev"
    );

    // The company workspace's provider: private HTTP clients, so the profile offers no general client factory.
    public static JevToolProvider Company(ModelVendorPolicy? vendorPolicy = null) =>
        new(new PrivateClients(), vendorPolicy);

    public IntegrationStatus Status() =>
        JevClient.Key() is not null
            ? new("jev", "Jev", true, "A Jev key is set; request text is sent to Jev only when you start a Jev action.")
            : new("jev", "Jev", false, "Set HIREZERO_JEV_API_KEY to use Jev.");

    private sealed class PrivateClients : IHttpClientFactory
    {
        private readonly SocketsHttpHandler _handler = new()
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.typesafe.ai/"),
                Timeout = TimeSpan.FromSeconds(30),
            };
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(JevTools.Status, (_, _) => Task.FromResult(new JevStatus(JevClient.Key() is not null)));
        registry.Register(JevTools.Classify, ClassifyAsync);
    }

    private async Task<ServiceReply> ClassifyAsync(JevRequest request, CancellationToken token)
    {
        if (JevClient.Key() is not { } key)
            throw new ToolFailureException("unconfigured", "Jev needs its server-side API key.", 503);
        try
        {
            using var client = clients.CreateClient("jev");
            using var message = new HttpRequestMessage(HttpMethod.Post, "v1/systemone");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            message.Content = new StringContent(request.Json, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(message, token);
            return new((int)response.StatusCode, await response.Content.ReadAsStringAsync(token));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            if (token.IsCancellationRequested)
                token.ThrowIfCancellationRequested();
            throw new ToolFailureException("unavailable", "Jev is unavailable. No work was changed.");
        }
    }
}
