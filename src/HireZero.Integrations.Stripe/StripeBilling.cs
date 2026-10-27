using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HireZero.Integrations.Stripe;

public sealed record StripeBillingOptions(string AccountId, bool LiveMode, Uri ReturnUrl, string PortalConfigurationId);

// Platform integration only. The caller must persist exact owner authority and a once-only dispatch before POST.
public sealed partial class StripeBilling : IBillingProvider, IDisposable
{
    public const string ApiVersion = "2026-09-30.endive";
    public const int MaximumResponseBytes = 128 * 1024;
    private readonly StripeBillingOptions _options;
    private readonly Func<string?> _key;
    private readonly HttpClient _client;
    public string Identity { get; }

    public StripeBilling(StripeBillingOptions options, Func<string?> key, HttpMessageHandler? fixtureHandler = null)
    {
        Identifier(options.AccountId, "acct_");
        Identifier(options.PortalConfigurationId, "bpc_");
        if (
            !options.ReturnUrl.IsAbsoluteUri
            || options.ReturnUrl.Scheme != "https"
            || !options.ReturnUrl.IsDefaultPort
            || options.ReturnUrl.UserInfo.Length != 0
            || options.ReturnUrl.Fragment.Length != 0
            || options.ReturnUrl.AbsoluteUri.Length > 2000
        )
            throw new ArgumentException("Use a fixed HTTPS billing return URL without credentials or a fragment.");
        _options = options;
        _key = key;
        _client = new HttpClient(
            fixtureHandler
                ?? new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.None,
                }
        )
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        Identity = CompanyJson.Digest(
            new
            {
                Provider = "stripe",
                options.AccountId,
                options.LiveMode,
                ApiVersion,
            }
        );
    }

    public async Task VerifyAccountAsync(CancellationToken token) =>
        _ = await Send(
            HttpMethod.Get,
            "account",
            null,
            null,
            root =>
            {
                Require(Text(root, "id") == _options.AccountId && Text(root, "object") == "account");
                return true;
            },
            token
        );

    public Task<BillingPrice> PriceAsync(string id, CancellationToken token)
    {
        Identifier(id, "price_");
        return Send(
            HttpMethod.Get,
            "prices/" + id,
            null,
            null,
            root =>
            {
                Require(Text(root, "id") == id);
                return Price(root);
            },
            token
        );
    }

    public async Task<BillingCheckoutReceipt> CreateCheckoutAsync(
        BillingCheckoutRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        Operation(request.OperationId);
        Metadata(request.CompanyId, 120);
        Digest(request.PlanDigest);
        Identifier(request.Price.Id, "price_");
        if (request.CustomerId is not null)
            Identifier(request.CustomerId, "cus_");
        Authorize(stillAuthorized, token);
        await VerifyAccountAsync(token);
        var price = await PriceAsync(request.Price.Id, token);
        if (price != request.Price || !price.Active)
            throw new BillingProviderException("price-changed");
        var fields = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = price.Id,
            ["line_items[0][quantity]"] = "1",
            ["client_reference_id"] = request.OperationId,
            ["success_url"] = ReturnUrl(request.CompanyId),
            ["cancel_url"] = ReturnUrl(request.CompanyId),
            ["payment_method_collection"] = "always",
            ["allow_promotion_codes"] = "false",
            ["expand[0]"] = "line_items.data.price",
        };
        if (request.CustomerId is not null)
            fields["customer"] = request.CustomerId;
        foreach (var prefix in new[] { "metadata", "subscription_data[metadata]" })
        {
            fields[prefix + "[hirezero_company]"] = request.CompanyId;
            fields[prefix + "[hirezero_operation]"] = request.OperationId;
            fields[prefix + "[hirezero_plan]"] = request.PlanDigest;
        }
        Authorize(stillAuthorized, token);
        return await Send(
            HttpMethod.Post,
            "checkout/sessions",
            fields,
            "checkout:" + request.OperationId,
            root =>
            {
                var receipt = Checkout(root);
                Require(
                    receipt.OperationId == request.OperationId
                        && receipt.CompanyId == request.CompanyId
                        && receipt.PlanDigest == request.PlanDigest
                        && receipt.PriceId == price.Id
                        && (request.CustomerId is null || receipt.CustomerId == request.CustomerId)
                );
                return receipt;
            },
            token
        );
    }

    public Task<BillingCheckoutReceipt> CheckoutAsync(string id, CancellationToken token)
    {
        Identifier(id, "cs_");
        return Send(
            HttpMethod.Get,
            "checkout/sessions/" + id + "?expand%5B0%5D=line_items.data.price",
            null,
            null,
            root =>
            {
                Require(Text(root, "id") == id);
                return Checkout(root);
            },
            token
        );
    }

    public Task<BillingSubscriptionReceipt> SubscriptionAsync(string id, CancellationToken token)
    {
        Identifier(id, "sub_");
        return Send(
            HttpMethod.Get,
            "subscriptions/" + id,
            null,
            null,
            root =>
            {
                Object(root, "subscription");
                Require(Text(root, "id") == id);
                var item = Single(root.GetProperty("items"));
                Require(item.GetProperty("quantity").GetInt64() == 1);
                var start = Time(item, "current_period_start");
                var end = Time(item, "current_period_end");
                Require(end > start);
                var metadata = Binding(root);
                return new BillingSubscriptionReceipt(
                    id,
                    Reference(root, "customer", "cus_")!,
                    Choice(
                        root,
                        "status",
                        "active",
                        "trialing",
                        "past_due",
                        "unpaid",
                        "incomplete",
                        "incomplete_expired",
                        "canceled",
                        "paused"
                    ),
                    Price(item.GetProperty("price")),
                    start,
                    end,
                    root.GetProperty("cancel_at_period_end").GetBoolean(),
                    Present(root, "pause_collection"),
                    Reference(root, "latest_invoice", "in_", optional: true),
                    metadata.Company,
                    metadata.Operation,
                    metadata.Digest
                );
            },
            token
        );
    }

    public Task<BillingInvoiceReceipt> InvoiceAsync(string id, CancellationToken token)
    {
        Identifier(id, "in_");
        return Send(
            HttpMethod.Get,
            "invoices/" + id,
            null,
            null,
            root =>
            {
                Object(root, "invoice");
                Require(Text(root, "id") == id);
                var parent = root.GetProperty("parent");
                Require(Text(parent, "type") == "subscription_details");
                var due = root.GetProperty("amount_due").GetInt64();
                var paid = root.GetProperty("amount_paid").GetInt64();
                var remaining = root.GetProperty("amount_remaining").GetInt64();
                Require(due >= 0 && paid >= 0 && remaining >= 0);
                return new BillingInvoiceReceipt(
                    id,
                    Reference(root, "customer", "cus_")!,
                    Reference(parent.GetProperty("subscription_details"), "subscription", "sub_")!,
                    Choice(root, "status", "draft", "open", "paid", "uncollectible", "void"),
                    Currency(root),
                    due,
                    paid,
                    remaining
                );
            },
            token
        );
    }

    public async Task<BillingPortalReceipt> CreatePortalAsync(
        BillingPortalRequest request,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        Operation(request.OperationId);
        Identifier(request.CustomerId, "cus_");
        Authorize(stillAuthorized, token);
        await VerifyAccountAsync(token);
        Authorize(stillAuthorized, token);
        return await Send(
            HttpMethod.Post,
            "billing_portal/sessions",
            new()
            {
                ["customer"] = request.CustomerId,
                ["configuration"] = _options.PortalConfigurationId,
                ["return_url"] = ReturnUrl(request.CompanyId),
            },
            "portal:" + request.OperationId,
            root =>
            {
                Object(root, "billing_portal.session");
                Require(
                    Reference(root, "customer", "cus_") == request.CustomerId
                        && Reference(root, "configuration", "bpc_") == _options.PortalConfigurationId
                );
                return new BillingPortalReceipt(
                    Identifier(Text(root, "id"), "bps_"),
                    request.CustomerId,
                    Link(Text(root, "url"), "billing.stripe.com", "/p/session/")
                );
            },
            token
        );
    }

    private BillingCheckoutReceipt Checkout(JsonElement root)
    {
        Object(root, "checkout.session");
        Require(Text(root, "mode") == "subscription");
        var item = Single(root.GetProperty("line_items"));
        Require(item.GetProperty("quantity").GetInt64() == 1);
        var price = Price(item.GetProperty("price"));
        var metadata = Binding(root);
        Require(Text(root, "client_reference_id") == metadata.Operation);
        var status = Choice(root, "status", "open", "complete", "expired");
        var url = Present(root, "url") ? Link(Text(root, "url"), "checkout.stripe.com", "/c/pay/") : null;
        Require(status != "open" || url is not null);
        return new(
            Identifier(Text(root, "id"), "cs_"),
            metadata.Operation,
            metadata.Company,
            metadata.Digest,
            price.Id,
            status,
            Choice(root, "payment_status", "paid", "unpaid", "no_payment_required"),
            Reference(root, "customer", "cus_", true),
            Reference(root, "subscription", "sub_", true),
            url,
            Time(root, "expires_at")
        );
    }

    private string ReturnUrl(string? company)
    {
        if (company is null)
            return _options.ReturnUrl.AbsoluteUri;
        Metadata(company, 120);
        return _options.ReturnUrl.AbsoluteUri
            + (_options.ReturnUrl.Query.Length == 0 ? "?" : "&")
            + "company="
            + Uri.EscapeDataString(company)
            + "&billingReturn=1";
    }

    private BillingPrice Price(JsonElement root)
    {
        Object(root, "price");
        var recurring = root.GetProperty("recurring");
        Require(
            Text(root, "type") == "recurring"
                && Text(root, "billing_scheme") == "per_unit"
                && Text(recurring, "interval") == "month"
                && recurring.GetProperty("interval_count").GetInt32() == 1
                && Text(recurring, "usage_type") == "licensed"
                && !Present(root, "custom_unit_amount")
                && !Present(root, "transform_quantity")
                && !Present(recurring, "trial_period_days")
        );
        var amount = root.GetProperty("unit_amount").GetInt64();
        Require(amount > 0);
        return new(
            Identifier(Text(root, "id"), "price_"),
            Currency(root),
            amount,
            "month",
            1,
            root.GetProperty("active").GetBoolean()
        );
    }

    private async Task<T> Send<T>(
        HttpMethod method,
        string path,
        Dictionary<string, string>? fields,
        string? operation,
        Func<JsonElement, T> parse,
        CancellationToken token
    )
    {
        var key = _key();
        var mode = _options.LiveMode ? "live_" : "test_";
        if (
            key is null
            || !(
                key.StartsWith("sk_" + mode, StringComparison.Ordinal)
                || key.StartsWith("rk_" + mode, StringComparison.Ordinal)
            )
            || key.Length is < 16 or > 256
            || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
        )
            throw new BillingProviderException("credentials-unavailable-or-mode-mismatch");
        token.ThrowIfCancellationRequested();
        var mutation = method == HttpMethod.Post;
        using var request = new HttpRequestMessage(method, new Uri("https://api.stripe.com/v1/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("Stripe-Version", ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (operation is not null)
            request.Headers.Add("Idempotency-Key", "hirezero:" + operation);
        if (fields is not null)
            request.Content = new FormUrlEncodedContent(fields);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            // Never retry a POST, including a timeout or 5xx. Idempotency is an extra fence, not replay permission.
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token
            );
            if (!response.IsSuccessStatusCode)
                throw new BillingProviderException("provider-http-" + (int)response.StatusCode, mutation);
            Require(response.Content.Headers.ContentLength is null or <= MaximumResponseBytes);
            Require(response.Content.Headers.ContentType?.MediaType == "application/json");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var content = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                Require(content.Length + read <= MaximumResponseBytes);
                content.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(content.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            UniqueProperties(json.RootElement);
            return parse(json.RootElement);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception error)
            when (error
                    is HttpRequestException
                        or IOException
                        or OperationCanceledException
                        or JsonException
                        or InvalidOperationException
                        or FormatException
                        or KeyNotFoundException
                        or ArgumentException
                        or OverflowException
            )
        {
            throw new BillingProviderException("provider-unavailable-or-invalid-receipt", mutation);
        }
    }

    internal static void Require(bool condition)
    {
        if (!condition)
            throw new FormatException("Invalid bounded billing receipt.");
    }

    internal static bool Present(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    internal static string Text(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new FormatException("Missing billing value.");

    private void Object(JsonElement root, string kind) =>
        Require(Text(root, "object") == kind && root.GetProperty("livemode").GetBoolean() == _options.LiveMode);

    internal static string Identifier(string value, string prefix)
    {
        Require(
            value.Length > prefix.Length
                && value.Length <= 200
                && value.StartsWith(prefix, StringComparison.Ordinal)
                && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
        );
        return value;
    }

    internal static void Metadata(string value, int length) =>
        Require(
            value.Length is > 0
                && value.Length <= length
                && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        );

    internal static void Digest(string value) => Require(value.Length == 64 && value.All(Uri.IsHexDigit));

    internal static void Operation(string value) =>
        Require(Guid.TryParseExact(value, "N", out _) || Guid.TryParseExact(value, "D", out _));

    internal static DateTimeOffset Time(JsonElement root, string field) =>
        DateTimeOffset.FromUnixTimeSeconds(root.GetProperty(field).GetInt64());

    private static string Choice(JsonElement root, string name, params string[] choices)
    {
        var value = Text(root, name);
        Require(choices.Contains(value));
        return value;
    }

    private static string Currency(JsonElement root)
    {
        var currency = Text(root, "currency");
        Require(currency.Length == 3 && currency.All(c => c is >= 'a' and <= 'z'));
        return currency;
    }

    private static JsonElement Single(JsonElement list)
    {
        Require(!list.GetProperty("has_more").GetBoolean() && list.GetProperty("data").GetArrayLength() == 1);
        return list.GetProperty("data")[0];
    }

    private static string? Reference(JsonElement root, string name, string prefix, bool optional = false)
    {
        if (optional && !Present(root, name))
            return null;
        var value = root.GetProperty(name);
        return Identifier(value.ValueKind == JsonValueKind.String ? value.GetString()! : Text(value, "id"), prefix);
    }

    private static (string Company, string Operation, string Digest) Binding(JsonElement root)
    {
        var metadata = root.GetProperty("metadata");
        var company = Text(metadata, "hirezero_company");
        Metadata(company, 120);
        var operation = Text(metadata, "hirezero_operation");
        Operation(operation);
        var digest = Text(metadata, "hirezero_plan");
        Digest(digest);
        return (company, operation, digest);
    }

    private static string Link(string value, string host, string prefix)
    {
        if (
            value.Length > 4096
            || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.Host != host
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
        )
            throw new FormatException("Invalid bounded billing link.");
        return uri.AbsoluteUri;
    }

    internal static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name));
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                UniqueProperties(item);
    }

    private static void Authorize(Func<bool> stillAuthorized, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!stillAuthorized())
            throw new BillingProviderException("authority-withdrawn");
    }

    public void Dispose() => _client.Dispose();
}
