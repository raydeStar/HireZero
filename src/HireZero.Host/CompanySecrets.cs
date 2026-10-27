// Every secret the host and its integrations read, for the startup report (docs/SECRETS.md). The report names where
// each came from, never its value.
public static class CompanySecrets
{
    public static readonly string[] Known =
    [
        CompanyBillingRuntime.KeySecret,
        CompanyBillingRuntime.WebhookSecret,
        CompanyBillingRuntime.PreviousWebhookSecret,
        "HIREZERO_DATABASE",
        "HIREZERO_DATABASE_PASSWORD",
        "HIREZERO_AUDIT_KEY",
        HireZero.Integrations.Notifications.SmtpNotifications.PasswordSecret,
        CompanyKeyProtection.Variable,
        CompanyKeyProtection.PreviousVariable,
        HostedOptions.ClientSecretVariable,
        "HIREZERO_TLS_CERTIFICATE_PASSWORD",
        HireZero.Integrations.ModelApis.ApiModelProvider.AnthropicKey,
        HireZero.Integrations.ModelApis.ApiModelProvider.OpenAIKey,
        "HIREZERO_OPENROUTER_API_KEY",
        "OPENROUTER_API_KEY",
        "HIREZERO_JEV_API_KEY",
        "TYPESAFE_API_KEY",
        "HIREZERO_SEMAFORGE_API_KEY",
        HireZero.Integrations.Web.BusinessSearch.KeySecret,
        HireZero.Integrations.Google.GoogleAnalyticsProvider.KeySecret,
        HireZero.Integrations.Google.GoogleDriveDocuments.KeySecret,
        HireZero.Integrations.Google.GoogleAdsProvider.DeveloperTokenSecret,
        HireZero.Integrations.Google.GoogleAdsProvider.ClientSecretSecret,
        HireZero.Integrations.Google.GoogleAdsProvider.RefreshTokenSecret,
        HireZero.Integrations.Google.GoogleAdsProvider.ChangeRefreshTokenSecret,
    ];

    public static IEnumerable<string> Report(SecretSource? secrets = null)
    {
        secrets ??= SecretSource.Default;
        foreach (var name in Known)
        {
            var status = secrets.Describe(name);
            if (status.Origin == SecretOrigin.Missing)
                continue;
            var line = "Secret " + name + ": " + status.Origin.ToString().ToLowerInvariant();
            if (status.Location is not null)
                line += " (" + status.Location + ")";
            if (status.Origin == SecretOrigin.Environment)
                line += "; prefer a mounted file, which other processes cannot read from the environment";
            if (status.AlsoInEnvironment)
                line += "; the environment variable of the same name is ignored";
            yield return line;
        }
        yield return "Stored token keys are protected by " + CompanyKeyProtection.Protection(secrets) + ".";
    }
}
