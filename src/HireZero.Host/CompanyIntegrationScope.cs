using System.Security.Cryptography;
using System.Text;
using HireZero.Integrations.AgentApps;
using HireZero.Integrations.AzureDevOps;
using HireZero.Integrations.Google;
using HireZero.Integrations.Jev;
using HireZero.Integrations.Microsoft365;
using HireZero.Integrations.OpenRouter;
using Microsoft.AspNetCore.DataProtection;

// One company's connections. No ambient request context is consulted by a tool or background job: the
// provider closes over its company's storage and secret reader for its entire lifetime (ADR 0062).
public sealed class CompanyIntegrationScope
{
    public CompanyStore Store { get; }
    public ToolRegistry Tools { get; }
    public string DataRoot { get; }
    public bool MachineAccess { get; }
    public LocalSecretStore? PastedSecrets { get; }
    public SecretSource Secrets { get; }
    public GoogleAnalyticsProvider Google { get; }
    public GoogleAdsProvider GoogleAds { get; }
    public GoogleDriveDocuments DriveDocuments { get; }
    public CompanyDocumentImports DocumentImports { get; }
    public OpenRouterToolProvider OpenRouter { get; }
    public MicrosoftStore MicrosoftStore { get; }
    public MicrosoftConnector MicrosoftConnector { get; }
    public Microsoft365ToolProvider Microsoft { get; }
    public MicrosoftOptions MicrosoftOptions { get; }
    public IConfigurableIntegration[] Configurable { get; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public long Version { get; private set; }
    private readonly AgentAppsToolProvider _agentApps;
    private readonly AzureDevOpsToolProvider _azureDevOps;
    private readonly JevToolProvider? _jev;
    private readonly IToolProvider? _syntheticAnalytics;
    private readonly IToolProvider? _syntheticAds;

    public static string DirectoryFor(string hostRoot, string companyId, bool local) =>
        local && companyId == CompanyStore.DefaultCompanyId
            ? hostRoot
            : Path.Combine(
                hostRoot,
                "companies",
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(companyId))),
                "integrations"
            );

    public CompanyIntegrationScope(
        string hostRoot,
        CompanyStore store,
        ToolRegistry tools,
        bool local,
        IDataProtectionProvider protection,
        string origin,
        IToolProvider? syntheticAnalytics = null,
        IToolProvider? syntheticAds = null,
        HttpMessageHandler? googleHandler = null,
        IHttpClientFactory? microsoftClients = null
    )
    {
        Store = store;
        Tools = tools;
        MachineAccess = local && store.CompanyId == CompanyStore.DefaultCompanyId;
        DataRoot = DirectoryFor(hostRoot, store.CompanyId, local);
        // Preserve previously authorized local connections only in the original local company. New/hosted
        // companies have a separate directory AND encryption purpose. A copied envelope opens no other door.
        var scopedProtection = MachineAccess
            ? protection
            : protection.CreateProtector("HireZero.CompanyConnections.v1", store.CompanyId);
        PastedSecrets = local ? new LocalSecretStore(DataRoot, scopedProtection, LocalSecretStore.Names) : null;
        Secrets = MachineAccess
            ? new SecretSource(app: PastedSecrets!.Read)
            : new SecretSource(
                _ => null,
                [Path.Combine(DataRoot, "credentials")],
                PastedSecrets is null ? null : PastedSecrets.Read
            );
        Google = new(
            DataRoot,
            () => Secrets.Read(GoogleAnalyticsProvider.KeySecret),
            googleHandler,
            secrets: Secrets.Read
        );
        GoogleAds = new(DataRoot, Secrets.Read);
        DriveDocuments = new(DataRoot, () => Secrets.Read(GoogleDriveDocuments.KeySecret), googleHandler);
        DocumentImports = new(protection, store.CompanyId);
        store.DocumentSourceVerifier = DocumentImports.Verify;
        OpenRouter = new(
            DataRoot,
            key: () =>
                Secrets.Read("HIREZERO_OPENROUTER_API_KEY")
                ?? (MachineAccess ? Secrets.Read("OPENROUTER_API_KEY") : null)
        );
        _agentApps = AgentAppsToolProvider.Company(DataRoot);
        _azureDevOps = new(DataRoot);
        _jev = MachineAccess ? JevToolProvider.Company() : null;
        MicrosoftStore = new(DataRoot);
        MicrosoftOptions = new(origin + "/oauth/microsoft/callback", ReturnTo("microsoft"));
        MicrosoftConnector = Microsoft365Services.CompanyConnector(
            MicrosoftStore,
            scopedProtection,
            MicrosoftOptions,
            microsoftClients
        );
        Microsoft = new(MicrosoftStore, MicrosoftConnector);
        _syntheticAnalytics = syntheticAnalytics;
        _syntheticAds = syntheticAds;
        Configurable =
        [
            Google,
            GoogleAds,
            DriveDocuments,
            OpenRouter,
            .. MachineAccess ? new IConfigurableIntegration[] { _azureDevOps, _agentApps } : [],
        ];
    }

    public string ReturnTo(string provider) =>
        "/?view=departments&section=integrations&company="
        + Uri.EscapeDataString(Store.CompanyId)
        + "&"
        + provider
        + "=";

    // Created only by a future metered decision path, never adopted as an ordinary employee tool.
    // The original local company's existing alias stays local; all other companies use only their own secret.
    public OpenRouterDecisions CreateJevDecisions(HttpMessageHandler? handler = null) =>
        new(
            Store.CompanyId,
            () =>
                Secrets.Read("HIREZERO_OPENROUTER_API_KEY")
                ?? (MachineAccess ? Secrets.Read("OPENROUTER_API_KEY") : null),
            handler
        );

    public static IntegrationStatus MachineOnly(string id, string name) =>
        new(
            id,
            name,
            false,
            "This computer's connection belongs to the original local company. A separate company connection is not available yet."
        );

    public IntegrationStatus[] Statuses() =>
        [
            Microsoft.Status(),
            MachineAccess ? _agentApps.Status() : MachineOnly("agent-apps", "Coding apps"),
            MachineAccess ? _azureDevOps.Status() : MachineOnly("azure-devops", "Azure DevOps"),
            _jev?.Status() ?? MachineOnly("jev", "Jev"),
            OpenRouter.Status(),
            Google.Status(),
            GoogleAds.Status(),
            DriveDocuments.Status(),
        ];

    public void Register()
    {
        if (_jev?.Status().Active == true)
            _jev.Register(Tools);
        _syntheticAnalytics?.Register(Tools);
        _syntheticAds?.Register(Tools);
        Activate();
    }

    public void Activate()
    {
        if (MachineAccess && _agentApps.Status().Active)
            Tools.Adopt(_agentApps);
        if (MachineAccess && _azureDevOps.Status().Active)
            Tools.Adopt(_azureDevOps);
        if (MicrosoftStore.Settings().ClientId is { Length: > 0 })
            Microsoft.RegisterOnce(Tools);
        if (OpenRouter.Status().Active)
            Tools.Adopt(OpenRouter);
        if (Google.Status().Active)
            Tools.Adopt(Google, _syntheticAnalytics);
        if (GoogleAds.Status().Active)
            Tools.Adopt(GoogleAds, _syntheticAds);
        // Registered real providers recheck authorization at use; removing a key never restores fictional data.
    }

    public void Change(Action change)
    {
        Gate.Wait();
        try
        {
            change();
            Version++;
            Activate();
        }
        finally
        {
            Gate.Release();
        }
    }
}
