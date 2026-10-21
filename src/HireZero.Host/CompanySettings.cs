using System.Net;
using Microsoft.Extensions.Configuration;

// Every non-secret setting of the host, each with one name (docs/adr/0028-typed-settings.md, docs/SETTINGS.md).
// A value comes from the environment variable of that name or, failing that, from the JSON settings file named by
// HIREZERO_SETTINGS_FILE, whose keys are the same names. All values are checked together at startup and every problem
// is reported at once. A settings file may not hold a secret (docs/adr/0027) or a name this build does not know.
public sealed class CompanySettings
{
    public const string FileVariable = "HIREZERO_SETTINGS_FILE";

    public sealed record Setting(string Name, string Group, string Description, Action<CompanySettings, string> Apply);

    public static readonly Setting[] All =
    [
        new(
            "HIREZERO_DATA_ROOT",
            "Workspace",
            "Absolute directory for company data, keys and locks.",
            (s, v) => s.DataRoot = v
        ),
        new(
            "HIREZERO_OWNER_LOGIN",
            "Workspace",
            "The first owner's identity: an email in hosted mode.",
            (s, v) => s.OwnerLogin = v.Trim().ToLowerInvariant()
        ),
        new(
            "HIREZERO_LOCAL_OWNER",
            "Workspace",
            "1 for the one-person local owner mode; 0 or unset otherwise.",
            (s, v) => s.LocalOwner = Flag(v)
        ),
        new(
            global::ExecutionHold.Variable,
            "Workspace",
            "1 refuses startup until the owner clears the hold.",
            (s, v) => s.ExecutionHold = Flag(v)
        ),
        new(
            "HIREZERO_PUBLIC_URL",
            "Hosted",
            "The https origin members open, for example https://hirezero.example.com/.",
            (s, v) => s.PublicUrl = AbsoluteUri(v)
        ),
        new(
            "HIREZERO_LISTEN",
            "Hosted",
            "The address the host listens on, as http(s)://IP:port.",
            (s, v) => s.Listen = AbsoluteUri(v)
        ),
        new(
            "HIREZERO_OIDC_AUTHORITY",
            "Hosted",
            "The OIDC provider's issuer URL.",
            (s, v) => s.OidcAuthority = AbsoluteUri(v).ToString()
        ),
        new("HIREZERO_OIDC_CLIENT_ID", "Hosted", "HireZero's client id at the provider.", (s, v) => s.OidcClientId = v),
        new(
            "HIREZERO_OIDC_SCOPES",
            "Hosted",
            "Space-separated scopes; default openid profile email.",
            (s, v) => s.OidcScopes = v.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        ),
        new(
            "HIREZERO_TRUSTED_PROXY",
            "Hosted",
            "The reverse proxy's IP address, the only source of forwarded headers.",
            (s, v) =>
                s.TrustedProxy = IPAddress.TryParse(v, out var address)
                    ? address
                    : throw new FormatException("is not an IP address")
        ),
        new(
            "HIREZERO_TLS_CERTIFICATE",
            "Hosted",
            "A certificate file, when the host serves https itself.",
            (s, v) => s.TlsCertificate = v
        ),
        new(
            "HIREZERO_COMPANY_TEXT_MODEL",
            "Models",
            "Route for conversations, as vendor/model; default from config/model-routes.json.",
            (s, v) => s.TextModel = v
        ),
        new(
            "HIREZERO_COMPANY_CODING_MODEL",
            "Models",
            "Route for coding runs, as vendor/model; default from config/model-routes.json.",
            (s, v) => s.CodingModel = v
        ),
        new(
            "HIREZERO_API_MODELS",
            "Models",
            "1 uses an API model (Anthropic, OpenAI or OpenRouter key) when the Codex connection is not selected.",
            (s, v) => s.ApiModels = Flag(v)
        ),
        new(
            "HIREZERO_COMPANY_CODEX_EXECUTABLE",
            "Codex",
            "The pinned Codex executable (with --company-codex).",
            (s, v) => s.CodexExecutable = v
        ),
        new(
            "HIREZERO_COMPANY_CODEX_IDENTITY",
            "Codex",
            "The reviewed bundle identity of that executable.",
            (s, v) => s.CodexIdentity = v
        ),
        new("HIREZERO_COMPANY_CODEX_HOME", "Codex", "The dedicated Codex profile home.", (s, v) => s.CodexHome = v),
        new(
            "HIREZERO_COMPANY_CODING_READ",
            "Codex",
            "Semicolon-separated folders live coding runs may read.",
            (s, v) =>
                s.CodingRead = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ),
        new(
            "HIREZERO_COMPANY_CODING_PACKAGES",
            "Codex",
            "A pre-filled NuGet package folder live coding runs read offline.",
            (s, v) => s.CodingPackages = v.Trim()
        ),
        new(
            "HIREZERO_SYNTHETIC_WIKI",
            "Integrations",
            "1 turns on the synthetic Wiki pilot.",
            (s, v) => s.SyntheticWiki = Flag(v)
        ),
        new(
            "HIREZERO_SYNTHETIC_ANALYTICS",
            "Integrations",
            "1 serves fictional analytics data when Google is not connected.",
            (s, v) => s.SyntheticAnalytics = Flag(v)
        ),
        new(
            "HIREZERO_SYNTHETIC_ADS",
            "Integrations",
            "1 serves fictional ad data (in XTS) to Marketing.",
            (s, v) => s.SyntheticAds = Flag(v)
        ),
        new(
            "HIREZERO_SYNTHETIC_EMAIL",
            "Integrations",
            "1 serves fictional email campaign results to Marketing.",
            (s, v) => s.SyntheticEmail = Flag(v)
        ),
        new(
            "HIREZERO_SYNTHETIC_CRM",
            "Integrations",
            "1 serves a fictional sales pipeline (in XTS) to Marketing.",
            (s, v) => s.SyntheticCrm = Flag(v)
        ),
    ];

    public string? DataRoot { get; private set; }
    public string? OwnerLogin { get; private set; }
    public bool LocalOwner { get; private set; }
    public bool ExecutionHold { get; private set; }
    public Uri? PublicUrl { get; private set; }
    public Uri? Listen { get; private set; }
    public string? OidcAuthority { get; private set; }
    public string? OidcClientId { get; private set; }
    public string[]? OidcScopes { get; private set; }
    public IPAddress? TrustedProxy { get; private set; }
    public string? TlsCertificate { get; private set; }
    public string? TextModel { get; private set; }
    public string? CodingModel { get; private set; }
    public string? CodexExecutable { get; private set; }
    public string? CodexIdentity { get; private set; }
    public string? CodexHome { get; private set; }
    public string[] CodingRead { get; private set; } = [];
    public string? CodingPackages { get; private set; }
    public bool SyntheticWiki { get; private set; }
    public bool ApiModels { get; private set; }
    public bool SyntheticAnalytics { get; private set; }
    public bool SyntheticAds { get; private set; }
    public bool SyntheticEmail { get; private set; }
    public bool SyntheticCrm { get; private set; }

    // Where each set value came from: "environment" or the settings file. Values are not repeated here.
    public IReadOnlyDictionary<string, string> Sources { get; private set; } = new Dictionary<string, string>();

    public static CompanySettings Load(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var errors = new List<string>();
        var values = new Dictionary<string, (string Value, string Source)>(StringComparer.Ordinal);
        if (environment(FileVariable) is { Length: > 0 } file)
            ReadFile(file, values, errors);
        foreach (var setting in All)
            if (environment(setting.Name) is { Length: > 0 } value)
                values[setting.Name] = (value, "environment");

        var settings = new CompanySettings();
        foreach (var setting in All)
        {
            if (!values.TryGetValue(setting.Name, out var entry))
                continue;
            try
            {
                setting.Apply(settings, entry.Value);
            }
            catch (Exception error) when (error is FormatException or InvalidOperationException or UriFormatException)
            {
                errors.Add(setting.Name + " (" + entry.Source + ") " + error.Message.TrimEnd('.') + ".");
            }
        }
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "The settings are not valid. Nothing was started:\n- " + string.Join("\n- ", errors)
            );
        settings.Sources = values.ToDictionary(v => v.Key, v => v.Value.Source, StringComparer.Ordinal);
        return settings;
    }

    private static void ReadFile(string file, Dictionary<string, (string, string)> values, List<string> errors)
    {
        if (!File.Exists(file))
        {
            errors.Add(FileVariable + " names a file that does not exist: " + file);
            return;
        }
        IConfiguration configuration;
        try
        {
            configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(file), optional: false).Build();
        }
        catch (Exception error) when (error is FormatException or InvalidDataException)
        {
            errors.Add("The settings file is not valid JSON: " + error.Message);
            return;
        }
        var known = All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var secrets = CompanySecrets.Known.ToHashSet(StringComparer.Ordinal);
        foreach (var section in configuration.GetChildren())
        {
            if (secrets.Contains(section.Key))
                errors.Add(
                    section.Key + " is a secret: supply it as a secret file, not in settings (docs/SECRETS.md)."
                );
            else if (!known.Contains(section.Key))
                errors.Add(section.Key + " in the settings file is not a setting this version knows.");
            else if (section.Value is null)
                errors.Add(section.Key + " in the settings file must be a string.");
            else
                values[section.Key] = (section.Value, "settings file");
        }
    }

    private static bool Flag(string value) =>
        value switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => throw new FormatException("must be 1 or 0"),
        };

    private static Uri AbsoluteUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : throw new FormatException("is not an absolute URL");
}
