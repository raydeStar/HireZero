using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HireZero.Core;
using HireZero.Integrations.Stripe;
using Microsoft.Data.Sqlite;

public sealed class StripeBillingTests
{
    internal const string Operation = "11111111111111111111111111111111";
    internal static readonly string Digest = new('a', 64);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static BillingPrice ApprovedPrice => new("price_fictional", "usd", 1900, "month", 1, true);
    private static BillingCheckoutRequest Request =>
        new(Operation, "fictional-company", Digest, ApprovedPrice, "cus_fictional");

    internal static JsonObject Price() =>
        JsonNode
            .Parse(
                """
                {"id":"price_fictional","object":"price","livemode":false,"active":true,"currency":"usd","unit_amount":1900,
                "type":"recurring","billing_scheme":"per_unit","recurring":{"interval":"month","interval_count":1,"usage_type":"licensed","trial_period_days":null},
                "custom_unit_amount":null,"transform_quantity":null}
                """
            )!
            .AsObject();

    private static JsonObject Binding() =>
        new()
        {
            ["hirezero_company"] = "fictional-company",
            ["hirezero_operation"] = Operation,
            ["hirezero_plan"] = Digest,
        };

    private static JsonObject Checkout()
    {
        var root = JsonNode
            .Parse(
                """
                {"id":"cs_test_fictional","object":"checkout.session","livemode":false,"mode":"subscription","status":"open",
                "payment_status":"unpaid","customer":"cus_fictional","subscription":null,"expires_at":1793030400,
                "url":"https://checkout.stripe.com/c/pay/cs_test_fictional","line_items":{"has_more":false,"data":[{"quantity":1}]}}
                """
            )!
            .AsObject();
        root["metadata"] = Binding();
        root["client_reference_id"] = Operation;
        root["line_items"]!["data"]![0]!["price"] = Price();
        return root;
    }

    private static JsonObject Subscription()
    {
        var root = JsonNode
            .Parse(
                """
                {"id":"sub_fictional","object":"subscription","livemode":false,"customer":"cus_fictional","status":"active",
                "cancel_at_period_end":true,"pause_collection":null,"latest_invoice":"in_fictional",
                "items":{"has_more":false,"data":[{"quantity":1,"current_period_start":1792965600,"current_period_end":1795644000}]}}
                """
            )!
            .AsObject();
        root["items"]!["data"]![0]!["price"] = Price();
        root["metadata"] = Binding();
        return root;
    }

    private sealed record Sent(string Method, string Path, string? Version, string? Idempotency, string Body);

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<Sent> Requests = [];
        public Func<Sent, HttpResponseMessage>? Reply;
        public Action<Sent>? Observe;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.stripe.com", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var sent = new Sent(
                request.Method.Method,
                request.RequestUri.PathAndQuery,
                request.Headers.GetValues("Stripe-Version").Single(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)
            );
            Requests.Add(sent);
            Observe?.Invoke(sent);
            return Reply?.Invoke(sent) ?? Default(sent);
        }
    }

    private static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Default(Sent sent) =>
        sent.Path switch
        {
            "/v1/account" => Json(new JsonObject { ["id"] = "acct_fictional", ["object"] = "account" }),
            "/v1/prices/price_fictional" => Json(Price()),
            "/v1/checkout/sessions" => Json(Checkout()),
            _ when sent.Path.StartsWith("/v1/checkout/sessions/", StringComparison.Ordinal) => Json(Checkout()),
            "/v1/subscriptions/sub_fictional" => Json(Subscription()),
            "/v1/billing_portal/sessions" => Json(
                JsonNode.Parse(
                    """
                    {"id":"bps_fictional","object":"billing_portal.session","livemode":false,"customer":"cus_fictional",
                    "configuration":"bpc_fictional","url":"https://billing.stripe.com/p/session/fictional"}
                    """
                )!
            ),
            "/v1/invoices/in_fictional" => Json(
                JsonNode.Parse(
                    """
                    {"id":"in_fictional","object":"invoice","livemode":false,"customer":"cus_fictional","status":"paid","currency":"usd",
                    "amount_due":1900,"amount_paid":1900,"amount_remaining":0,"parent":{"type":"subscription_details","subscription_details":{"subscription":"sub_fictional"}}}
                    """
                )!
            ),
            _ => throw new InvalidOperationException("The fictional butler did not approve this provider route."),
        };

    private static StripeBilling Provider(Handler handler, string? key = null) =>
        new(
            new(
                "acct_fictional",
                false,
                new Uri("https://workspace.example.invalid/?view=departments&section=billing"),
                "bpc_fictional"
            ),
            () => key ?? "sk_test_" + "fictional",
            handler
        );

    [Fact]
    public async Task LostHttpReplyRecoversThroughTheRealAdapterAndDurableCompanyLedgerWithoutAnotherPost()
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var root = Path.GetFullPath(Path.Combine(temp, "billing-transport-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            Dictionary<string, string>? fields = null;
            var handler = new Handler
            {
                Reply = sent =>
                {
                    if (sent.Method == "POST")
                    {
                        fields = sent
                            .Body.Split('&')
                            .Select(v => v.Split('=', 2))
                            .ToDictionary(v => Uri.UnescapeDataString(v[0]), v => Uri.UnescapeDataString(v[1]));
                        throw new HttpRequestException(
                            "The fictional checkout was created, but the reply went missing."
                        );
                    }
                    if (
                        sent.Path.StartsWith("/v1/checkout/sessions/", StringComparison.Ordinal)
                        || sent.Path.StartsWith("/v1/subscriptions/", StringComparison.Ordinal)
                    )
                    {
                        Assert.NotNull(fields);
                        var checkout = sent.Path.StartsWith("/v1/checkout/", StringComparison.Ordinal);
                        var value = checkout ? Checkout() : Subscription();
                        value["metadata"]!["hirezero_operation"] = fields["client_reference_id"];
                        value["metadata"]!["hirezero_plan"] = fields["metadata[hirezero_plan]"];
                        if (checkout)
                        {
                            value["client_reference_id"] = fields["client_reference_id"];
                            value["status"] = "complete";
                            value["payment_status"] = "paid";
                            value["subscription"] = "sub_fictional";
                            value["url"] = null;
                        }
                        return Json(value);
                    }
                    return Default(sent);
                },
            };
            using var provider = Provider(handler);
            var plan = new BillingPlan("fictional", "Fictional transport test", ApprovedPrice, new(2, 28_000, 2, 60));
            CompanyStore Store() =>
                new(Path.Combine(root, "company.sqlite"), "owner@example.invalid", [], companyId: "fictional-company")
                {
                    Clock = () => DateTimeOffset.FromUnixTimeSeconds(1793000000),
                    BillingConfiguration = new(provider.Identity, [plan]),
                };
            var store = Store();
            var proposed = CompanyJson.Decode<CompanyBillingCheckout>(
                store.Apply(new(Guid.NewGuid().ToString("D"), "billing.prepare") { Scope = plan.Id })
            );
            store.Apply(
                new(Guid.NewGuid().ToString("D"), "billing.checkout")
                {
                    Id = proposed.Id,
                    ExpectedVersion = proposed.Version,
                    Digest = proposed.Digest,
                    Decision = "approve-subscription",
                }
            );
            await Assert.ThrowsAsync<BillingProviderException>(() =>
                new CompanyBillingWorkflow(store, provider).DispatchAsync(proposed.Id, Token)
            );
            var reopened = Store();
            Assert.Equal("uncertain", reopened.Billing().Checkout!.State);
            Assert.Null(reopened.Billing().Allowance);
            var recovery = new CompanyBillingWorkflow(reopened, provider);
            Assert.False(await recovery.DispatchAsync(proposed.Id, Token));
            Assert.True(await recovery.RefreshAsync(proposed.Id, Token, "cs_test_fictional"));
            Assert.Equal(plan.Allowance, Store().Billing().Allowance);
            Assert.Single(handler.Requests, r => r.Method == "POST");
            Assert.All(handler.Requests, r => Assert.Equal(StripeBilling.ApiVersion, r.Version));
            Assert.True(reopened.VerifyAudit().Intact);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (
                !root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(root).StartsWith("billing-transport-", StringComparison.Ordinal)
            )
                throw new InvalidOperationException("The fictional butler refuses an unexpected cleanup path.");
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckoutRequiresExactCurrentPriceAndCarriesTheDurableBindingToHostedStripe()
    {
        var handler = new Handler();
        using var provider = Provider(handler);
        var result = await provider.CreateCheckoutAsync(Request, () => true, Token);
        Assert.Equal("unpaid", result.PaymentStatus);
        Assert.Null(result.SubscriptionId);
        Assert.Equal(Request.PlanDigest, result.PlanDigest);
        Assert.Equal(Request.OperationId, result.OperationId);
        Assert.Equal(3, handler.Requests.Count);
        var sent = Assert.Single(handler.Requests, r => r.Method == "POST");
        Assert.Equal(StripeBilling.ApiVersion, sent.Version);
        Assert.Equal("hirezero:checkout:" + Operation, sent.Idempotency);
        var fields = sent
            .Body.Split('&')
            .Select(v => v.Split('=', 2))
            .ToDictionary(v => Uri.UnescapeDataString(v[0]), v => Uri.UnescapeDataString(v[1]));
        Assert.Equal("subscription", fields["mode"]);
        Assert.Equal("1", fields["line_items[0][quantity]"]);
        Assert.Equal("price_fictional", fields["line_items[0][price]"]);
        Assert.Equal("false", fields["allow_promotion_codes"]);
        Assert.Equal(
            "https://workspace.example.invalid/?view=departments&section=billing&company=fictional-company&billingReturn=1",
            fields["success_url"]
        );
        Assert.Equal(fields["success_url"], fields["cancel_url"]);
        Assert.Equal(Digest, fields["metadata[hirezero_plan]"]);
        Assert.Equal("fictional-company", fields["subscription_data[metadata][hirezero_company]"]);
        Assert.DoesNotContain("unit_amount", sent.Body);
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test_fictional", result.Url);
    }

    [Fact]
    public async Task ChangedPriceMerchantOrWithdrawnAuthorityPreventsAnyCheckoutPost()
    {
        foreach (var mode in new[] { "price", "merchant", "authority" })
        {
            var authorized = true;
            var handler = new Handler
            {
                Reply = sent =>
                {
                    if (mode == "merchant" && sent.Path == "/v1/account")
                        return Json(new JsonObject { ["id"] = "acct_other", ["object"] = "account" });
                    if (mode == "price" && sent.Path.Contains("/prices/", StringComparison.Ordinal))
                    {
                        var price = Price();
                        price["unit_amount"] = 2900;
                        return Json(price);
                    }
                    return Default(sent);
                },
                Observe = sent =>
                {
                    if (mode == "authority" && sent.Path.Contains("/prices/", StringComparison.Ordinal))
                        authorized = false;
                },
            };
            using var provider = Provider(handler);
            Assert.False(
                (
                    await Assert.ThrowsAsync<BillingProviderException>(() =>
                        provider.CreateCheckoutAsync(Request, () => authorized, Token)
                    )
                ).OutcomeUnknown
            );
            Assert.DoesNotContain(handler.Requests, r => r.Method == "POST");
        }
    }

    [Fact]
    public async Task UnknownMutationOutcomesNeverRetryAndNeverExposeProviderBodies()
    {
        foreach (
            var failure in new[] { "network", "http", "redirect", "malformed", "duplicate", "oversized", "hostile-url" }
        )
        {
            var handler = new Handler
            {
                Reply = sent =>
                {
                    if (sent.Method != "POST")
                        return Default(sent);
                    if (failure == "network")
                        throw new HttpRequestException("fictional-private-provider-message");
                    if (failure == "http")
                        return new(HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("fictional-private-provider-message"),
                        };
                    if (failure == "redirect")
                        return new(HttpStatusCode.TemporaryRedirect)
                        {
                            Headers = { Location = new Uri("https://other.example.invalid/") },
                        };
                    if (failure == "malformed")
                        return new(HttpStatusCode.OK)
                        {
                            Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
                        };
                    if (failure == "duplicate")
                        return new(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                "{\"id\":\"one\",\"id\":\"two\"}",
                                Encoding.UTF8,
                                "application/json"
                            ),
                        };
                    if (failure == "oversized")
                        return new(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                new string('x', StripeBilling.MaximumResponseBytes + 1),
                                Encoding.UTF8,
                                "application/json"
                            ),
                        };
                    var checkout = Checkout();
                    checkout["url"] = "https://checkout.stripe.com.attacker.invalid/c/pay/fictional";
                    return Json(checkout);
                },
            };
            using var provider = Provider(handler);
            var error = await Assert.ThrowsAsync<BillingProviderException>(() =>
                provider.CreateCheckoutAsync(Request, () => true, Token)
            );
            Assert.True(error.OutcomeUnknown);
            Assert.Single(handler.Requests, r => r.Method == "POST");
            Assert.DoesNotContain("fictional-private-provider-message", error.ToString());
        }
    }

    [Fact]
    public async Task ReturnedPaymentLinksRejectAmbiguousBrowserParsing()
    {
        foreach (
            var url in new[]
            {
                " https://checkout.stripe.com/c/pay/fictional",
                "https://checkout.stripe.com/c/pay/fictional\n",
                "https://checkout.stripe.com\\@other.example/c/pay/fictional",
                "https://user@checkout.stripe.com/c/pay/fictional",
                "https://checkout.stripe.com:444/c/pay/fictional",
            }
        )
        {
            var handler = new Handler
            {
                Reply = _ =>
                {
                    var value = Checkout();
                    value["url"] = url;
                    return Json(value);
                },
            };
            using var provider = Provider(handler);
            Assert.False(
                (
                    await Assert.ThrowsAsync<BillingProviderException>(() =>
                        provider.CheckoutAsync("cs_test_fictional", Token)
                    )
                ).OutcomeUnknown
            );
        }
    }

    [Fact]
    public async Task ReadRecoveryIsOnlyGetAndRejectsMismatchedObjectsAndUnsupportedPrices()
    {
        var handler = new Handler();
        using var provider = Provider(handler);
        Assert.Equal("cs_test_fictional", (await provider.CheckoutAsync("cs_test_fictional", Token)).Id);
        foreach (
            var change in new Action<JsonObject>[]
            {
                p => p["livemode"] = true,
                p => p["recurring"]!["interval"] = "year",
                p => p["recurring"]!["usage_type"] = "metered",
                p => p["unit_amount"] = 0,
                p => p["transform_quantity"] = new JsonObject { ["divide_by"] = 10 },
            }
        )
        {
            handler.Reply = _ =>
            {
                var price = Price();
                change(price);
                return Json(price);
            };
            Assert.False(
                (
                    await Assert.ThrowsAsync<BillingProviderException>(() =>
                        provider.PriceAsync("price_fictional", Token)
                    )
                ).OutcomeUnknown
            );
        }
        handler.Reply = _ =>
        {
            var checkout = Checkout();
            checkout["id"] = "cs_test_foreign";
            return Json(checkout);
        };
        Assert.False(
            (
                await Assert.ThrowsAsync<BillingProviderException>(() =>
                    provider.CheckoutAsync("cs_test_fictional", Token)
                )
            ).OutcomeUnknown
        );
        Assert.All(handler.Requests, r => Assert.Equal("GET", r.Method));
    }

    [Fact]
    public async Task SubscriptionAndInvoiceReceiptsPreserveStatusAndPaymentEvidenceWithoutGrantingAccess()
    {
        var handler = new Handler();
        using var provider = Provider(handler);
        var subscription = await provider.SubscriptionAsync("sub_fictional", Token);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.False(subscription.CollectionPaused);
        Assert.True(subscription.PeriodEnd > subscription.PeriodStart);
        Assert.Equal("in_fictional", subscription.LatestInvoiceId);
        Assert.Equal(Request.PlanDigest, subscription.PlanDigest);
        var invoice = await provider.InvoiceAsync("in_fictional", Token);
        Assert.Equal(subscription.CustomerId, invoice.CustomerId);
        Assert.Equal(subscription.Id, invoice.SubscriptionId);
        Assert.Equal("paid", invoice.Status);
        Assert.Equal(0, invoice.AmountRemaining);
        handler.Reply = _ =>
        {
            var changed = Subscription();
            changed["status"] = "past_due";
            changed["pause_collection"] = new JsonObject { ["behavior"] = "void" };
            return Json(changed);
        };
        subscription = await provider.SubscriptionAsync("sub_fictional", Token);
        Assert.Equal("past_due", subscription.Status);
        Assert.True(subscription.CollectionPaused);
        handler.Reply = _ =>
        {
            var changed = Subscription();
            changed["items"]!["has_more"] = true;
            return Json(changed);
        };
        await Assert.ThrowsAsync<BillingProviderException>(() => provider.SubscriptionAsync("sub_fictional", Token));
        Assert.All(handler.Requests, r => Assert.Equal("GET", r.Method));
    }

    [Fact]
    public async Task PortalBindsCustomerConfigurationAndStripeDestination()
    {
        var handler = new Handler();
        using var provider = Provider(handler);
        var portal = await provider.CreatePortalAsync(
            new(Operation, "cus_fictional", "fictional-company"),
            () => true,
            Token
        );
        Assert.Equal("cus_fictional", portal.CustomerId);
        Assert.StartsWith("https://billing.stripe.com/p/session/", portal.Url);
        var sent = Assert.Single(handler.Requests, r => r.Method == "POST");
        Assert.Equal("hirezero:portal:" + Operation, sent.Idempotency);
        Assert.Contains("configuration=bpc_fictional", sent.Body);
        Assert.Contains("company%3Dfictional-company%26billingReturn%3D1", sent.Body);
        handler.Reply = r =>
            r.Method == "GET"
                ? Default(r)
                : Json(
                    new JsonObject
                    {
                        ["id"] = "bps_fictional",
                        ["object"] = "billing_portal.session",
                        ["livemode"] = false,
                        ["customer"] = "cus_other",
                        ["configuration"] = "bpc_fictional",
                        ["url"] = "https://billing.stripe.com/p/session/fictional",
                    }
                );
        Assert.True(
            (
                await Assert.ThrowsAsync<BillingProviderException>(() =>
                    provider.CreatePortalAsync(new(Guid.NewGuid().ToString("N"), "cus_fictional"), () => true, Token)
                )
            ).OutcomeUnknown
        );
    }

    [Fact]
    public async Task InvalidScopeModeAndPreCanceledCallsMakeNoProviderRequest()
    {
        var handler = new Handler();
        using var provider = Provider(handler, "sk_live_" + "fictional");
        await Assert.ThrowsAsync<BillingProviderException>(() => provider.PriceAsync("price_fictional", Token));
        await Assert.ThrowsAsync<FormatException>(() => provider.PriceAsync("price_fictional/../../account", Token));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var validProvider = Provider(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validProvider.CreateCheckoutAsync(Request, () => true, canceled.Token)
        );
        await Assert.ThrowsAsync<BillingProviderException>(() =>
            validProvider.CreateCheckoutAsync(Request, () => false, Token)
        );
        Assert.Empty(handler.Requests);
    }
}
