using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Stripe;

// Verifies raw transport bytes before parsing. A signed notice requests reconciliation; it never grants access.
public static class StripeWebhook
{
    public const int MaximumBodyBytes = 256 * 1024;
    private static readonly Dictionary<string, (string Kind, string Prefix)> Supported = new(StringComparer.Ordinal)
    {
        ["checkout.session.completed"] = ("checkout.session", "cs_"),
        ["checkout.session.expired"] = ("checkout.session", "cs_"),
        ["checkout.session.async_payment_succeeded"] = ("checkout.session", "cs_"),
        ["checkout.session.async_payment_failed"] = ("checkout.session", "cs_"),
        ["customer.subscription.created"] = ("subscription", "sub_"),
        ["customer.subscription.updated"] = ("subscription", "sub_"),
        ["customer.subscription.deleted"] = ("subscription", "sub_"),
        ["customer.subscription.paused"] = ("subscription", "sub_"),
        ["customer.subscription.resumed"] = ("subscription", "sub_"),
        ["invoice.paid"] = ("invoice", "in_"),
        ["invoice.payment_failed"] = ("invoice", "in_"),
        ["invoice.payment_action_required"] = ("invoice", "in_"),
        ["invoice.voided"] = ("invoice", "in_"),
        ["invoice.marked_uncollectible"] = ("invoice", "in_"),
    };

    public static BillingEventNotice? Verify(
        ReadOnlyMemory<byte> rawBody,
        string signature,
        IReadOnlyList<string> secrets,
        bool liveMode,
        DateTimeOffset now
    )
    {
        try
        {
            StripeBilling.Require(
                rawBody.Length is > 0 and <= MaximumBodyBytes
                    && signature.Length is > 0 and <= 4096
                    && secrets.Count is > 0 and <= 4
            );
            var parts = signature.Split(',').Select(p => p.Trim().Split('=', 2)).ToArray();
            StripeBilling.Require(parts.Length <= 20 && parts.All(p => p.Length == 2));
            var timestamps = parts.Where(p => p[0] == "t").Select(p => p[1]).ToArray();
            StripeBilling.Require(
                timestamps.Length == 1
                    && long.TryParse(timestamps[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            );
            var timestamp = long.Parse(timestamps[0], CultureInfo.InvariantCulture);
            StripeBilling.Require(Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(timestamp)).TotalSeconds) <= 300);
            var signatures = parts.Where(p => p[0] == "v1").Select(p => Convert.FromHexString(p[1])).ToArray();
            StripeBilling.Require(signatures.Length is > 0 and <= 8 && signatures.All(s => s.Length == 32));
            var prefix = Encoding.ASCII.GetBytes(timestamps[0] + ".");
            var signed = new byte[prefix.Length + rawBody.Length];
            prefix.CopyTo(signed, 0);
            rawBody.Span.CopyTo(signed.AsSpan(prefix.Length));
            var valid = false;
            foreach (var secret in secrets)
            {
                StripeBilling.Require(
                    secret.StartsWith("whsec_", StringComparison.Ordinal)
                        && secret.Length is > 12 and <= 256
                        && secret.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
                );
                var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
                foreach (var candidate in signatures)
                    valid |= CryptographicOperations.FixedTimeEquals(expected, candidate);
            }
            StripeBilling.Require(valid);
            using var document = JsonDocument.Parse(rawBody, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            StripeBilling.UniqueProperties(root);
            StripeBilling.Require(
                StripeBilling.Text(root, "object") == "event"
                    && StripeBilling.Text(root, "api_version") == StripeBilling.ApiVersion
                    && root.GetProperty("livemode").GetBoolean() == liveMode
                    && !StripeBilling.Present(root, "account")
            ); // Connected-account destinations need a separate binding.
            var eventId = StripeBilling.Identifier(StripeBilling.Text(root, "id"), "evt_");
            var created = StripeBilling.Time(root, "created");
            StripeBilling.Require(created > DateTimeOffset.UnixEpoch && created <= now.AddMinutes(5));
            var type = StripeBilling.Text(root, "type");
            StripeBilling.Require(type.Length <= 120);
            if (!Supported.TryGetValue(type, out var shape))
                return null;
            var subject = root.GetProperty("data").GetProperty("object");
            StripeBilling.Require(StripeBilling.Text(subject, "object") == shape.Kind);
            if (StripeBilling.Present(subject, "livemode"))
                StripeBilling.Require(subject.GetProperty("livemode").GetBoolean() == liveMode);
            return new(
                eventId,
                type,
                StripeBilling.Identifier(StripeBilling.Text(subject, "id"), shape.Prefix),
                created,
                Convert.ToHexStringLower(SHA256.HashData(rawBody.Span))
            );
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or FormatException
                        or KeyNotFoundException
                        or ArgumentException
                        or OverflowException
            )
        {
            // Never include the body or a signing secret in an exception or log. The butler seals the envelope.
            throw new BillingProviderException("webhook-unverified");
        }
    }
}
