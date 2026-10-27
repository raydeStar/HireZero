using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using HireZero.Core;
using HireZero.Integrations.Stripe;

public sealed class StripeWebhookTests
{
    private const string Secret = "whsec_fictional_signing_key";
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static JsonObject Event() =>
        new()
        {
            ["id"] = "evt_fictional",
            ["object"] = "event",
            ["api_version"] = StripeBilling.ApiVersion,
            ["livemode"] = false,
            ["created"] = Now.AddDays(-2).ToUnixTimeSeconds(),
            ["type"] = "customer.subscription.updated",
            ["data"] = new JsonObject
            {
                ["object"] = new JsonObject
                {
                    ["id"] = "sub_fictional",
                    ["object"] = "subscription",
                    ["livemode"] = false,
                    ["private_field"] = "fictional content omitted from notice",
                },
            },
        };

    private static byte[] Body(JsonObject? value = null) => Encoding.UTF8.GetBytes((value ?? Event()).ToJsonString());

    private static string Signature(byte[] body, DateTimeOffset? at = null, string secret = Secret)
    {
        var time = (at ?? Now).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signed = Encoding.UTF8.GetBytes(time + ".").Concat(body).ToArray();
        return "t="
            + time
            + ",v1="
            + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed));
    }

    private static BillingEventNotice? Verify(byte[] body, string? signature = null) =>
        StripeWebhook.Verify(body, signature ?? Signature(body), [Secret], false, Now);

    [Fact]
    public void RawSignatureRotationAndFreshRetryOfAnOlderEventProduceOnlyABoundedNotice()
    {
        var body = Body();
        var signature = Signature(body) + ",v1=" + new string('0', 64) + ",v0=ignored";
        var notice = StripeWebhook.Verify(body, signature, ["whsec_fictional_old_key", Secret], false, Now)!;
        Assert.Equal("evt_fictional", notice.Id);
        Assert.Equal("sub_fictional", notice.ObjectId);
        Assert.Equal(Now.AddDays(-2), notice.CreatedAt);
        Assert.Equal(64, notice.PayloadDigest.Length);
        Assert.DoesNotContain("fictional content omitted", CompanyJson.Encode(notice));
        Assert.Equal(notice, Verify(body)); // Durable deduplication belongs to the billing ledger, not this parser.
    }

    [Fact]
    public void AlteredBodiesWrongSecretsStaleFutureAndAmbiguousSignaturesAreRejected()
    {
        var body = Body();
        foreach (
            var signature in new[]
            {
                Signature(body, Now.AddSeconds(-301)),
                Signature(body, Now.AddSeconds(301)),
                Signature(body, secret: "whsec_fictional_wrong_key"),
                Signature(body) + ",t=" + Now.ToUnixTimeSeconds(),
                "t=" + Now.ToUnixTimeSeconds() + ",v0=" + new string('0', 64),
                Signature(body) + ",v1=nothex",
            }
        )
            Assert.Equal(
                "webhook-unverified",
                Assert.Throws<BillingProviderException>(() => Verify(body, signature)).Code
            );
        Assert.Throws<BillingProviderException>(() =>
            Verify(body.Concat(new byte[] { 32 }).ToArray(), Signature(body))
        );
        Assert.Throws<BillingProviderException>(() => StripeWebhook.Verify(body, Signature(body), [], false, Now));
    }

    [Fact]
    public void SignedWrongModeVersionConnectedAccountOrObjectCannotBecomeAnAcceptedNotice()
    {
        foreach (
            var change in new Action<JsonObject>[]
            {
                e => e["livemode"] = true,
                e => e["api_version"] = "2025-01-01",
                e => e["account"] = "acct_other",
                e => e["data"]!["object"]!["object"] = "invoice",
                e => e["data"]!["object"]!["id"] = "sub_fictional/../../other",
                e => e["data"]!["object"]!["livemode"] = true,
            }
        )
        {
            var e = Event();
            change(e);
            Assert.Throws<BillingProviderException>(() => Verify(Body(e)));
        }
    }

    [Fact]
    public void UnsupportedEventsAreIgnoredOnlyAfterVerificationAndBodyLimitsStillApply()
    {
        var irrelevant = Event();
        irrelevant["type"] = "product.created";
        Assert.Null(Verify(Body(irrelevant)));
        var duplicate = Encoding.UTF8.GetBytes(
            Encoding
                .UTF8.GetString(Body())
                .Replace(
                    "\"id\":\"evt_fictional\"",
                    "\"id\":\"evt_fictional\",\"id\":\"evt_other\"",
                    StringComparison.Ordinal
                )
        );
        Assert.Throws<BillingProviderException>(() => Verify(duplicate));
        var tooLarge = new byte[StripeWebhook.MaximumBodyBytes + 1];
        Assert.Throws<BillingProviderException>(() => Verify(tooLarge));
        var bad = Assert.Throws<BillingProviderException>(() =>
            Verify(Body(), Signature(Body(), secret: "whsec_fictional_other_key"))
        );
        Assert.DoesNotContain(Secret, bad.ToString());
        Assert.False(bad.OutcomeUnknown);
    }
}
