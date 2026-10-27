using HireZero.Core;
using HireZero.Integrations.Stripe;

public sealed class CompanyBillingRuntime(
    IBillingProvider provider,
    CompanyBillingConfiguration configuration,
    Func<ReadOnlyMemory<byte>, string, DateTimeOffset, BillingEventNotice?> verify,
    bool paymentsEnabled,
    bool liveMode
)
{
    public const string KeySecret = "HIREZERO_STRIPE_SECRET_KEY";
    public const string WebhookSecret = "HIREZERO_STRIPE_WEBHOOK_SECRET";
    public const string PreviousWebhookSecret = "HIREZERO_STRIPE_PREVIOUS_WEBHOOK_SECRET";
    public const string WebhookPath = "/api/billing/stripe/events";
    public IBillingProvider Provider { get; } = provider;
    public CompanyBillingConfiguration Configuration { get; } = configuration;
    public bool PaymentsEnabled { get; } = paymentsEnabled;
    public bool LiveMode { get; } = liveMode;

    public BillingEventNotice? Verify(ReadOnlyMemory<byte> body, string signature, DateTimeOffset now) =>
        verify(body, signature, now);

    public static CompanyBillingRuntime? Create(CompanySettings settings, HostedOptions? hosted)
    {
        if (settings.BillingCatalog is null)
            return null;
        if (hosted is null)
            throw new InvalidOperationException("Stripe billing requires hosted sign-in and its HTTPS public origin.");
        if (SecretSource.Default.Read(KeySecret) is null || SecretSource.Default.Read(WebhookSecret) is null)
            throw new InvalidOperationException(
                "Stripe billing needs separately supplied API and webhook signing secrets."
            );
        var info = new FileInfo(settings.BillingCatalog);
        if (!info.Exists || info.Length is <= 0 or > 64 * 1024)
            throw new InvalidOperationException("The billing catalog must be a bounded JSON file with explicit plans.");
        BillingPlan[] plans;
        try
        {
            plans = CompanyJson.Decode<BillingPlan[]>(File.ReadAllText(info.FullName));
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException)
        {
            throw new InvalidOperationException("The billing catalog is invalid. Check its plan and price fields.");
        }
        var adapter = new StripeBilling(
            new(
                settings.StripeAccount!,
                settings.StripeLiveMode,
                new Uri(hosted.PublicUrl, "?view=departments&section=billing"),
                settings.StripePortalConfiguration!
            ),
            () => SecretSource.Default.Read(KeySecret)
        );
        try
        {
            var catalog = new CompanyBillingConfiguration(adapter.Identity, plans);
            return new(
                adapter,
                catalog,
                (body, signature, now) =>
                    StripeWebhook.Verify(
                        body,
                        signature,
                        new[]
                        {
                            SecretSource.Default.Read(WebhookSecret),
                            SecretSource.Default.Read(PreviousWebhookSecret),
                        }
                            .OfType<string>()
                            .ToArray(),
                        settings.StripeLiveMode,
                        now
                    ),
                settings.BillingEnabled,
                settings.StripeLiveMode
            );
        }
        catch
        {
            adapter.Dispose();
            throw;
        }
    }
}
