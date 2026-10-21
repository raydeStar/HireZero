using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireZero.Tools.Analytics;

namespace HireZero.Integrations.Google;

// The analytics.* tools from Google (docs/adr/0029-marketing-analytics-reads.md): traffic from the GA4 Data API and
// search performance from the Search Console API, read as the owner's own Google sign-in (docs/adr/0049) or a service
// account. Off until <data root>/google.json names a GA4 property or a Search Console site and one of the two is set.
// Read-only; requests go to Google's API hosts only, with no redirects.
public sealed class GoogleAnalyticsProvider : IToolProvider, IIntegrationStatusSource, IConfigurableIntegration
{
    public const string KeySecret = "HIREZERO_GOOGLE_SERVICE_ACCOUNT";
    private const string DataApi = "https://analyticsdata.googleapis.com/v1beta/";
    private const string SearchApi = "https://searchconsole.googleapis.com/webmasters/v3/sites/";
    private const string AdminApi = "https://analyticsadmin.googleapis.com/v1beta/accountSummaries?pageSize=200";

    private sealed record Settings(string? Ga4Property, string? SearchConsoleSite, string? OAuthClientId = null);

    // A GA4 property or Search Console site the signed-in account can read, for the owner to pick.
    public sealed record Choice(string Value, string Label);

    private readonly string _dataRoot;
    private readonly Func<string?> _key;
    private readonly HttpClient _http;
    private readonly GoogleServiceAccount _account;

    // The owner's own Google sign-in, preferred over a service account when present (docs/adr/0049).
    public GoogleOAuth OAuth { get; }

    public GoogleAnalyticsProvider(
        string dataRoot,
        Func<string?>? key = null,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? clock = null,
        Func<string, string?>? secrets = null
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
        OAuth = new GoogleOAuth(secrets ?? SecretSource.Default.Read, () => Read().OAuthClientId, _http, clock);
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

    // In-app setup (docs/adr/0038-integration-setup-in-the-app.md): the same google.json and secret name.
    public IntegrationSetup Setup { get; } =
        new(
            "google-analytics",
            "google.json",
            [
                new(
                    "oauthClientId",
                    "OAuth client ID",
                    "Your Google Cloud OAuth client (Desktop app) ID, ending in .apps.googleusercontent.com.",
                    200
                )
                {
                    Check = v =>
                        v.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
                            ? null
                            : "An OAuth client ID ends in .apps.googleusercontent.com.",
                },
                new("ga4Property", "GA4 property", "The property's number, such as 123456789.", 40)
                {
                    Check = v => Property(new(v, null)) is null ? "A GA4 property is its number." : null,
                },
                new(
                    "searchConsoleSite",
                    "Search Console site",
                    "A URL-prefix property (https://www.example.com/) or a domain property (sc-domain:example.com).",
                    200
                )
                {
                    Check = v =>
                        Site(new(null, v)) is null ? "Use https://…/ or sc-domain:… for a Search Console site." : null,
                },
            ],
            [
                new(GoogleOAuth.ClientSecretName, "OAuth client secret", "The client secret of that OAuth client."),
                new(
                    KeySecret,
                    "Service account key (instead of signing in)",
                    "Optional: the JSON key of a service account with read access to the property and site."
                )
                {
                    Multiline = true,
                },
            ]
        )
        {
            Steps =
            [
                new(
                    "Once: in Google Cloud, pick or create a project, and enable the Google Analytics Data API, the Google Analytics Admin API and the Google Search Console API.",
                    "https://console.cloud.google.com/apis/library/analyticsadmin.googleapis.com",
                    "Analytics Admin API"
                ),
                new(
                    "Set up the OAuth consent screen as External and publish it (In production). Unverified is fine for your own account; test mode sign-ins expire after 7 days.",
                    "https://console.cloud.google.com/apis/credentials/consent",
                    "Consent screen"
                ),
                new(
                    "Create credentials → OAuth client ID → Desktop app. Enter its client ID and client secret below.",
                    "https://console.cloud.google.com/apis/credentials",
                    "Credentials"
                ),
                new("Sign in with Google, then pick your GA4 property and Search Console site."),
            ],
        };

    public IntegrationStatus Status()
    {
        var settings = Read();
        string reason;
        var active = false;
        try
        {
            // The service account's email is not secret; it is what the owner shares the property and site with.
            var key = OAuth.SignedIn ? null : GoogleServiceAccount.Parse(_key());
            if (!OAuth.SignedIn && key is null)
                reason = OAuth.Configured
                    ? "Sign in with Google in Company settings → Integrations → Google Analytics and Search Console."
                    : "Add your Google OAuth client and sign in, or paste a service account key, in Company settings → Integrations → Google Analytics and Search Console.";
            else if (Property(settings) is null && Site(settings) is null)
                reason =
                    "Pick a GA4 property or a Search Console site in Company settings → Integrations → Google Analytics and Search Console"
                    + (key is null ? "." : ", and share them with " + key.ClientEmail + ".");
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
                    + (
                        key is null
                            ? " with your Google sign-in (read-only)."
                            : " as " + key.ClientEmail + " (read-only). Each must be shared with that email."
                    );
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
        AnalyticsTools.Check(request);
        var property =
            Property(Read())
            ?? throw new ToolFailureException(
                "refused",
                "Add a GA4 property in Company settings → Integrations → Google Analytics and Search Console.",
                409
            );
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
        AnalyticsTools.Check(request);
        var site =
            Site(Read())
            ?? throw new ToolFailureException(
                "refused",
                "Add a Search Console site in Company settings → Integrations → Google Analytics and Search Console.",
                409
            );
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
            request.Headers.Authorization = await AuthorizeAsync(token);
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

    private Task<System.Net.Http.Headers.AuthenticationHeaderValue> AuthorizeAsync(CancellationToken token) =>
        OAuth.SignedIn ? OAuth.AuthorizeAsync(token) : _account.AuthorizeAsync(token);

    // The GA4 properties and Search Console sites this sign-in can read, for the owner to pick (docs/adr/0049). A list
    // that cannot be read (an API not enabled, say) comes back empty with the reason.
    public async Task<(Choice[] Properties, Choice[] Sites, string[] Problems)> ChoicesAsync(CancellationToken token)
    {
        var problems = new List<string>();
        async Task<JsonDocument?> Get(string url, string what)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = await AuthorizeAsync(token);
                using var response = await _http.SendAsync(request, token);
                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                problems.Add(what + " could not be listed (HTTP " + (int)response.StatusCode + ").");
            }
            catch (Exception error) when (error is HttpRequestException or InvalidOperationException or JsonException)
            {
                problems.Add(what + " could not be listed: " + error.Message);
            }
            return null;
        }
        var properties = new List<Choice>();
        using (var admin = await Get(AdminApi, "GA4 properties"))
            if (admin?.RootElement.TryGetProperty("accountSummaries", out var accounts) == true)
                foreach (var account in accounts.EnumerateArray())
                    if (account.TryGetProperty("propertySummaries", out var summaries))
                        foreach (var property in summaries.EnumerateArray())
                            properties.Add(
                                new(
                                    property.GetProperty("property").GetString()!.Replace("properties/", ""),
                                    property.TryGetProperty("displayName", out var name) ? name.GetString()! : "GA4"
                                )
                            );
        var sites = new List<Choice>();
        using (var search = await Get(SearchApi.TrimEnd('/'), "Search Console sites"))
            if (search?.RootElement.TryGetProperty("siteEntry", out var entries) == true)
                foreach (var entry in entries.EnumerateArray())
                    if (entry.GetProperty("permissionLevel").GetString() != "siteUnverifiedUser")
                        sites.Add(
                            new(entry.GetProperty("siteUrl").GetString()!, entry.GetProperty("siteUrl").GetString()!)
                        );
        return ([.. properties], [.. sites], [.. problems]);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
