using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Integrations.Codex;
using HireZero.Integrations.ModelApis;
using Microsoft.AspNetCore.DataProtection;

public sealed record CompanyApiModel(string Route, string Name, ModelPrice Price);

public sealed record CompanyModelOption(string Route, string Provider, string AllowanceUnit);

public sealed record CompanyModelConnectionInput(
    int ExpectedVersion,
    string Provider,
    string Route,
    string Key,
    string Decision,
    string? AnalysisRoute = null
);

public sealed record CompanyModelConnectionRemoval(int ExpectedVersion, string Decision);

public sealed record CompanyModelConnectionStatus(
    int Version,
    string Mode,
    string? Provider,
    string? Route,
    bool KeyStored,
    string Status,
    CompanyApiModel[] Models,
    string? AnalysisRoute = null,
    bool CanCombine = false,
    CompanyModelOption[]? Choices = null
);

// A company's key never falls back to a host secret. The butler keeps each purse in its own locked drawer.
public sealed partial class CompanyModelConnection(
    string companyId,
    IModelProvider platform,
    CompanyModelRouting platformRouting,
    Func<CompanyApiModel[]>? catalog = null,
    HttpMessageHandler? handler = null,
    HttpMessageHandler? decisionHandler = null
) : IModelProvider, IMeasuredModelProvider
{
    private sealed record Saved(
        int Version = 0,
        string Mode = "host",
        string? Provider = null,
        string? Route = null,
        string? Key = null,
        string Revision = "initial",
        bool Unreadable = false,
        string? AnalysisRoute = null
    );

    private Saved _saved = new();
    private readonly object _gate = new();
    private IDataProtector? _protector;
    private string? _path;
    private Saved Current => Volatile.Read(ref _saved);

    private CompanyApiModel[] Models() =>
        catalog?.Invoke()
        ?? ModelRoutes
            .PricedModels.Select(route => new CompanyApiModel(
                route,
                ModelRoutes.Name(route),
                ModelRoutes.Price(route)!
            ))
            .ToArray();

    public void Initialize(string hostRoot, IDataProtectionProvider protection)
    {
        lock (_gate)
        {
            if (_protector is not null)
                throw new InvalidOperationException("Company model storage was already initialized.");
            _protector = protection.CreateProtector("HireZero.CompanyModelKey.v1", companyId);
            _path = Path.Combine(
                hostRoot,
                "companies",
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(companyId))),
                "model-connection.secret"
            );
            if (!File.Exists(_path))
                return;
            try
            {
                if (new FileInfo(_path).Length > 64 * 1024)
                    throw new CryptographicException();
                var saved = CompanyJson.Decode<Saved>(_protector.Unprotect(File.ReadAllText(_path)));
                if (
                    saved is null
                    || saved.Version < 1
                    || saved.Mode is not ("host" or "company-key" or "host-and-company-key")
                    || !Guid.TryParseExact(saved.Revision, "N", out _)
                    || saved.Mode is "company-key" or "host-and-company-key"
                        && (
                            string.IsNullOrWhiteSpace(saved.Key)
                            || saved.Provider is not ("openai" or "anthropic" or "openrouter")
                            || string.IsNullOrWhiteSpace(saved.Route)
                        )
                )
                    throw new CryptographicException();
                Volatile.Write(ref _saved, saved);
            }
            catch (Exception error)
                when (error is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
            {
                Volatile.Write(ref _saved, new Saved(Mode: "company-key", Revision: "unreadable", Unreadable: true));
            }
        }
    }

    private string IdentityFor(Saved saved)
    {
        var underlying =
            saved.Mode == "company-key" ? ApiModelProvider.Identity
            : saved.Mode == "host-and-company-key"
                ? ApiModelProvider.Identity
                    + ":"
                    + ((platform as IMeasuredModelProvider)?.RuntimeIdentity ?? "unavailable")
            : (platform as IMeasuredModelProvider)?.RuntimeIdentity ?? "unavailable";
        if (saved.Version == 0 && !saved.Unreadable)
            return underlying;
        return "company-model-v1:"
            + CompanyJson.Digest(
                new
                {
                    companyId,
                    saved.Mode,
                    saved.Revision,
                    underlying,
                }
            );
    }

    public bool HasMeasured =>
        !Current.Unreadable && (Current.Mode == "company-key" || platform is IMeasuredModelProvider);
    public string? MeasuredIdentity => HasMeasured ? RuntimeIdentity : null;
    public string RuntimeIdentity => IdentityFor(Current);
    public bool Qualified => Current.Mode == "host" && platform.Qualified;
    public string AllowanceUnit =>
        Current.Mode == "company-key" || platform is ApiModelProvider ? "usd-cents" : "credits";
    private string PlatformProvider =>
        platform is CompanyCodexConnection ? "codex-oauth"
        : platform is ApiModelProvider ? "host-api"
        : "host";
    public bool CanCombine => platform is IMeasuredModelProvider && platform is not ApiModelProvider;

    private static bool UsesCompanyKey(Saved saved, string route) =>
        saved.Mode is "company-key" or "host-and-company-key" && (route == saved.Route || route == saved.AnalysisRoute);

    public CompanyModelTerms TermsFor(string route) =>
        UsesCompanyKey(Current, route)
            ? new(Current.Provider!, "usd-cents")
            : new(PlatformProvider, platform is ApiModelProvider ? "usd-cents" : "credits");

    public CompanyModelOption[] Options =>
        new[] { Routing.TextModel, Routing.AnalysisModel }
            .Concat(Routing.AdditionalTextModels ?? [])
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(route => new CompanyModelOption(route, TermsFor(route).Provider, TermsFor(route).AllowanceUnit))
            .ToArray();
    public CompanyModelRouting Routing
    {
        get
        {
            var saved = Current;
            return saved is { Mode: "company-key", Route: { } route }
                    ? platformRouting with
                    {
                        TextModel = route,
                        AnalysisModel = saved.AnalysisRoute,
                    }
                : saved.Mode == "host-and-company-key"
                    ? platformRouting with
                    {
                        AdditionalTextModels = new[] { saved.Route, saved.AnalysisRoute }
                            .OfType<string>()
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                    }
                : platformRouting;
        }
    }
    public CompanyModelFunding Funding
    {
        get
        {
            var saved = Current;
            return new(saved.Mode, saved.Provider ?? "host", saved.Revision);
        }
    }

    public CompanyModelFunding FundingFor(string kind) =>
        kind == "developer-job" ? new("host", "coding-runtime", "separate-runtime") : Funding;

    public CompanyModelFunding FundingFor(string kind, string route) =>
        kind == "developer-job" ? FundingFor(kind)
        : UsesCompanyKey(Current, route) ? new("company-key", Current.Provider!, Current.Revision)
        : new("host", PlatformProvider, Current.Revision);

    public ModelPrice? Price(string route) =>
        !UsesCompanyKey(Current, route)
        && (Current.Mode == "host-and-company-key" || platform is CompanyCodexConnection)
            ? null
            : Models().SingleOrDefault(m => m.Route == route)?.Price ?? ModelRoutes.Price(route);

    public string Status => Describe().Status;

    public CompanyModelConnectionStatus Describe()
    {
        var saved = Current;
        return new(
            saved.Version,
            saved.Mode,
            saved.Provider,
            saved.Route,
            saved.Key is not null,
            saved.Unreadable
                    ? "The saved company connection cannot be opened. Replace it or explicitly return to the host connection. No host key will be substituted."
                : saved.Mode == "host" ? platform.Status
                : saved.Mode == "host-and-company-key"
                    ? "The host connection remains the default. The saved API model is a separate choice for each reviewed text job; it never replaces a failed host request. Provider access is unverified."
                : "Company API key configured; provider access is unverified. Each request needs approval. The provider bills this company directly; HireZero subscription and safety limits still apply.",
            Models(),
            saved.Mode is "company-key" or "host-and-company-key" ? saved.AnalysisRoute : platformRouting.AnalysisModel,
            CanCombine,
            Options
        );
    }

    public CompanyModelConnectionStatus Save(CompanyModelConnectionInput input)
    {
        if (SpendStore is not null)
            throw new WorkspaceException(
                "Free allowance uses the configured host model. Company model connections are unavailable.",
                403
            );
        lock (_gate)
        {
            if (input.ExpectedVersion != Current.Version)
                throw new WorkspaceException("This connection changed. Reload before replacing its key.", 409);
            var combine = input.Decision == "use-host-and-company-key";
            if (
                (!combine && input.Decision != "use-company-key")
                || input.Provider is not ("openai" or "anthropic" or "openrouter")
            )
                throw new WorkspaceException("Explicitly authorize this company's provider connection.", 422);
            if (combine && !CanCombine)
                throw new WorkspaceException(
                    "Connect a measured host runtime before keeping it alongside an API model.",
                    422
                );
            var model = Models().SingleOrDefault(m => m.Route == input.Route);
            if (model is null || input.Provider != "openrouter" && ModelRoutes.Provider(input.Route) != input.Provider)
                throw new WorkspaceException("Choose a compatible model with an operator-reviewed price.", 422);
            var analysisRoute = string.IsNullOrWhiteSpace(input.AnalysisRoute) ? null : input.AnalysisRoute.Trim();
            if (
                analysisRoute is not null
                && (
                    !Models().Any(m => m.Route == analysisRoute)
                    || input.Provider != "openrouter" && ModelRoutes.Provider(analysisRoute) != input.Provider
                )
            )
                throw new WorkspaceException(
                    "Choose a compatible analysis model with an operator-reviewed price, or use the routine model.",
                    422
                );
            var key = input.Key?.Trim() ?? "";
            if (combine && new[] { input.Route, analysisRoute }.OfType<string>().Any(platformRouting.AllowsText))
                throw new WorkspaceException(
                    "Choose distinct host and API model routes so each review identifies exactly one provider.",
                    422
                );
            if (key.Length is < 8 or > 8192 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
                throw new WorkspaceException("Enter a provider API key of 8–8,192 characters without whitespace.", 422);
            Persist(
                new(
                    Current.Version + 1,
                    combine ? "host-and-company-key" : "company-key",
                    input.Provider,
                    input.Route,
                    key,
                    Guid.NewGuid().ToString("N"),
                    AnalysisRoute: analysisRoute
                )
            );
            return Describe();
        }
    }

    public CompanyModelConnectionStatus UseHost(CompanyModelConnectionRemoval input)
    {
        lock (_gate)
        {
            if (input.ExpectedVersion != Current.Version)
                throw new WorkspaceException("This connection changed. Reload before removing its key.", 409);
            if (input.Decision != "remove-key-and-use-host")
                throw new WorkspaceException(
                    "Confirm removing the company key and returning to the host connection.",
                    422
                );
            Persist(new(Current.Version + 1, Revision: Guid.NewGuid().ToString("N")));
            return Describe();
        }
    }

    private void Persist(Saved next)
    {
        if (_path is null || _protector is null)
            throw new WorkspaceException("Company key storage is unavailable.", 409);
        var directory = Path.GetDirectoryName(_path)!;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, _protector.Protect(CompanyJson.Encode(next)));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _path, overwrite: true);
            Volatile.Write(ref _saved, next);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public ModelInputAccounting CountInput(ModelCall call) =>
        Current.Mode == "host"
            ? platform.CountInput(call)
            : throw new ModelUnavailableException("Company API keys require explicit measured-job approval.");

    public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
        Current.Mode == "host"
            ? WithSpendAsync(call, call.MaxOutputTokens, () => platform.CompleteAsync(call, token), token)
            : throw new ModelUnavailableException("Company API keys do not authorize automatic model requests.");

    public async Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
    {
        var saved = Current;
        if (saved.Unreadable || job.RuntimeIdentity != IdentityFor(saved))
            throw new ModelTransportException(
                "The company model connection changed; prepare a fresh review.",
                0,
                usage: new(0, 0)
            );
        if (
            call.ModelRoute != job.Route
            || !Routing.AllowsText(job.Route)
            || job.Terms is not null && job.Terms != TermsFor(job.Route)
        )
            throw new ModelTransportException(
                "The approved model and provider terms no longer match this connection.",
                0,
                usage: new(0, 0)
            );
        if (saved.Mode == "host" || saved.Mode == "host-and-company-key" && !UsesCompanyKey(saved, job.Route))
        {
            if (platform is not IMeasuredModelProvider measured)
                throw new ModelTransportException("The host has no measured model connection.", 0, usage: new(0, 0));
            // The company envelope binds this generation and the underlying pinned runtime; only this trusted adapter unwraps it.
            return await WithSpendAsync(
                call,
                call.MaxOutputTokens > 0 ? call.MaxOutputTokens : job.Policy.OutputTarget,
                () =>
                    measured.CompleteMeasuredAsync(
                        call,
                        job with
                        {
                            RuntimeIdentity = measured.RuntimeIdentity,
                        },
                        token
                    ),
                token
            );
        }
        if (
            (call.ModelRoute != saved.Route && (saved.AnalysisRoute is null || call.ModelRoute != saved.AnalysisRoute))
            || saved.Key is null
        )
            throw new ModelTransportException(
                "The approved model does not match this company connection.",
                0,
                usage: new(0, 0)
            );
        var keyName = saved.Provider switch
        {
            "openai" => ApiModelProvider.OpenAIKey,
            "anthropic" => ApiModelProvider.AnthropicKey,
            _ => ApiModelProvider.OpenRouterKey,
        };
        using var api = new ApiModelProvider(
            name => name == keyName ? saved.Key : null,
            handler,
            Price,
            IdentityFor(saved)
        );
        return await api.CompleteMeasuredAsync(call, job, token);
    }
}

public static class CompanyModelConnectionEndpoints
{
    public static void MapCompanyModelConnection(
        this WebApplication app,
        Func<CompanyStore> store,
        Func<CompanyModelConnection> connection
    )
    {
        void Owner(HttpContext context) =>
            CompanyAccess.Demand(
                store().Actor((string)context.Items["login"]!),
                CompanyRoles.Owner,
                "manage this company's model connection"
            );
        app.MapGet(
            "/api/company/model-connection",
            (HttpContext context) =>
            {
                Owner(context);
                return Results.Ok(connection().Describe());
            }
        );
        app.MapPut(
            "/api/company/model-connection",
            (CompanyModelConnectionInput input, HttpContext context) =>
            {
                Owner(context);
                var status = connection().Save(input);
                store().RefreshModelReviews();
                store()
                    .IntegrationEvent(
                        "model.connection-changed",
                        "company-model",
                        "Owner selected a company API key for "
                            + status.Provider
                            + ". Prepared work requires a fresh review."
                    );
                return Results.Ok(status);
            }
        );
        app.MapDelete(
            "/api/company/model-connection",
            (HttpContext context, [Microsoft.AspNetCore.Mvc.FromBody] CompanyModelConnectionRemoval input) =>
            {
                Owner(context);
                var status = connection().UseHost(input);
                store().RefreshModelReviews();
                store()
                    .IntegrationEvent(
                        "model.connection-removed",
                        "company-model",
                        "Owner removed the current company key and selected the host connection. Prepared work requires a fresh review."
                    );
                return Results.Ok(status);
            }
        );
    }
}
