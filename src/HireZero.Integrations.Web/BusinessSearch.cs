using System.Net;
using System.Text.Json;
using HireZero.Tools.Web;

namespace HireZero.Integrations.Web;

// One official API request. It returns candidates only, and never follows result URLs or retries a possible charge.
public sealed class BusinessSearch(Func<string?> key, HttpMessageHandler? handler = null) : IToolProvider
{
    public const string KeySecret = "HIREZERO_BRAVE_SEARCH_API_KEY";
    private readonly HttpClient _http = new(
        handler
            ?? new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }
    )
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public void Register(ToolRegistry registry) => registry.Register(WebTools.Search, SearchAsync);

    public async Task<WebSearchResult> SearchAsync(WebSearchRequest request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        token = deadline.Token;
        var query = ContentGuard.Bounded(request.Query, 550);
        if (query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 70)
            throw new ToolFailureException("refused", "Use a shorter business name and location for search.", 422);
        string? secret;
        try
        {
            secret = key();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new ToolFailureException(
                "unavailable",
                "The search credential could not be read. Ask the workspace operator to check its configuration.",
                503
            );
        }
        if (string.IsNullOrWhiteSpace(secret))
            throw new ToolFailureException(
                "unavailable",
                "Website search is not configured. Ask the workspace operator to configure Brave Search, or enter the website yourself.",
                503
            );
        if (secret.Length > 4096 || secret.Any(char.IsControl))
            throw new ToolFailureException(
                "unavailable",
                "The configured search credential has an invalid format. Ask the workspace operator to replace it.",
                503
            );
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.search.brave.com/res/v1/web/search?q="
                + Uri.EscapeDataString(query)
                + "&count=5&result_filter=web&safesearch=strict"
        );
        message.Headers.Add("X-Subscription-Token", secret);
        message.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new ToolFailureException(
                "failed",
                response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "The search service is rate limited. This request was not retried."
                    : "The search service declined or could not finish this request. It was not retried.",
                502
            );
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes, token)) > 0)
        {
            if (buffer.Length + read > 256 * 1024)
                throw new ToolFailureException("refused", "Search returned more data than allowed.", 422);
            buffer.Write(bytes, 0, read);
        }
        try
        {
            using var json = JsonDocument.Parse(buffer.ToArray());
            if (!json.RootElement.TryGetProperty("web", out var web) || !web.TryGetProperty("results", out var results))
                return new([]);
            var candidates = new List<WebCandidate>();
            foreach (var item in results.EnumerateArray().Take(20))
            {
                if (!item.TryGetProperty("url", out var address) || address.ValueKind != JsonValueKind.String)
                    continue;
                Uri url;
                try
                {
                    url = WebReader.Checked(ContentGuard.Bounded(address.GetString()!, 800));
                }
                catch (WorkspaceException)
                {
                    continue;
                }
                if (url.ToString().Length > 800 || url.Query.Length > 0 || url.Port != 443)
                    continue;
                if (!url.Host.Contains('.') || url.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                    continue;
                var cleanUrl = new UriBuilder(url) { Fragment = "" }
                    .Uri
                    .AbsoluteUri;
                if (candidates.Any(c => new Uri(c.Url).Host == url.Host))
                    continue;
                string Text(string name, int maximum) =>
                    item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                        ? ContentGuard.Clean(WebReader.Text(value.GetString()!), maximum).Text
                        : "";
                candidates.Add(new(cleanUrl, Text("title", 160), Text("description", 350)));
                if (candidates.Count == 5)
                    break;
            }
            return new([.. candidates]);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new ToolFailureException(
                "failed",
                "Search returned an unreadable response. No candidates were imported.",
                502
            );
        }
    }
}
