using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireZero.Tools.Analytics;

namespace HireZero.Integrations.SiteAnalytics;

// A site's own cookie-free counts (docs/adr/0173-site-analytics.md): daily visitors and page views, launch-list sign-ups
// and new accounts, read from the site's MCP endpoint with one of its agent keys through the site's analytics_summary
// and signups_daily tools. Off until <data root>/site-analytics.json names the endpoint and the key is set. Read-only:
// it calls no other tool, follows no redirects and sends the key to that endpoint only.
public sealed class SiteAnalyticsProvider : IToolProvider, IIntegrationStatusSource, IConfigurableIntegration
{
    public const string KeySecret = "HIREZERO_SITE_ANALYTICS_KEY";

    // How many days, counting today, the site keeps page views, and sign-ups.
    public const int ViewDays = 90;
    public const int SignUpDays = 400;
    private const int MaxReplyBytes = 512 * 1024;

    private sealed record Settings(string? McpUrl);

    private readonly string _dataRoot;
    private readonly Func<string?> _key;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private int _requests;

    public SiteAnalyticsProvider(
        string dataRoot,
        Func<string?>? key = null,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? clock = null
    )
    {
        _dataRoot = dataRoot;
        _key = key ?? (() => SecretSource.Default.Read(KeySecret));
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
            Timeout = TimeSpan.FromSeconds(20),
            MaxResponseContentBufferSize = MaxReplyBytes,
        };
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private Settings Read()
    {
        var file = Path.Combine(_dataRoot, "site-analytics.json");
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), CompanyJson.Options) ?? new(null)
                : new(null);
        }
        catch (JsonException)
        {
            return new(null);
        }
    }

    // The endpoint is https, or http only on this computer; it carries no sign-in, query or fragment of its own.
    private static Uri? Endpoint(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
            ? uri
            : null;

    public IntegrationSetup Setup { get; } =
        new(
            "site-analytics",
            "site-analytics.json",
            [
                new(
                    "mcpUrl",
                    "Your site's MCP address",
                    "The address your site's admin shows for AI employees, such as https://www.example.com/mcp.",
                    200
                )
                {
                    Check = v => Endpoint(v) is null ? "Use the https:// address your site's admin shows." : null,
                },
            ],
            [
                new(
                    KeySecret,
                    "Agent key",
                    "A key from your site's admin. HireZero uses it only to read the counts, never to save or publish."
                ),
            ]
        )
        {
            Steps =
            [
                new(
                    "In your site's admin, open Settings → AI employee and create a key named HireZero. Copy it: it is shown once."
                ),
                new("Enter your site's MCP address and paste the key below."),
            ],
        };

    public IntegrationStatus Status()
    {
        var endpoint = Endpoint(Read().McpUrl);
        var (active, reason) =
            endpoint is null
                ? (false, "Enter your site's MCP address in Company settings → Integrations → Site analytics.")
            : string.IsNullOrWhiteSpace(_key())
                ? (
                    false,
                    "Paste an agent key from your site's admin in Company settings → Integrations → Site analytics."
                )
            : (true, "Reading daily visitors, page views and sign-ups from " + endpoint.Host + " (read-only).");
        return new("site-analytics", "Site analytics", active, reason);
    }

    public void Register(ToolRegistry registry) => registry.Register(AnalyticsTools.Daily, DailyAsync);

    private async Task<DailyReport> DailyAsync(DailyRequest request, CancellationToken token)
    {
        AnalyticsTools.Check(request);
        var today = DateOnly.FromDateTime(_clock().UtcDateTime);
        if (request.End > today)
            throw new ToolFailureException("refused", "The range ends after today.", 422);
        var endpoint =
            Endpoint(Read().McpUrl)
            ?? throw new ToolFailureException(
                "refused",
                "Enter your site's MCP address in Company settings → Integrations → Site analytics.",
                409
            );
        var back = today.DayNumber - request.Start.DayNumber + 1;
        var views = await CallAsync(endpoint, "analytics_summary", Math.Min(back, ViewDays), token);
        var signUps = await CallAsync(endpoint, "signups_daily", Math.Min(back, SignUpDays), token);
        var seen = Days(views, "series", "day", ("visitors", "views"));
        var joined = Days(signUps, "days", "date", ("signups", "accounts"));
        var rows = new List<DailyRow>();
        for (var day = request.Start; day <= request.End; day = day.AddDays(1))
        {
            var (visitors, pageViews) = seen.GetValueOrDefault(day);
            var (newSignUps, accounts) = joined.GetValueOrDefault(day);
            rows.Add(new(day, visitors, pageViews, newSignUps, accounts));
        }
        return new("Site analytics (" + endpoint.Host + ")", request.Start, request.End, [.. rows]);
    }

    // One count pair per day from a tool's list; a day the list leaves out stays unknown.
    private static Dictionary<DateOnly, (long?, long?)> Days(
        JsonElement result,
        string list,
        string dayField,
        (string First, string Second) counts
    )
    {
        var days = new Dictionary<DateOnly, (long?, long?)>();
        if (!result.TryGetProperty(list, out var items) || items.ValueKind != JsonValueKind.Array)
            throw new ToolFailureException("unavailable", "The site's reply had no " + list + ".");
        foreach (var item in items.EnumerateArray())
            if (
                item.TryGetProperty(dayField, out var day)
                && DateOnly.TryParseExact(day.GetString(), "yyyy-MM-dd", out var date)
            )
                days[date] = (Count(item, counts.First), Count(item, counts.Second));
        return days;
    }

    private static long? Count(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) && count >= 0 ? count : null;

    // One MCP tools/call over Streamable HTTP with a JSON reply. The site's tool results are untrusted data: only the
    // counts are kept.
    private async Task<JsonElement> CallAsync(Uri endpoint, string tool, int days, CancellationToken token)
    {
        var key = _key();
        if (string.IsNullOrWhiteSpace(key))
            throw new ToolFailureException(
                "refused",
                "Paste an agent key from your site's admin in Company settings → Integrations → Site analytics.",
                409
            );
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref _requests),
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = tool,
                ["arguments"] = new JsonObject { ["days"] = days },
            },
        };
        try
        {
            // A sized body: a site's simple server may read exactly Content-Length bytes and see a streamed one as empty.
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _http.SendAsync(request, token);
            if ((int)response.StatusCode is 401 or 403)
                throw new ToolFailureException(
                    "refused",
                    "Your site refused the agent key. Create a new one in your site's admin and paste it again.",
                    409
                );
            if (!response.IsSuccessStatusCode)
                throw new ToolFailureException(
                    "unavailable",
                    "Your site could not be read (HTTP " + (int)response.StatusCode + ")."
                );
            using var reply = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (reply.RootElement.TryGetProperty("error", out _))
                throw new ToolFailureException("unavailable", "Your site could not run " + tool + ".");
            var result = reply.RootElement.GetProperty("result");
            if (result.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True)
                throw new ToolFailureException("unavailable", "Your site could not run " + tool + ".");
            return result.GetProperty("structuredContent").Clone();
        }
        catch (Exception error)
            when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                || error is TaskCanceledException && !token.IsCancellationRequested
            )
        {
            throw new ToolFailureException("unavailable", "Your site could not be read: " + error.Message);
        }
    }
}
