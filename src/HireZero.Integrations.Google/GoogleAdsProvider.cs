using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireZero.Tools.Advertising;

namespace HireZero.Integrations.Google;

// The ads.* tools from Google Ads (docs/adr/0032-google-ads-provider.md). Off until <data root>/google-ads.json names
// the customer account and OAuth client, and the developer token, client secret and refresh token are set as secrets.
// It only ever sends read queries (GAQL SELECT) that it composes itself, to the Google Ads search endpoint; the Google
// Ads scope is not read-only, so authorize it as a user with read-only access to the account.
public sealed partial class GoogleAdsProvider : IToolProvider, IIntegrationStatusSource
{
    public const string DeveloperTokenSecret = "HIREZERO_GOOGLE_ADS_DEVELOPER_TOKEN";
    public const string ClientSecretSecret = "HIREZERO_GOOGLE_ADS_CLIENT_SECRET";
    public const string RefreshTokenSecret = "HIREZERO_GOOGLE_ADS_REFRESH_TOKEN";

    // A second, opt-in sign-in for changes (docs/adr/0033-approved-ad-changes.md): a user who may edit the account. Reads
    // keep the read-only user, and without this token no change can be made at all.
    public const string ChangeRefreshTokenSecret = "HIREZERO_GOOGLE_ADS_CHANGE_REFRESH_TOKEN";

    // Google retires API versions about a year after release; google-ads.json may name a newer one without a rebuild.
    public const string DefaultApiVersion = "v22";

    private sealed record Settings(string? CustomerId, string? LoginCustomerId, string? ClientId, string? ApiVersion);

    private readonly string _dataRoot;
    private readonly Func<string, string?> _secret;
    private readonly HttpClient _http;
    private readonly GoogleOAuthRefresh _oauth;
    private readonly GoogleOAuthRefresh _changeOAuth;

    public GoogleAdsProvider(
        string dataRoot,
        Func<string, string?>? secret = null,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? clock = null
    )
    {
        _dataRoot = dataRoot;
        _secret = secret ?? SecretSource.Default.Read;
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
            Timeout = TimeSpan.FromSeconds(30),
        };
        _oauth = new GoogleOAuthRefresh(
            () => Read().ClientId,
            () => _secret(ClientSecretSecret),
            () => _secret(RefreshTokenSecret),
            _http,
            clock
        );
        _changeOAuth = new GoogleOAuthRefresh(
            () => Read().ClientId,
            () => _secret(ClientSecretSecret),
            () => _secret(ChangeRefreshTokenSecret),
            _http,
            clock
        );
    }

    public bool ChangesEnabled => Status().Active && !string.IsNullOrEmpty(_secret(ChangeRefreshTokenSecret));

    private Settings Read()
    {
        var file = Path.Combine(_dataRoot, "google-ads.json");
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), CompanyJson.Options)
                    ?? new(null, null, null, null)
                : new(null, null, null, null);
        }
        catch (JsonException)
        {
            return new(null, null, null, null);
        }
    }

    // A customer id is ten digits, written with or without dashes.
    private static string? CustomerId(string? text) =>
        text?.Replace("-", "", StringComparison.Ordinal).Trim() is { } id && TenDigits().IsMatch(id) ? id : null;

    private static string Version(Settings settings) =>
        settings.ApiVersion is { } v && ApiVersion().IsMatch(v) ? v : DefaultApiVersion;

    public IntegrationStatus Status()
    {
        var settings = Read();
        var missing = new[]
        {
            (DeveloperTokenSecret, _secret(DeveloperTokenSecret)),
            (ClientSecretSecret, _secret(ClientSecretSecret)),
            (RefreshTokenSecret, _secret(RefreshTokenSecret)),
        }
            .Where(s => string.IsNullOrEmpty(s.Item2))
            .Select(s => s.Item1)
            .ToArray();
        var (active, reason) =
            CustomerId(settings.CustomerId) is null ? (false, "Name the ten-digit customerId in google-ads.json.")
            : string.IsNullOrWhiteSpace(settings.ClientId) ? (false, "Name the OAuth clientId in google-ads.json.")
            : settings.LoginCustomerId is { Length: > 0 } login && CustomerId(login) is null
                ? (false, "The loginCustomerId in google-ads.json is not a ten-digit id.")
            : settings.ApiVersion is { } v && !ApiVersion().IsMatch(v)
                ? (false, "The apiVersion in google-ads.json is not like v22.")
            : missing.Length > 0
                ? (false, "Set the secrets " + string.Join(", ", missing) + " (see docs/ANALYTICS.md).")
            : (
                true,
                $"Reading Google Ads account {Dashed(CustomerId(settings.CustomerId)!)} through API {Version(settings)}; "
                    + (
                        string.IsNullOrEmpty(_secret(ChangeRefreshTokenSecret))
                            ? "read only, changes are off."
                            : "changes only as owner-approved publications."
                    )
            );
        return new("google-ads", "Google Ads", active, reason);
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(AdsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AdsTools.Performance, PerformanceAsync);
        registry.Register(AdsTools.Campaign, CampaignAsync);
        if (ChangesEnabled)
            registry.Register(AdsTools.Change, ChangeAsync);
    }

    private async Task<AdsCampaign> CampaignAsync(AdsCampaignRequest request, CancellationToken token)
    {
        if (!request.CampaignId.All(char.IsAsciiDigit) || request.CampaignId.Length is 0 or > 20)
            throw new ToolFailureException("refused", "A campaign id is digits.", 422);
        if (!Status().Active)
            throw new ToolFailureException("refused", "Google Ads is not active: " + Status().Reason, 409);
        var settings = Read();
        var customer = CustomerId(settings.CustomerId)!;
        using var found = await SearchAsync(
            settings,
            customer,
            "SELECT campaign.id, campaign.name, campaign.status, campaign_budget.id, campaign_budget.amount_micros, "
                + $"campaign_budget.explicitly_shared, customer.currency_code FROM campaign WHERE campaign.id = {request.CampaignId}",
            token
        );
        var result = Results(found).FirstOrDefault();
        if (result.ValueKind != JsonValueKind.Object)
            throw new ToolFailureException("refused", "No campaign " + request.CampaignId + " in this account.", 404);
        var campaign = result.GetProperty("campaign");
        var budget = result.TryGetProperty("campaignBudget", out var b) ? b : default;
        var micros =
            budget.ValueKind == JsonValueKind.Object && budget.TryGetProperty("amountMicros", out var m)
                ? decimal.Parse(m.ToString(), CultureInfo.InvariantCulture)
                : (decimal?)null;
        return new(
            Text(campaign, "id") ?? request.CampaignId,
            Text(campaign, "name") ?? "",
            Text(campaign, "status")?.ToLowerInvariant() ?? "unknown",
            budget.ValueKind == JsonValueKind.Object ? Text(budget, "id") : null,
            micros / 1_000_000m,
            budget.ValueKind == JsonValueKind.Object
                && budget.TryGetProperty("explicitlyShared", out var shared)
                && shared.ValueKind == JsonValueKind.True,
            result.TryGetProperty("customer", out var c) ? Text(c, "currencyCode") ?? "XXX" : "XXX"
        );
    }

    // One mutate call, signed in as the change user. The publication has already re-read the campaign; nothing here
    // retries, so a failed or unanswered call is left for the owner to reconcile by reading.
    private async Task<AdsChangeResult> ChangeAsync(AdsChange change, CancellationToken token)
    {
        AdsTools.Check(change);
        if (!ChangesEnabled)
            throw new ToolFailureException("refused", "Google Ads changes are off.", 409);
        var settings = Read();
        var customer = CustomerId(settings.CustomerId)!;
        var (resource, body) =
            change.Kind == AdsTools.Pause
                ? (
                    "campaigns",
                    (object)
                        new
                        {
                            operations = new[]
                            {
                                new
                                {
                                    update = new Dictionary<string, string>
                                    {
                                        ["resourceName"] = $"customers/{customer}/campaigns/{change.CampaignId}",
                                        ["status"] = "PAUSED",
                                    },
                                    updateMask = "status",
                                },
                            },
                        }
                )
                : (
                    "campaignBudgets",
                    new
                    {
                        operations = new[]
                        {
                            new
                            {
                                update = new Dictionary<string, string>
                                {
                                    ["resourceName"] = $"customers/{customer}/campaignBudgets/{change.BudgetId}",
                                    ["amountMicros"] = Micros(change.NewDailyBudget!.Value),
                                },
                                updateMask = "amount_micros",
                            },
                        },
                    }
                );
        using var response = await PostAsync(
            settings,
            $"https://googleads.googleapis.com/{Version(settings)}/customers/{customer}/{resource}:mutate",
            body,
            _changeOAuth,
            "changed",
            token
        );
        var reference = Results(response).FirstOrDefault() is { ValueKind: JsonValueKind.Object } first
            ? Text(first, "resourceName")
            : null;
        return new(
            reference ?? throw new ToolFailureException("unavailable", "Google Ads did not confirm the change.")
        );
    }

    // Whole cents, as Google Ads requires for budget amounts.
    private static string Micros(decimal amount) =>
        ((long)(Math.Round(amount, 2, MidpointRounding.AwayFromZero) * 1_000_000m)).ToString(
            CultureInfo.InvariantCulture
        );

    private const string Metrics =
        "metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value";

    private async Task<AdsReport> PerformanceAsync(AdsRequest request, CancellationToken token)
    {
        AdsTools.Check(request);
        if (!Status().Active)
            throw new ToolFailureException("refused", "Google Ads is not active: " + Status().Reason, 409);
        var settings = Read();
        var customer = CustomerId(settings.CustomerId)!;
        var range = $"segments.date BETWEEN '{Iso(request.Start)}' AND '{Iso(request.End)}'";

        using var totals = await SearchAsync(
            settings,
            customer,
            $"SELECT customer.currency_code, {Metrics} FROM customer WHERE {range}",
            token
        );
        var currency = "XXX";
        var total = new AdsRow("total", null, null, 0, 0, 0, 0, 0);
        foreach (var result in Results(totals))
        {
            if (result.TryGetProperty("customer", out var c) && c.TryGetProperty("currencyCode", out var code))
                currency = code.GetString() ?? currency;
            total = Add(total, Row("total", null, null, result));
        }

        var rows = new List<AdsRow>();
        if (request.Breakdown == "campaign")
        {
            using var campaigns = await SearchAsync(
                settings,
                customer,
                $"SELECT campaign.id, campaign.name, campaign.status, {Metrics} FROM campaign WHERE {range} ORDER BY metrics.cost_micros DESC LIMIT {request.Limit}",
                token
            );
            foreach (var result in Results(campaigns))
            {
                var campaign = result.GetProperty("campaign");
                rows.Add(
                    Row(
                        Text(campaign, "id") ?? "",
                        Text(campaign, "name"),
                        Text(campaign, "status")?.ToLowerInvariant() is { } status && AdsTools.Statuses.Contains(status)
                            ? status
                            : null,
                        result
                    )
                );
            }
        }
        else
        {
            using var days = await SearchAsync(
                settings,
                customer,
                $"SELECT segments.date, {Metrics} FROM customer WHERE {range} ORDER BY segments.date LIMIT {request.Limit}",
                token
            );
            foreach (var result in Results(days))
                rows.Add(Row(Text(result.GetProperty("segments"), "date") ?? "", null, null, result));
        }
        return new(
            "Google Ads account " + Dashed(customer),
            currency,
            request.Start,
            request.End,
            request.Breakdown,
            total,
            [.. rows]
        );
    }

    private static IEnumerable<JsonElement> Results(JsonDocument document) =>
        document.RootElement.TryGetProperty("results", out var results) ? results.EnumerateArray() : [];

    // Google Ads returns 64-bit numbers as strings and money in micros (millionths of the currency unit).
    private static AdsRow Row(string key, string? name, string? status, JsonElement result)
    {
        var m = result.TryGetProperty("metrics", out var metrics) ? metrics : default;
        double Number(string field) =>
            m.ValueKind == JsonValueKind.Object && m.TryGetProperty(field, out var v)
                ? v.ValueKind == JsonValueKind.String
                    ? double.Parse(v.GetString()!, CultureInfo.InvariantCulture)
                    : v.GetDouble()
                : 0;
        return new(
            key,
            name,
            status,
            (long)Number("impressions"),
            (long)Number("clicks"),
            (decimal)Number("costMicros") / 1_000_000m,
            Number("conversions"),
            (decimal)Number("conversionsValue")
        );
    }

    private static AdsRow Add(AdsRow a, AdsRow b) =>
        a with
        {
            Impressions = a.Impressions + b.Impressions,
            Clicks = a.Clicks + b.Clicks,
            Spend = a.Spend + b.Spend,
            Conversions = a.Conversions + b.Conversions,
            ConversionValue = a.ConversionValue + b.ConversionValue,
        };

    private static string? Text(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) ? value.ToString() : null;

    private async Task<JsonDocument> SearchAsync(
        Settings settings,
        string customer,
        string query,
        CancellationToken token
    ) =>
        await PostAsync(
            settings,
            $"https://googleads.googleapis.com/{Version(settings)}/customers/{customer}/googleAds:search",
            new { query },
            _oauth,
            "read",
            token
        );

    private async Task<JsonDocument> PostAsync(
        Settings settings,
        string url,
        object body,
        GoogleOAuthRefresh oauth,
        string verb,
        CancellationToken token
    )
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = await oauth.AuthorizeAsync(token);
            request.Headers.Add("developer-token", _secret(DeveloperTokenSecret));
            if (CustomerId(settings.LoginCustomerId) is { } login)
                request.Headers.Add("login-customer-id", login);
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
                    "Google Ads could not be " + verb + " (HTTP " + (int)response.StatusCode + status + ")."
                );
            }
            return JsonDocument.Parse(text);
        }
        catch (Exception error)
            when (error is HttpRequestException or JsonException or InvalidOperationException
                || error is TaskCanceledException && !token.IsCancellationRequested
            )
        {
            throw new ToolFailureException("unavailable", "Google Ads could not be " + verb + ": " + error.Message);
        }
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Dashed(string id) => id[..3] + "-" + id[3..6] + "-" + id[6..];

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex TenDigits();

    [GeneratedRegex("^v[0-9]{1,3}$")]
    private static partial Regex ApiVersion();
}
