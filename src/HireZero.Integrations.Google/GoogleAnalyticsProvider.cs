using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireZero.Tools.Analytics;

namespace HireZero.Integrations.Google;

// The analytics.* tools from Google (docs/adr/0029-marketing-analytics-reads.md): traffic from the GA4 Data API and
// search performance from the Search Console API, read with a service account. Off until <data root>/google.json names
// a GA4 property or a Search Console site and HIREZERO_GOOGLE_SERVICE_ACCOUNT holds the key. Read-only; requests go to
// Google's API hosts only, with no redirects.
public sealed class GoogleAnalyticsProvider : IToolProvider, IIntegrationStatusSource
{
    public const string KeySecret = "HIREZERO_GOOGLE_SERVICE_ACCOUNT";
    private const string DataApi = "https://analyticsdata.googleapis.com/v1beta/";
    private const string SearchApi = "https://searchconsole.googleapis.com/webmasters/v3/sites/";

    private sealed record Settings(string? Ga4Property, string? SearchConsoleSite);

    private readonly string _dataRoot;
    private readonly Func<string?> _key;
    private readonly HttpClient _http;
    private readonly GoogleServiceAccount _account;

    public GoogleAnalyticsProvider(
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
        };
        _account = new GoogleServiceAccount(_key, _http, clock);
    }

    private Settings Read()
    {
        var file = Path.Combine(_dataRoot, "google.json");
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), CompanyJson.Options) ?? new(null, null)
                : new(null, null);
        }
        catch (JsonException)
        {
            return new(null, null);
        }
    }

    // A GA4 property is its number, written with or without "properties/".
    private static string? Property(Settings settings) =>
        settings.Ga4Property?.Trim().Replace("properties/", "", StringComparison.Ordinal) is { Length: > 0 } id
        && id.All(char.IsAsciiDigit)
            ? id
            : null;

    // A Search Console site is a URL-prefix property (https://…/) or a domain property (sc-domain:…).
    private static string? Site(Settings settings) =>
        settings.SearchConsoleSite?.Trim() is { Length: > 0 } site
        && (
            site.StartsWith("sc-domain:", StringComparison.Ordinal)
            || Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
        )
            ? site
            : null;

    public IntegrationStatus Status()
    {
        var settings = Read();
        string reason;
        var active = false;
        try
        {
            if (GoogleServiceAccount.Parse(_key()) is null)
                reason = "Set " + KeySecret + " to a service account key (see docs/SECRETS.md).";
            else if (Property(settings) is null && Site(settings) is null)
                reason =
                    "Name a GA4 property (ga4Property) or a Search Console site (searchConsoleSite) in google.json.";
            else
            {
                active = true;
                reason =
                    "Reading "
                    + string.Join(
                        " and ",
                        new[]
                        {
                            Property(settings) is { } p ? "GA4 property " + p : null,
                            Site(settings) is { } s ? "Search Console " + s : null,
                        }.OfType<string>()
                    )
                    + " with a read-only service account.";
            }
        }
        catch (InvalidOperationException error)
        {
            reason = error.Message;
        }
        return new("google-analytics", "Google Analytics and Search Console", active, reason);
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(AnalyticsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AnalyticsTools.Traffic, TrafficAsync);
        registry.Register(AnalyticsTools.Search, SearchAsync);
    }

    private static readonly Dictionary<string, string> TrafficDimensions = new()
    {
        ["date"] = "date",
        ["channel"] = "sessionDefaultChannelGroup",
        ["landing-page"] = "landingPage",
        ["device"] = "deviceCategory",
        ["country"] = "country",
        ["source"] = "sessionSource",
    };

    private static readonly string[] TrafficMetrics =
    [
        "sessions",
        "totalUsers",
        "newUsers",
        "screenPageViews",
        "keyEvents",
        "engagementRate",
    ];

    private async Task<TrafficReport> TrafficAsync(TrafficRequest request, CancellationToken token)
    {
        AnalyticsRequests.Check(request);
        var property =
            Property(Read())
            ?? throw new ToolFailureException("refused", "Name a GA4 property in google.json first.", 409);
        var body = new JsonObject
        {
            ["dateRanges"] = new JsonArray(
                new JsonObject { ["startDate"] = Iso(request.Start), ["endDate"] = Iso(request.End) }
            ),
            ["metrics"] = new JsonArray(
                TrafficMetrics.Select(m => (JsonNode)new JsonObject { ["name"] = m }).ToArray()
            ),
            ["metricAggregations"] = new JsonArray("TOTAL"),
            ["limit"] = request.Limit,
        };
        if (request.Breakdown is { } breakdown)
        {
            body["dimensions"] = new JsonArray(new JsonObject { ["name"] = TrafficDimensions[breakdown] });
            body["orderBys"] = new JsonArray(
                breakdown == "date"
                    ? new JsonObject { ["dimension"] = new JsonObject { ["dimensionName"] = "date" } }
                    : new JsonObject
                    {
                        ["metric"] = new JsonObject { ["metricName"] = "sessions" },
                        ["desc"] = true,
                    }
            );
        }
        using var response = await SendAsync(DataApi + "properties/" + property + ":runReport", body, token);
        var rows = new List<TrafficRow>();
        TrafficRow? totals = null;
        if (response.RootElement.TryGetProperty("rows", out var found))
            foreach (var row in found.EnumerateArray())
            {
                var key = request.Breakdown is null
                    ? "total"
                    : row.GetProperty("dimensionValues")[0].GetProperty("value").GetString() ?? "";
                if (request.Breakdown == "date" && key.Length == 8)
                    key = key[..4] + "-" + key[4..6] + "-" + key[6..];
                rows.Add(Traffic(key, row.GetProperty("metricValues")));
            }
        if (response.RootElement.TryGetProperty("totals", out var total) && total.GetArrayLength() > 0)
            totals = Traffic("total", total[0].GetProperty("metricValues"));
        if (request.Breakdown is null)
        {
            totals ??= rows.FirstOrDefault();
            rows.Clear();
        }
        return new(
            "GA4 property " + property,
            request.Start,
            request.End,
            request.Breakdown,
            totals ?? new("total", 0, 0, 0, 0, 0, 0),
            [.. rows]
        );
    }

    private static TrafficRow Traffic(string key, JsonElement values)
    {
        double Value(int i) =>
            double.Parse(values[i].GetProperty("value").GetString() ?? "0", CultureInfo.InvariantCulture);
        return new(key, (long)Value(0), (long)Value(1), (long)Value(2), (long)Value(3), (long)Value(4), Value(5));
    }

    private async Task<SearchReport> SearchAsync(SearchRequest request, CancellationToken token)
    {
        AnalyticsRequests.Check(request);
        var site =
            Site(Read())
            ?? throw new ToolFailureException("refused", "Name a Search Console site in google.json first.", 409);
        var url = SearchApi + Uri.EscapeDataString(site) + "/searchAnalytics/query";
        JsonObject Body(bool rows)
        {
            var body = new JsonObject
            {
                ["startDate"] = Iso(request.Start),
                ["endDate"] = Iso(request.End),
                ["dataState"] = "final",
            };
            if (rows)
            {
                body["dimensions"] = new JsonArray(request.Breakdown);
                body["rowLimit"] = request.Limit;
            }
            return body;
        }
        using var totalsResponse = await SendAsync(url, Body(rows: false), token);
        using var rowsResponse = await SendAsync(url, Body(rows: true), token);
        static SearchRow[] Rows(JsonDocument document, bool keyed) =>
            document.RootElement.TryGetProperty("rows", out var rows)
                ?
                [
                    .. rows.EnumerateArray()
                        .Select(r => new SearchRow(
                            keyed ? r.GetProperty("keys")[0].GetString() ?? "" : "total",
                            (long)r.GetProperty("clicks").GetDouble(),
                            (long)r.GetProperty("impressions").GetDouble(),
                            r.GetProperty("ctr").GetDouble(),
                            r.GetProperty("position").GetDouble()
                        )),
                ]
                : [];
        var rowsFound = Rows(rowsResponse, keyed: true);
        if (request.Breakdown == "date")
            rowsFound = [.. rowsFound.OrderBy(r => r.Key, StringComparer.Ordinal)];
        return new(
            "Search Console " + site,
            request.Start,
            request.End,
            request.Breakdown,
            Rows(totalsResponse, keyed: false).FirstOrDefault() ?? new("total", 0, 0, 0, 0),
            rowsFound
        );
    }

    private async Task<JsonDocument> SendAsync(string url, JsonObject body, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = await _account.AuthorizeAsync(token);
            using var response = await _http.SendAsync(request, token);
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                var status = "";
                try
                {
                    using var error = JsonDocument.Parse(text);
                    if (error.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("status", out var s))
                        status = ", " + s.GetString();
                }
                catch (JsonException) { }
                throw new ToolFailureException(
                    "unavailable",
                    "Google could not be read (HTTP " + (int)response.StatusCode + status + ")."
                );
            }
            return JsonDocument.Parse(text);
        }
        catch (Exception error)
            when (error is HttpRequestException or JsonException or InvalidOperationException
                || error is TaskCanceledException && !token.IsCancellationRequested
            )
        {
            throw new ToolFailureException("unavailable", "Google could not be read: " + error.Message);
        }
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// The same request rules for every analytics provider.
public static class AnalyticsRequests
{
    private static void Range(DateOnly start, DateOnly end, int limit)
    {
        if (end < start)
            throw new ToolFailureException("refused", "The range ends before it starts.", 422);
        if (end.DayNumber - start.DayNumber + 1 > AnalyticsTools.MaxDays)
            throw new ToolFailureException("refused", "Read at most " + AnalyticsTools.MaxDays + " days at once.", 422);
        if (limit is < 1 or > 100)
            throw new ToolFailureException("refused", "Read between 1 and 100 rows.", 422);
    }

    public static void Check(TrafficRequest request)
    {
        Range(request.Start, request.End, request.Limit);
        if (request.Breakdown is { } b && !AnalyticsTools.TrafficBreakdowns.Contains(b))
            throw new ToolFailureException("refused", "Unknown traffic breakdown: " + b, 422);
    }

    public static void Check(SearchRequest request)
    {
        Range(request.Start, request.End, request.Limit);
        if (!AnalyticsTools.SearchBreakdowns.Contains(request.Breakdown))
            throw new ToolFailureException("refused", "Unknown search breakdown: " + request.Breakdown, 422);
    }
}
