using HireZero.Core;
using HireZero.Integrations.AgentApps;
using HireZero.Integrations.AzureDevOps;
using HireZero.Integrations.Codex;
using HireZero.Integrations.Git;
using HireZero.Integrations.GitHub;
using HireZero.Integrations.Jev;
using HireZero.Integrations.Microsoft365;
using HireZero.Integrations.OpenRouter;
using HireZero.Integrations.Wiki;

// Explicit composition profile. Native Codex inspection is opt-in; legacy workers are never registered.
public static class CompanyWorkspace
{
    // DI also releases the lock when a fixture or startup failure disposes without StopAsync.
    private sealed class SchedulerOwnership(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }

    public const string Flag = "--company-local";
    public const string CodexFlag = "--company-codex";

    // Live company coding on the pinned Codex runtime (docs/adr/0014-pinned-codex-runtime.md). Off unless asked for,
    // and only together with the Codex connection profile.
    public const string CodingFlag = "--company-coding";

    // The least role a change through a route needs; commands carry their own check in the store.
    internal static string? RouteRole(PathString path) =>
        path.StartsWithSegments("/api/company/commands") ? null
        : path == "/api/company/documents/retrieve" || path == "/api/company/memory/retrieve" ? CompanyRoles.Viewer
        : path.StartsWithSegments("/api/company/actions") || path.StartsWithSegments("/api/company/executions")
            ? CompanyAccess.Steps
        : CompanyRoles.Owner;

    public static CompanyRole[] Definitions() => [.. HostEmployees.Modules.Select(CompanyRole.From)];

    // A scheduler round skipped because another writer held the company database leaves a warning
    // (docs/adr/0167-a-locked-database-skips-a-scheduler-round.md).
    private static Action<Exception> LogSkippedRound(ILogger log, string company) =>
        error =>
            log.LogWarning(
                "Skipped a scheduler round for company {Company}: {Error} It will be tried again.",
                company,
                error.Message
            );

    public static WebApplication? TryCreate(
        string[] args,
        string owner,
        bool localOwner,
        string root,
        IModelProvider? fixture = null,
        string? fixtureUrl = null,
        string? fixtureWebRoot = null,
        Action<ToolRegistry>? fixtureTools = null,
        ICompanyCodingFactory? fixtureCoding = null,
        HostedOptions? fixtureHosted = null,
        StoreDatabase? fixtureDatabase = null,
        CompanySettings? settings = null,
        Func<DateTimeOffset>? fixtureClock = null,
        INotificationSender? fixtureNotificationSender = null,
        HttpMessageHandler? fixtureGoogle = null,
        ISandboxExecutor? fixtureSandbox = null,
        IBrowserExecutor? fixtureBrowser = null,
        CompanyBillingRuntime? fixtureBilling = null,
        Func<CompanyApiModel[]>? fixtureCompanyModels = null,
        Func<string, HttpMessageHandler?>? fixtureCompanyModelHandler = null,
        Func<string, HttpMessageHandler?>? fixtureCompanyDecisionHandler = null
    )
    {
        settings ??= CompanySettings.Load();
        // Local mode is the one-person, loopback-only owner; hosted mode signs members in through OIDC (ADR 0020).
        var hosted =
            fixtureHosted ?? (args.Contains(CompanyHosting.HostedFlag) ? HostedOptions.FromSettings(settings) : null);
        if (!args.Contains(Flag) && hosted is null)
            return null;
        if (args.Contains(CodingFlag) && !args.Contains(CodexFlag) && fixtureCoding is null)
            throw new InvalidOperationException(
                "Live company coding needs the Codex connection profile (--company-codex)."
            );
        if (hosted is null && !localOwner)
            throw new InvalidOperationException("Company local mode requires explicit local owner mode.");
        if (hosted is not null && (localOwner || !owner.Contains('@')))
            throw new InvalidOperationException(
                "Hosted mode needs HIREZERO_OWNER_LOGIN set to the first owner's email, and no local owner mode."
            );
        if (fixtureUrl is null && string.IsNullOrWhiteSpace(settings.DataRoot))
            throw new InvalidOperationException("Select an explicit new HIREZERO_DATA_ROOT for company-local mode.");
        if (settings.ExecutionHold)
            throw new InvalidOperationException(
                "Execution is held (HIREZERO_EXECUTION_HOLD=1). Nothing starts until the owner clears the hold."
            );
        var url = hosted?.Listen.ToString() ?? fixtureUrl ?? settings.Listen?.ToString() ?? "http://127.0.0.1:5065";
        var uri = hosted is not null
            ? hosted.Listen
            : LocalHosting.LoopbackUri(url, "Company mode must use a loopback listener.");
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args.Where(a => a != Flag && a != CompanyHosting.HostedFlag && a != CodexFlag && a != CodingFlag)
                    .ToArray(),
                WebRootPath = fixtureUrl is not null ? fixtureWebRoot : null,
            }
        );
        if (hosted is not null)
        {
            builder.ListenHosted(hosted, 16_384);
            builder.Services.AddHostedSignIn(hosted);
        }
        else
            builder.ListenLoopbackOnly(uri, uri.Port, 16_384);
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "workspace.sqlite");
        // SQLite in the data root unless a server database is configured (docs/adr/0025-one-schema-two-databases.md).
        var database = fixtureDatabase ?? CompanyDatabases.FromEnvironment(root, file);
        if (database is SqliteStoreDatabase && File.Exists(file))
        {
            using var check = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = file,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString()
            );
            check.Open();
            using var query = check.CreateCommand();
            query.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='co_company'";
            if (Convert.ToInt32(query.ExecuteScalar()) == 0)
                throw new InvalidOperationException(
                    "Company-local mode refuses an existing legacy database. Select a new data root."
                );
        }
        // FileShare.None owns only this new company scheduler; no process enumeration or runtime manipulation.
        var ownership = new FileStream(
            Path.Combine(root, "company-scheduler.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        try
        {
            var routing = new CompanyModelRouting(
                settings.TextModel ?? ModelRoutes.Route(ModelRoutes.CompanyText),
                settings.CodingModel ?? ModelRoutes.Route(ModelRoutes.CompanyCoding)
            )
            {
                AnalysisModel = settings.AnalysisModel,
            };
            routing.Validate();
            CompanyCodexOptions? codex =
                fixture is null && args.Contains(CodexFlag)
                    ? new(
                        settings.CodexExecutable
                            ?? throw new InvalidOperationException("Pin the selected Codex executable."),
                        settings.CodexIdentity
                            ?? throw new InvalidOperationException(
                                "Pin the selected Codex executable bundle identity."
                            ),
                        Path.Combine(root, "codex-scratch"),
                        Home: settings.CodexHome
                            ?? throw new InvalidOperationException("Select the dedicated native Codex profile home.")
                    )
                    : null;
            IModelProvider platformModel =
                fixture
                ?? (
                    codex is not null ? new CompanyCodexConnection(codex, routing)
                    // The pinned Codex CLI stays the model when it is selected; an API model only on explicit opt-in
                    // (docs/adr/0036-api-model-provider.md).
                    : settings.ApiModels ? new HireZero.Integrations.ModelApis.ApiModelProvider()
                    : new UnavailableModelProvider()
                );
            var model = new CompanyModelConnection(
                CompanyStore.DefaultCompanyId,
                platformModel,
                routing,
                fixtureUrl is null ? null : fixtureCompanyModels,
                fixtureUrl is null ? null : fixtureCompanyModelHandler?.Invoke(CompanyStore.DefaultCompanyId),
                fixtureUrl is null ? null : fixtureCompanyDecisionHandler?.Invoke(CompanyStore.DefaultCompanyId)
            );
            var coding = fixtureCoding;
            if (coding is null && codex is not null && args.Contains(CodingFlag))
            {
                var live = new CompanyCodexCodingFactory(
                    codex,
                    routing,
                    settings.CodingRead,
                    packageCache: settings.CodingPackages
                );
                coding = live.Qualified
                    ? live
                    : throw new InvalidOperationException("Live company coding is not qualified: " + live.Refusal);
            }
            var notificationSender =
                fixtureNotificationSender
                ?? (fixtureUrl is null ? CompanyNotifications.Create(settings, hosted) : null);
            // A real executor is registered only after operator qualification. Fixtures cannot activate a daemon.
            var sandbox = fixtureUrl is not null ? fixtureSandbox : CompanySandboxRuntime.Create(settings);
            var browser = fixtureUrl is not null ? fixtureBrowser : CompanyBrowserRuntime.Create(settings);
            var billing = fixtureUrl is not null ? fixtureBilling : CompanyBillingRuntime.Create(settings, hosted);
            var notificationUrl = fixtureNotificationSender is not null
                ? "https://workspace.example.invalid"
                : settings.PublicUrl?.ToString();
            var store = new CompanyStore(database, owner, Definitions(), routing, model.MeasuredIdentity)
            {
                Clock = fixtureClock ?? (() => DateTimeOffset.UtcNow),
                CurrentModelRouting = () => model.Routing,
                CurrentMeasuredRuntimeIdentity = () => model.MeasuredIdentity,
                UsageFunding = kind => model.FundingFor(kind),
                UsageRouteFunding = (kind, route) => model.FundingFor(kind, route),
                PrepareDecisionPacket = model.PrepareDecision,
                CurrentDecisionPacket = model.DecisionCurrent,
                MeasuredTerms = route => model.TermsFor(route),
                UsagePrice = model.Price,
                NotificationServiceIdentity = notificationSender?.Identity,
                NotificationPublicUrl = notificationUrl,
                SandboxRuntimeIdentity = sandbox?.Identity,
                SandboxExecutionAvailable = () => CompanySandboxRuntime.Status(sandbox).Available,
                ExtendedSandboxAvailable = () =>
                    sandbox is IExtendedSandboxRuntime extended && extended.ExtendedScriptsAvailable,
                BrowserRuntimeIdentity = browser?.Identity,
                BrowserExecutionAvailable = () => CompanyBrowserRuntime.Status(browser).Available,
                BillingConfiguration = billing?.Configuration,
            };
            // Request helpers are bound below; background work uses its own company connection scope.
            Func<IntegrationStatus[]> currentIntegrations = () => [];
            Func<IntegrationSetup[]> currentSetups = () => [];
            builder.Services.AddSingleton(store);
            Func<CompanyStore[]> sandboxStores = () => [store];
            if (billing is not null)
            {
                builder.Services.AddSingleton(s => new CompanyBillingService(
                    () => sandboxStores(),
                    billing,
                    s.GetRequiredService<ILogger<CompanyBillingService>>()
                ));
                builder.Services.AddHostedService(s => s.GetRequiredService<CompanyBillingService>());
            }
            if (browser is not null)
            {
                builder.Services.AddSingleton(s => new CompanyBrowserService(
                    () => sandboxStores(),
                    browser,
                    s.GetRequiredService<ILogger<CompanyBrowserService>>()
                ));
                builder.Services.AddHostedService(s => s.GetRequiredService<CompanyBrowserService>());
            }
            if (sandbox is not null)
            {
                builder.Services.AddSingleton(s => new CompanySandboxService(
                    () => sandboxStores(),
                    sandbox,
                    s.GetRequiredService<ILogger<CompanySandboxService>>()
                ));
                builder.Services.AddHostedService(s => s.GetRequiredService<CompanySandboxService>());
            }
            // Tools employees may call, audited in the company store (docs/adr/0007-tool-registry.md).
            // The only verifier is the publication's: a write or push runs only as a call of an approved
            // publication (docs/adr/0012-company-publication.md); every other write is refused.
            var tools = new ToolRegistry(
                store,
                [store.PublicationAuthority, new CompanyBusinessResearch.SearchAuthority(store)]
            );
            builder.Services.AddSingleton(tools);
            builder.Services.AddSingleton<IModelProvider>(model);
            builder.Services.AddSingleton(s => new CompanyScheduler(store, model)
            {
                SkippedRound = LogSkippedRound(s.GetRequiredService<ILogger<CompanyScheduler>>(), store.CompanyId),
                NotificationSender = notificationSender,
                RefreshScheduledSources = (command, subject, token) =>
                    CompanyBusinessResearch.RunRefreshAsync(command, subject, store, tools, token),
            });
            builder.Services.AddHostedService(s => s.GetRequiredService<CompanyScheduler>());
            // The synthetic Wiki pilot, only when it is switched on (docs/adr/0015-publishing-an-approved-artifact.md).
            var wiki = fixtureTools is null && settings.SyntheticWiki;
            // Microsoft 365: owner-started reads only, no polling; its tools register once a client ID is saved.
            builder.Services.AddSingleton(global::Microsoft.Extensions.Options.Options.Create(settings));
            builder.Services.AddCompanyKeyProtection(root);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddCompanyTelemetry(CompanyTelemetryHosting.Enabled);
            var membership = new CompanyMembership();
            builder.Services.AddSingleton(membership);
            builder.Services.AddSingleton(_ => new SchedulerOwnership(ownership));
            var app = builder.Build();
            _ = app.Services.GetRequiredService<SchedulerOwnership>();
            if (billing?.Provider is IDisposable billingProvider)
                app.Lifetime.ApplicationStopped.Register(billingProvider.Dispose);
            foreach (var (name, source) in settings.Sources.OrderBy(s => s.Key, StringComparer.Ordinal))
                app.Logger.LogInformation("Setting {Name}: {Source}", name, source);
            foreach (var line in CompanySecrets.Report())
                app.Logger.LogInformation("{Secret}", line);
            if (fixtureDatabase is null && database is IDisposable owned)
                app.Lifetime.ApplicationStopped.Register(owned.Dispose);
            var connectionOrigin =
                hosted?.PublicUrl.GetLeftPart(UriPartial.Authority)
                ?? "http://" + (fixtureUrl is null || uri.Port != 0 ? uri.Authority : "127.0.0.1:5065");
            var protection =
                app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
            model.Initialize(root, protection);
            if (fixture is null && platformModel is IDisposable ownedModel)
                app.Lifetime.ApplicationStopped.Register(ownedModel.Dispose);
            var connections = new System.Collections.Concurrent.ConcurrentDictionary<string, CompanyIntegrationScope>(
                StringComparer.Ordinal
            );
            var syntheticAnalytics = settings.SyntheticAnalytics
                ? new HireZero.Integrations.Synthetic.SyntheticAnalyticsProvider()
                : null;
            var syntheticAds = settings.SyntheticAds
                ? new HireZero.Integrations.Synthetic.SyntheticAdsProvider()
                : null;
            var syntheticEmail = settings.SyntheticEmail
                ? new HireZero.Integrations.Synthetic.SyntheticEmailProvider()
                : null;
            var syntheticCrm = settings.SyntheticCrm
                ? new HireZero.Integrations.Synthetic.SyntheticCrmProvider()
                : null;
            var gitHubSignIn = fixtureUrl is not null ? new GitHubSignIn(() => null) : new GitHubSignIn();
            CompanyIntegrationScope Connect(CompanyStore companyStore, ToolRegistry registry)
            {
                var scope = new CompanyIntegrationScope(
                    root,
                    companyStore,
                    registry,
                    hosted is null,
                    protection,
                    connectionOrigin,
                    syntheticAnalytics,
                    syntheticAds,
                    googleHandler: fixtureGoogle
                );
                connections[companyStore.CompanyId] = scope;
                companyStore.IntegrationStatuses = () => IntegrationsFor(scope);
                if (fixtureTools is not null)
                    fixtureTools(registry);
                else
                {
                    // Machine-wide sign-ins and process access stay with the original local company.
                    if (scope.MachineAccess)
                    {
                        if (GitToolRegistration.FindGit() is { } git)
                            registry.AddGitTools(new GitCheckoutInspector(git));
                        new GitHubCli().Register(registry);
                        new HireZero.Integrations.Node.NpmInstaller().Register(registry);
                        new HireZero.Integrations.Node.TrustedPostgresToolchain(scope.DataRoot).Register(registry);
                        new HireZero.Integrations.Node.TrustedPostgresProfiles(file).Register(registry);
                    }
                    new HireZero.Integrations.Web.WebReader().Register(registry);
                    new HireZero.Integrations.Web.BusinessSearch(() =>
                        SecretSource.Default.Read(HireZero.Integrations.Web.BusinessSearch.KeySecret)
                    ).Register(registry);
                    if (wiki)
                        WikiRegistration.CompanyProvider().Register(registry);
                    scope.Register();
                    syntheticEmail?.Register(registry);
                    syntheticCrm?.Register(registry);
                }
                return scope;
            }
            Connect(store, tools);
            var runtimes = new System.Collections.Concurrent.ConcurrentDictionary<string, CompanyRuntime>(
                StringComparer.Ordinal
            );
            runtimes[store.CompanyId] = new(store, tools, app.Services.GetRequiredService<CompanyScheduler>(), model);
            CompanyRuntime Open(string id)
            {
                lock (runtimes)
                {
                    if (runtimes.TryGetValue(id, out var existing))
                        return existing;
                    var key = id;
                    var listing =
                        CompanyStore.Companies(database).SingleOrDefault(c => c.Id == key)
                        ?? throw new WorkspaceException("No such company.", 404);
                    var companyModel = new CompanyModelConnection(
                        key,
                        platformModel,
                        routing,
                        fixtureUrl is null ? null : fixtureCompanyModels,
                        fixtureUrl is null ? null : fixtureCompanyModelHandler?.Invoke(key),
                        fixtureUrl is null ? null : fixtureCompanyDecisionHandler?.Invoke(key)
                    );
                    companyModel.Initialize(root, protection);
                    var companyStore = new CompanyStore(
                        database,
                        listing.Creator,
                        Definitions(),
                        routing,
                        companyModel.MeasuredIdentity,
                        key
                    )
                    {
                        Clock = fixtureClock ?? (() => DateTimeOffset.UtcNow),
                        CurrentModelRouting = () => companyModel.Routing,
                        CurrentMeasuredRuntimeIdentity = () => companyModel.MeasuredIdentity,
                        UsageFunding = kind => companyModel.FundingFor(kind),
                        UsageRouteFunding = (kind, route) => companyModel.FundingFor(kind, route),
                        PrepareDecisionPacket = companyModel.PrepareDecision,
                        CurrentDecisionPacket = companyModel.DecisionCurrent,
                        MeasuredTerms = route => companyModel.TermsFor(route),
                        UsagePrice = companyModel.Price,
                        NotificationServiceIdentity = notificationSender?.Identity,
                        NotificationPublicUrl = notificationUrl,
                        SandboxRuntimeIdentity = sandbox?.Identity,
                        SandboxExecutionAvailable = () => CompanySandboxRuntime.Status(sandbox).Available,
                        ExtendedSandboxAvailable = () =>
                            sandbox is IExtendedSandboxRuntime extended && extended.ExtendedScriptsAvailable,
                        BrowserRuntimeIdentity = browser?.Identity,
                        BrowserExecutionAvailable = () => CompanyBrowserRuntime.Status(browser).Available,
                        BillingConfiguration = billing?.Configuration,
                    };
                    var registry = new ToolRegistry(
                        companyStore,
                        [companyStore.PublicationAuthority, new CompanyBusinessResearch.SearchAuthority(companyStore)]
                    );
                    Connect(companyStore, registry);
                    var scheduler = new CompanyScheduler(companyStore, companyModel)
                    {
                        SkippedRound = LogSkippedRound(
                            app.Services.GetRequiredService<ILogger<CompanyScheduler>>(),
                            companyStore.CompanyId
                        ),
                        NotificationSender = notificationSender,
                        RefreshScheduledSources = (command, subject, token) =>
                            CompanyBusinessResearch.RunRefreshAsync(command, subject, companyStore, registry, token),
                    };
                    _ = scheduler.StartAsync(app.Lifetime.ApplicationStopping);
                    app.Lifetime.ApplicationStopping.Register(() => scheduler.StopAsync(CancellationToken.None).Wait());
                    var opened = new CompanyRuntime(companyStore, registry, scheduler, companyModel);
                    runtimes[id] = opened;
                    return opened;
                }
            }
            // Durable schedules run after restart even when nobody has opened that company's page yet.
            foreach (var listing in CompanyStore.Companies(database))
                Open(listing.Id);
            sandboxStores = () => runtimes.Values.Select(r => r.Store).ToArray();
            // Hosted sign-in binds a waiting invitation in whichever company issued it.
            membership.SignIn = (subject, email, verified, name) =>
                CompanyStore
                    .Companies(database)
                    .Select(c => Open(c.Id).Store.SignIn(subject, email, verified, name))
                    .ToArray()
                    .FirstOrDefault(a => a is not null);
            CompanyChoice[] MemberCompanies(string subject)
            {
                var choices = new List<CompanyChoice>();
                foreach (var listing in CompanyStore.Companies(database))
                {
                    var current = Open(listing.Id).Store;
                    try
                    {
                        var actor = current.Actor(subject);
                        choices.Add(new(listing.Id, current.Name(), actor.Role));
                    }
                    catch (WorkspaceException e) when (e.Status == 403) { }
                }
                return choices.ToArray();
            }
            var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
            CompanyRuntime Runtime() =>
                accessor.HttpContext?.Items["company"] as CompanyRuntime ?? runtimes[CompanyStore.DefaultCompanyId];
            CompanyStore Store() => Runtime().Store;
            CompanyModelConnection Model() => Runtime().Model;
            ToolRegistry Tools() => Runtime().Tools;
            CompanyIntegrationScope Connections() => connections[Store().CompanyId];
            app.Lifetime.ApplicationStopped.Register(ownership.Dispose);
            app.UseWorkspaceCorrelation();
            if (hosted is not null)
                app.UseHostedSignIn(hosted);
            app.Use(
                async (c, next) =>
                {
                    LocalHosting.ApplySecurityHeaders(c);
                    var billingEvent =
                        c.Request.Path == CompanyBillingRuntime.WebhookPath && c.Request.Method == "POST";
                    string origin;
                    if (hosted is null)
                    {
                        // Fixture port zero is bound by Kestrel, but Host still proves the chosen profile's authority.
                        var expected = fixtureUrl is null || uri.Port != 0 ? uri.Authority : "127.0.0.1:5065";
                        if (!LocalHosting.IsDirectLocalRequest(c, expected))
                        {
                            if (LocalHosting.LocalhostRedirect(c, expected) is { } to)
                                c.Response.Redirect(to);
                            else
                                await LocalHosting.RefuseAsync(c, expected);
                            return;
                        }
                        origin = "http://" + expected;
                        c.Items["login"] = owner;
                        // Assistants on this computer (docs/adr/0048-assistant-connect-requests.md). A browser always
                        // sends Origin on such a request, so none is accepted: no web page can reach this endpoint.
                        if (c.Request.Path == CompanyMcp.Path)
                        {
                            if (c.Request.Headers.Origin.Count > 0)
                            {
                                c.Response.StatusCode = 403;
                                return;
                            }
                            await CompanyMcp.HandleAsync(
                                c,
                                Store(),
                                () => currentIntegrations(),
                                () => currentSetups()
                            );
                            return;
                        }
                    }
                    else
                    {
                        // Only the public HTTPS origin is served; the session cookie names the member.
                        if (!c.Request.IsHttps || c.Request.Host.Value != hosted.PublicUrl.Authority)
                        {
                            c.Response.StatusCode = 403;
                            return;
                        }
                        origin = hosted.PublicUrl.GetLeftPart(UriPartial.Authority);
                        var subject = CompanyHosting.Subject(c);
                        if (
                            subject is null
                            && c.Request.Path.StartsWithSegments("/api")
                            && c.Request.Path != "/api/session"
                            && !billingEvent
                        )
                        {
                            c.Response.StatusCode = 401;
                            await c.Response.WriteAsJsonAsync(new { error = "Sign in first.", signIn = "/signin" });
                            return;
                        }
                        c.Items["login"] = subject;
                    }
                    if (billingEvent)
                    {
                        await CompanyBillingEndpoints.ReceiveAsync(c, billing, store);
                        return;
                    }
                    if (
                        c.Request.Method is not ("GET" or "HEAD")
                        && (
                            c.Request.Headers.Origin != origin
                            || c.Request.Headers["X-Dev-Request"] != "1"
                            || !c.Request.HasJsonContentType()
                        )
                    )
                    {
                        c.Response.StatusCode = 403;
                        return;
                    }
                    try
                    {
                        // Every API request acts as an active member; a change needs its route's role, and a
                        // command is checked again against its own action in the store (docs/adr/0020).
                        if (c.Request.Path.StartsWithSegments("/api") && c.Items["login"] is string)
                        {
                            if (
                                c.Request.Path != "/api/session"
                                && c.Request.Headers.TryGetValue(
                                    CompanyHosting.BrowserScopeHeader,
                                    out var expectedMember
                                )
                                && (
                                    expectedMember.Count != 1
                                    || expectedMember[0] != CompanyHosting.BrowserScope((string)c.Items["login"]!)
                                )
                            )
                            {
                                c.Response.StatusCode = 409;
                                await c.Response.WriteAsJsonAsync(
                                    new
                                    {
                                        error = "The signed-in account changed. Reload before continuing.",
                                        code = "session-changed",
                                    }
                                );
                                return;
                            }
                            // A directory read needs an authenticated subject, not membership in the default company.
                            // The front door must not require a key to a room the guest was never invited into.
                            if (!(c.Request.Method == "GET" && c.Request.Path == "/api/companies"))
                            {
                                try
                                {
                                    var company = c.Request.Headers["X-HireZero-Company"].FirstOrDefault();
                                    var runtime = Open(
                                        string.IsNullOrEmpty(company) ? CompanyStore.DefaultCompanyId : company
                                    );
                                    var actor = runtime.Store.Actor((string)c.Items["login"]!);
                                    c.Items["company"] = runtime;
                                    System.Diagnostics.Activity.Current?.SetTag(
                                        "hirezero.company",
                                        runtime.Store.CompanyId
                                    );
                                    c.Items["role"] = actor.Role;
                                    if (
                                        c.Request.Method is not ("GET" or "HEAD")
                                        && RouteRole(c.Request.Path) is { } required
                                    )
                                        CompanyAccess.Demand(actor, required, "change " + c.Request.Path);
                                }
                                catch (WorkspaceException e)
                                    when (c.Request.Method == "GET"
                                        && c.Request.Path == "/api/session"
                                        && e.Status is 403 or 404
                                    )
                                {
                                    c.Items["companySelectionRequired"] = true;
                                }
                            }
                        }
                        await next();
                    }
                    catch (WorkspaceException e)
                    {
                        c.Response.StatusCode = e.Status;
                        await c.Response.WriteAsJsonAsync(new { error = e.Message, code = "company-command-refused" });
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        c.Response.StatusCode = 422;
                        await c.Response.WriteAsJsonAsync(new { error = "Invalid structured input." });
                    }
                }
            );
            CompanyBillingEndpoints.Map(app, Store, billing);
            app.MapGet(
                "/health",
                () =>
                    Results.Ok(
                        new
                        {
                            status = "healthy",
                            companyLocal = true,
                            modelExecution = model.Qualified,
                            liveCoding = hosted is null && coding?.Qualified == true,
                        }
                    )
            );
            app.MapGet(
                "/api/session",
                IResult (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    if (context.Items["companySelectionRequired"] is true && context.Items["login"] is string subject)
                        return Results.Ok(
                            new
                            {
                                login = subject,
                                browserScope = CompanyHosting.BrowserScope(subject),
                                signedIn = true,
                                accessMode = hosted is null ? "local" : "hosted",
                                companySelectionRequired = true,
                                companies = MemberCompanies(subject),
                            }
                        );
                    return Results.Ok(
                        new
                        {
                            login = (string?)context.Items["login"],
                            browserScope = context.Items["login"] is string signedInSubject
                                ? CompanyHosting.BrowserScope(signedInSubject)
                                : null,
                            role = (string?)context.Items["role"],
                            signedIn = context.Items["login"] is string,
                            accessMode = hosted is null ? "local" : "hosted",
                            roles = Array.Empty<string>(),
                            agentModel = Model().HasMeasured
                                ? CompanyModelRouting.Native(Model().Routing.TextModel)
                                    + " (explicit approval required)"
                                : "Unavailable",
                            agentReasoning = "",
                            companyEnabled = true,
                            executionHeld = false,
                        }
                    );
                }
            );
            // Qualification of a machine runner is not authority to lend that machine to every company.
            ICompanyCodingFactory? CodingFor(CompanyIntegrationScope scope) => scope.MachineAccess ? coding : null;
            IntegrationStatus GitHubStatus(CompanyIntegrationScope scope) =>
                !scope.MachineAccess ? CompanyIntegrationScope.MachineOnly("github", "GitHub")
                : gitHubSignIn.Status() is { State: "connected" } signedIn
                    ? new("github", "GitHub", true, "Signed in" + (signedIn.Account is { } a ? " as " + a : "") + ".")
                : new("github", "GitHub", false, "Not signed in to GitHub on this computer.");
            IntegrationStatus[] IntegrationsFor(CompanyIntegrationScope scope) =>
                [
                    GitHubStatus(scope),
                    .. scope.Statuses(),
                    syntheticAnalytics?.Status()
                        ?? new("synthetic-analytics", "Synthetic analytics", false, "Fictional analytics data is off."),
                    syntheticAds?.Status() ?? new("synthetic-ads", "Synthetic ads", false, "Fictional ad data is off."),
                    syntheticEmail?.Status()
                        ?? new("synthetic-email", "Synthetic email", false, "Fictional email data is off."),
                    syntheticCrm?.Status()
                        ?? new("synthetic-crm", "Synthetic CRM", false, "Fictional CRM data is off."),
                    new(
                        "wiki",
                        "Synthetic Wiki",
                        wiki,
                        wiki ? "The synthetic Wiki pilot is switched on." : "The synthetic Wiki pilot is off."
                    ),
                    new(
                        "git",
                        "Git",
                        scope.Tools.Has("git.read"),
                        scope.Tools.Has("git.read")
                            ? "Git is available."
                            : "Local Git access is not available to this company."
                    ),
                    new(
                        "live-coding",
                        "Live coding",
                        CodingFor(scope)?.Qualified == true,
                        !scope.MachineAccess
                                ? "Local coding is available only to the original company on this computer. This company needs an isolated execution environment."
                            : coding?.Qualified == true ? "A qualified coding runner is configured."
                            : coding is CompanyCodexCodingFactory { Refusal: { } why } ? why
                            : "Start with -Coding and the pinned Codex runtime to enable live coding."
                    ),
                ];
            IntegrationStatus[] Integrations() => IntegrationsFor(Connections());
            currentIntegrations = Integrations;
            app.MapCompanyMicrosoft365(Connections);
            app.MapCompanyModelConnection(Store, Model);
            app.MapGet(
                "/api/company",
                () =>
                    Results.Ok(
                        new
                        {
                            capabilities = new
                            {
                                localManagement = true,
                                modelExecution = Model().Qualified,
                                liveCoding = CodingFor(Connections())?.Qualified == true,
                                sandboxExecution = CompanySandboxRuntime.Status(sandbox).Available,
                                sandboxRecovery = sandbox is not null,
                                browserExecution = CompanyBrowserRuntime.Status(browser).Available,
                                browserRecovery = browser is not null,
                                costOwner = Store().Actor((string)accessor.HttpContext!.Items["login"]!).Role
                                    == CompanyRoles.Owner,
                                browserOwner = Store().Actor((string)accessor.HttpContext!.Items["login"]!).Role
                                    == CompanyRoles.Owner,
                                sandboxOwner = Store().Actor((string)accessor.HttpContext!.Items["login"]!).Role
                                    == CompanyRoles.Owner,
                                measuredOneJob = Model().HasMeasured,
                                conversationalReplies = Model().HasMeasured,
                                businessSearch = fixtureTools is not null
                                    ? Tools().Contracts.Any(c => c.Id == HireZero.Tools.Web.WebTools.Search.Id)
                                    : SecretSource
                                        .Default.Describe(HireZero.Integrations.Web.BusinessSearch.KeySecret)
                                        .Origin != SecretOrigin.Missing,
                                allowanceUnit = Model().AllowanceUnit,
                                modelChoices = Model().Options,
                                providerStatus = Model().Status,
                                modelRouting = Model().Routing,
                                ceoEnabled = true,
                                meetingsEnabled = true,
                                legacyDeveloperAvailable = false,
                                integrations = Integrations(),
                                tools = Tools()
                                    .Contracts.Select(c => new
                                    {
                                        c.Id,
                                        risk = c.Risk.ToString().ToLowerInvariant(),
                                        c.Description,
                                    }),
                            },
                            data = Store().Dashboard(),
                        }
                    )
            );
            app.MapGet(
                "/api/company/runtime",
                async (CancellationToken token) =>
                    platformModel is CompanyCodexConnection connection
                        ? Results.Ok(await connection.InspectAsync(token))
                        : Results.Json(new { error = "Select the explicit Codex connection profile." }, statusCode: 503)
            );
            // Configuration and activation affect only the selected company (ADR 0062).
            currentSetups = () => [.. Connections().Configurable.Select(i => i.Setup)];
            app.MapIntegrationSetup(Connections, Integrations);
            app.MapContextSources(Store, Tools);
            app.MapBusinessResearch(Store, Tools);
            app.MapCompanyDocuments(Store);
            app.MapDriveDocuments(Connections);
            // Google sign-in for Analytics and Search Console (docs/adr/0049-google-sign-in.md), on this host's origin.
            app.MapGoogleSignIn(Connections, connectionOrigin);
            app.MapGet("/api/company/tool-calls", (int? limit) => Results.Ok(Store().ToolCalls(limit ?? 100)));
            // Members and their roles, for every member to see (docs/adr/0020).
            app.MapGet("/api/company/members", () => Results.Ok(Store().Members()));
            // The company's tamper-evident audit chain (docs/adr/0022-tamper-evident-audit-log.md): any member can
            // verify it; approvers and owners can export it as NDJSON ending with the verified head.
            app.MapGet("/api/company/audit/verify", () => Results.Ok(Store().VerifyAudit()));
            app.MapGet(
                "/api/company/audit/export",
                (HttpContext context) =>
                {
                    var current = Store();
                    CompanyAccess.Demand(
                        current.Actor((string)context.Items["login"]!),
                        CompanyRoles.Approver,
                        "export the audit log"
                    );
                    var check = current.VerifyAudit();
                    var lines = new System.Text.StringBuilder();
                    foreach (var entry in current.AuditEntries(0, 100_000))
                        lines.Append(CompanyJson.Encode(entry)).Append('\n');
                    lines
                        .Append(
                            CompanyJson.Encode(
                                new
                                {
                                    company = current.CompanyId,
                                    exportedAt = DateTimeOffset.UtcNow,
                                    verified = check.Intact,
                                    count = check.Count,
                                    head = check.Head,
                                    brokenAt = check.BrokenAt,
                                }
                            )
                        )
                        .Append('\n');
                    context.Response.Headers.ContentDisposition =
                        "attachment; filename=\"hirezero-audit-" + current.CompanyId + ".ndjson\"";
                    return Results.Text(lines.ToString(), "application/x-ndjson");
                }
            );
            // The companies the signed-in member belongs to; an owner creates another, and becomes its owner
            // (docs/adr/0021-companies-per-data-root.md).
            app.MapGet(
                "/api/companies",
                (HttpContext context) =>
                {
                    var subject = (string)context.Items["login"]!;
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(MemberCompanies(subject));
                }
            );
            app.MapPost(
                "/api/companies",
                (CompanyName body, HttpContext context) =>
                {
                    var subject = (string)context.Items["login"]!;
                    var created = Store()
                        .CreateCompany(body.Name ?? "", body.RequestId ?? Guid.NewGuid().ToString("D"), subject);
                    return Results.Ok(created);
                }
            );
            app.MapGet(
                "/api/companies/creation/{requestId}",
                (string requestId, HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    var created = Store().CreatedCompany(requestId, (string)context.Items["login"]!);
                    return created is null
                        ? Results.NotFound(
                            new
                            {
                                error = "Company creation is not confirmed. Check again before starting another creation.",
                                code = "company-creation-not-recorded",
                            }
                        )
                        : Results.Ok(created);
                }
            );
            app.MapPost(
                "/api/company/name",
                (CompanyName body) =>
                {
                    Store().Rename(body.Name ?? "");
                    return Results.Ok(new { name = Store().Name() });
                }
            );
            app.MapGet(
                "/api/company/github/sign-in",
                (HttpContext context) =>
                {
                    CompanyAccess.Demand(
                        Store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "view GitHub sign-in details"
                    );
                    return Connections().MachineAccess
                        ? Results.Ok(gitHubSignIn.Status())
                        : Results.Ok(
                            new
                            {
                                state = "unavailable",
                                code = "",
                                url = "",
                                error = "This computer's GitHub connection belongs to the original local company.",
                            }
                        );
                }
            );
            app.MapGet("/api/company/schedules", () => Results.Ok(Store().Schedules()));
            app.MapGet("/api/company/schedules/script-templates", () => Results.Ok(Store().ScriptScheduleTemplates()));
            app.MapGet(
                "/api/company/schedules/browser-templates",
                () => Results.Ok(Store().BrowserScheduleTemplates())
            );
            app.Use(
                async (context, next) =>
                {
                    if (
                        context.Request.Method == "POST"
                        && (
                            context.Request.Path == "/api/company/sandboxes/prepare"
                            || context.Request.Path == "/api/company/sandboxes/outputs/prepare"
                            || context.Request.Path == "/api/company/browsers/prepare"
                            || context.Request.Path == "/api/company/browsers/outputs/prepare"
                        )
                    )
                    {
                        var bodyLimit =
                            context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                        if (bodyLimit is { IsReadOnly: false })
                            bodyLimit.MaxRequestBodySize = context.Request.Path.StartsWithSegments(
                                "/api/company/browsers"
                            )
                                ? 128 * 1024
                                : 512 * 1024;
                    }
                    await next(context);
                }
            );
            app.MapPost(
                "/api/company/sandboxes/prepare",
                (CompanyCommand command, HttpContext context) =>
                {
                    if (command.Action != "sandbox.prepare")
                        throw new WorkspaceException("This route only prepares a script review.", 422);
                    return Results.Content(
                        Store().Apply(command, (string)context.Items["login"]!, out _),
                        "application/json"
                    );
                }
            );
            app.MapPost(
                "/api/company/sandboxes/outputs/prepare",
                (CompanyCommand command, HttpContext context) =>
                {
                    if (command.Action != "sandbox.output.prepare")
                        throw new WorkspaceException(
                            "This route only prepares a saved script output for task review.",
                            422
                        );
                    return Results.Content(
                        Store().Apply(command, (string)context.Items["login"]!, out _),
                        "application/json"
                    );
                }
            );
            app.MapGet(
                "/api/company/tasks/{id}/sandbox-inputs",
                (string id) => Results.Ok(Store().SandboxEvidenceOptions(id))
            );
            app.MapGet(
                "/api/company/compute",
                () =>
                    Results.Ok(
                        new
                        {
                            available = CompanySandboxRuntime.Status(sandbox).Available,
                            recoveryAvailable = sandbox is not null,
                            runtime = CompanySandboxRuntime.Status(sandbox),
                            scriptTiers = Store().ScriptTiers(),
                            browserAvailable = CompanyBrowserRuntime.Status(browser).Available,
                            browserRecoveryAvailable = browser is not null,
                            browserRuntime = CompanyBrowserRuntime.Status(browser),
                            browserRetention = Store().BrowserRetention(),
                            usage = Store().ComputeUsage(),
                            jobs = Store().ComputeSummaries(),
                        }
                    )
            );
            foreach (
                var (route, action) in new (string, string)[]
                {
                    ("/api/company/browsers/prepare", "browser.prepare"),
                    ("/api/company/browsers/outputs/prepare", "browser.output.prepare"),
                }
            )
                app.MapPost(
                    route,
                    (CompanyCommand command, HttpContext context) =>
                    {
                        if (command.Action != action)
                            throw new WorkspaceException("This route only prepares its named browser review.", 422);
                        return Results.Content(
                            Store().Apply(command, (string)context.Items["login"]!, out _),
                            "application/json"
                        );
                    }
                );
            app.MapPost(
                "/api/company/browsers/{id}/recover",
                async (string id) =>
                {
                    if (browser is null)
                        throw new WorkspaceException(
                            "The original browser runtime is unavailable. Saved captures and allowance remain recorded.",
                            409
                        );
                    return Results.Ok(
                        await app
                            .Services.GetRequiredService<CompanyBrowserService>()
                            .RecoverAsync(Store(), id, app.Lifetime.ApplicationStopping)
                    );
                }
            );
            app.MapPost(
                "/api/company/sandboxes/{id}/recover",
                async (string id) =>
                {
                    if (sandbox is null)
                        throw new WorkspaceException(
                            "The approved script runtime is unavailable. Its saved result and allowance remain recorded.",
                            409
                        );
                    return Results.Ok(
                        await app
                            .Services.GetRequiredService<CompanySandboxService>()
                            .RecoverAsync(Store(), id, app.Lifetime.ApplicationStopping)
                    );
                }
            );
            app.MapGet(
                "/api/company/usage",
                (string? period, string? before) => Results.Ok(Store().Usage(period, before))
            );
            app.MapGet(
                "/api/company/progress",
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().Progress((string)context.Items["login"]!));
                }
            );
            app.MapGet(
                "/api/company/work-control",
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().WorkControl());
                }
            );
            app.MapGet(
                "/api/company/retirement-review",
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().RetirementReview((string)context.Items["login"]!));
                }
            );
            app.MapGet(
                "/api/company/tasks/{id}/costs",
                (string id, HttpContext context) =>
                {
                    CompanyAccess.Demand(
                        Store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "review task costs"
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().TaskCosts(id));
                }
            );
            app.MapGet("/api/company/memory", () => Results.Ok(Store().Memories()));
            app.MapGet(
                "/api/company/decisions",
                (HttpContext context) =>
                {
                    CompanyAccess.Demand(
                        Store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "review decisions"
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(new { connection = Model().DecisionConnection(), items = Store().Decisions() });
                }
            );
            app.MapGet(
                "/api/company/decisions/{id}",
                (string id, HttpContext context) =>
                {
                    CompanyAccess.Demand(
                        Store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "review decisions"
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().DecisionReview(id));
                }
            );
            app.MapGet("/api/company/memory/{id}", (string id) => Results.Ok(Store().Memory(id)));
            app.MapGet("/api/company/memory/work", () => Results.Ok(Store().MemoryWorks()));
            app.MapGet("/api/company/memory/work/{id}", (string id) => Results.Ok(Store().MemoryWork(id)));
            app.MapGet("/api/company/memory/meetings/{id}", (string id) => Results.Ok(Store().MemoryMeeting(id)));
            app.MapGet(
                "/api/company/memory/analyses",
                (HttpContext context) =>
                {
                    CompanyAccess.Demand(
                        Store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "review memory checks"
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().MemoryAnalyses());
                }
            );
            app.MapGet(
                "/api/company/memory/analyses/{id}",
                (string id, HttpContext context) =>
                {
                    CompanyAccess.Demand(
                        Store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "review memory checks"
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(Store().MemoryAnalysis(id));
                }
            );
            app.MapPost(
                "/api/company/memory/retrieve",
                (CompanyDocuments.RetrievalRequest request) =>
                    Results.Ok(Store().RetrieveMemory(request.Query, request.EmployeeId, request.CompanyOnly))
            );
            app.MapGet(
                "/api/company/notifications/unread",
                (HttpContext context) =>
                    Results.Ok(new { unread = Store().UnreadNotices((string)context.Items["login"]!) })
            );
            app.MapGet(
                "/api/company/notifications",
                (HttpContext context, long? before, bool? unread) =>
                    Results.Ok(Store().Inbox((string)context.Items["login"]!, before ?? long.MaxValue, unread ?? false))
            );
            app.MapGet("/api/company/schedules/{id}", (string id) => Results.Ok(Store().ScheduleDetail(id)));
            app.MapPost(
                "/api/company/github/sign-in",
                () =>
                    Connections().MachineAccess
                        ? Results.Ok(gitHubSignIn.Start())
                        : throw new WorkspaceException(
                            "A company-scoped GitHub connection is not available yet. This computer's sign-in cannot be shared.",
                            403
                        )
            );
            // The company's day on this machine's clock: what waits on the owner and what happened (2B.5i).
            app.MapGet(
                "/api/company/today",
                (string? day) =>
                {
                    var zone = TimeZoneInfo.Local;
                    var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
                    if (
                        day is not null
                        && !DateOnly.TryParseExact(
                            day,
                            "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out date
                        )
                    )
                        return Results.BadRequest(new { error = "Use a yyyy-MM-dd day." });
                    DateTimeOffset At(DateOnly value)
                    {
                        var local = value.ToDateTime(TimeOnly.MinValue);
                        return new(local, zone.GetUtcOffset(local));
                    }
                    return Results.Ok(Store().Today(date.ToString("yyyy-MM-dd"), At(date), At(date.AddDays(1))));
                }
            );
            // Runs a declared employee action once (docs/adr/0011-employee-actions.md). A retried request returns the
            // recorded task without running the action again.
            app.MapPost(
                "/api/company/actions",
                async (System.Text.Json.JsonElement body, HttpContext context, CancellationToken token) =>
                {
                    var command = CompanyJson.Decode<CompanyCommand>(body.GetRawText()) with
                    {
                        Action = "action.start",
                    };
                    var task = CompanyJson.Decode<CompanyTask>(
                        Store().Apply(command, (string)context.Items["login"]!, out var fresh)
                    );
                    if (!fresh)
                        return Results.Ok(Store().TaskDetail(task.Id));
                    var employee = Store().EmployeeOf(task.EmployeeId);
                    var module = HostEmployees.Modules.Single(m => m.Manifest.Id == employee.RoleId);
                    var input = CompanyJson.Decode<Dictionary<string, string>>(task.Input);
                    try
                    {
                        var output = await module.RunActionAsync(
                            task.Skill,
                            input,
                            Tools().For(module.Manifest, employee.Id, task.Id).ReadOnly(),
                            token
                        );
                        Store().CompleteAction(task.Id, output);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        // Expected refusals keep their message; anything else is reported without internals.
                        Store()
                            .FailAction(
                                task.Id,
                                error is WorkspaceException or ModelUnavailableException
                                    ? error.Message
                                    : "The action failed unexpectedly (" + error.GetType().Name + ").",
                                (error as IntegrationMissingException)?.IntegrationId
                            );
                    }
                    return Results.Ok(Store().TaskDetail(task.Id));
                }
            );
            // One step of reviewed execution, run by the module of the employee the work belongs to. Each step is
            // single use in the store; a failure after anything was sent is recorded as unknown and never repeated.
            app.MapPost(
                "/api/company/executions/{step}/{id}",
                async (string step, string id, CancellationToken token) =>
                {
                    if (!Enum.TryParse<ExecutionStep>(step, true, out var parsed) || !step.All(char.IsAsciiLetter))
                        return Results.NotFound();
                    var employeeId = parsed switch
                    {
                        ExecutionStep.Prepare => Store().Work(id).EmployeeId,
                        ExecutionStep.Run => Store().Execution(id).EmployeeId,
                        ExecutionStep.Propose => Store().FindExecution(id)?.EmployeeId
                            ?? Store().Artifact(id).EmployeeId,
                        _ => Store().Publication(id).EmployeeId,
                    };
                    var employee = Store().EmployeeOf(employeeId);
                    var module = HostEmployees.Modules.Single(m => m.Manifest.Id == employee.RoleId);
                    try
                    {
                        return Results.Ok(
                            await module.ExecuteAsync(
                                parsed,
                                id,
                                new(Store(), Tools(), CodingFor(Connections())),
                                token
                            )
                        );
                    }
                    catch (Exception error) when (error is not (WorkspaceException or OutOfMemoryException))
                    {
                        // The store already recorded the outcome; internals are not reported.
                        return Results.Json(
                            new { error = "The step stopped unexpectedly (" + error.GetType().Name + ")." },
                            statusCode: 502
                        );
                    }
                }
            );
            // The current choices for an action field that loads them (docs/adr/0037-loaded-action-choices.md). Read
            // only; a source that cannot be read gives no choices, and the form falls back to typing the value.
            app.MapGet(
                "/api/company/actions/choices",
                async (string employee, string action, string field, CancellationToken token) =>
                {
                    var owner = Store().EmployeeOf(employee);
                    var module = HostEmployees.Modules.Single(m => m.Manifest.Id == owner.RoleId);
                    var declared =
                        module
                            .Manifest.Actions.SingleOrDefault(a => a.Id == action)
                            ?.Fields.SingleOrDefault(f => f.Id == field && f.LoadsChoices)
                        ?? throw new WorkspaceException("This field has no choices to load.", 404);
                    ActionChoice[] choices;
                    try
                    {
                        choices = await module.ActionChoicesAsync(
                            action,
                            field,
                            Tools().For(module.Manifest, owner.Id).ReadOnly(),
                            token
                        );
                    }
                    catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException))
                    {
                        choices = [];
                    }
                    return Results.Ok(
                        choices
                            .Where(c => c.Value.Length is > 0 && c.Value.Length <= declared.MaxLength)
                            .Take(100)
                            .Select(c => c with { Label = ContentGuard.Bounded(c.Label, 120) })
                    );
                }
            );
            app.MapGet(
                "/api/company/feed",
                (long? after, int? limit) => Results.Ok(Store().Feed(after ?? 0, limit ?? 100))
            );
            app.MapGet(
                "/api/company/employees/{employee}/threads/{thread}",
                (string employee, string thread) => Results.Ok(Store().Thread(employee, thread))
            );
            app.MapGet("/api/company/tasks/{id}", (string id) => Results.Ok(Store().TaskDetail(id)));
            app.MapGet("/api/company/measured/{id}", (string id) => Results.Ok(Store().MeasuredDetail(id)));
            app.MapGet(
                "/api/company/goals/terms",
                (string roleId, string skill, string? modelRoute) =>
                    Results.Ok(Store().GoalTerms(roleId, skill, modelRoute))
            );
            app.MapGet(
                "/api/company/operations/{id}",
                (string id, HttpContext context) =>
                {
                    if (
                        Store().IsBillingOperation(id)
                        || Store().IsMemoryAnalysisOperation(id)
                        || Store().IsDecisionOperation(id)
                        || Store().IsTaskCostOperation(id)
                    )
                        CompanyAccess.Demand(
                            Store().Actor((string)context.Items["login"]!),
                            CompanyRoles.Owner,
                            "read owner operations"
                        );
                    context.Response.Headers.CacheControl = "no-store";
                    return Store().Operation(id) is { } result
                        ? Results.Content(result, "application/json")
                        : Results.NotFound();
                }
            );
            app.MapPost(
                "/api/company/commands",
                async (System.Text.Json.JsonElement body, HttpContext context) =>
                {
                    var command = CompanyJson.Decode<CompanyCommand>(body.GetRawText());
                    var subject = (string)context.Items["login"]!;
                    if (command.Action == "decision.run")
                    {
                        var runtime = Runtime();
                        CompanyAccess.Demand(
                            runtime.Store.Actor(subject),
                            CompanyRoles.Owner,
                            "run a reviewed decision"
                        );
                        var receipt = await runtime.Scheduler.RunDecisionOnceAsync(
                            command,
                            subject,
                            Model().SendDecisionAsync,
                            app.Lifetime.ApplicationStopping
                        );
                        CompanyAccess.Demand(
                            runtime.Store.Actor(subject),
                            CompanyRoles.Owner,
                            "read the decision outcome"
                        );
                        return Results.Content(receipt, "application/json");
                    }
                    if (command.Action is "billing.prepare" or "billing.checkout" && billing?.PaymentsEnabled != true)
                        throw new WorkspaceException(
                            "New subscriptions are not enabled. Existing billing records remain available.",
                            409
                        );
                    if (command.Action == "billing.portal" && billing is null)
                        throw new WorkspaceException(
                            "Billing account management is unavailable until its original provider is restored.",
                            409
                        );
                    if (command.Action == "measured.run")
                    {
                        CompanyAccess.Demand(
                            Store().Actor(subject),
                            CompanyAccess.Required(command.Action),
                            "run work"
                        );
                        if (!Model().HasMeasured)
                            throw new WorkspaceException("A pinned measured runtime is not configured.");
                        var receipt = await Runtime()
                            .Scheduler.RunMeasuredOnceAsync(command, Model(), app.Lifetime.ApplicationStopping);
                        return Results.Content(receipt, "application/json");
                    }
                    if (command.Action == "memory.analysis.run")
                    {
                        CompanyAccess.Demand(Store().Actor(subject), CompanyRoles.Owner, "check company memory");
                        if (!Model().HasMeasured)
                            throw new WorkspaceException("A measured model provider is not configured.", 409);
                        var receipt = await Runtime()
                            .Scheduler.RunMemoryAnalysisOnceAsync(
                                command,
                                subject,
                                Model(),
                                app.Lifetime.ApplicationStopping
                            );
                        CompanyAccess.Demand(Store().Actor(subject), CompanyRoles.Owner, "read the memory check");
                        return Results.Content(receipt, "application/json");
                    }
                    if (command.Action == "business.draft.run")
                    {
                        CompanyAccess.Demand(Store().Actor(subject), CompanyRoles.Owner, "draft the business brief");
                        if (!Model().HasMeasured)
                            throw new WorkspaceException("A measured model provider is not configured.", 409);
                        var receipt = await Runtime()
                            .Scheduler.RunBusinessDraftOnceAsync(
                                command,
                                subject,
                                Model(),
                                app.Lifetime.ApplicationStopping
                            );
                        return Results.Content(receipt, "application/json");
                    }
                    if (command.Action is "business.lookup.start" or "goal.lookup.start")
                    {
                        var receipt = await CompanyBusinessResearch.RunLookupAsync(
                            command,
                            subject,
                            Store(),
                            Tools(),
                            app.Lifetime.ApplicationStopping
                        );
                        return Results.Content(receipt, "application/json");
                    }
                    if (command.Action == "business.refresh.start")
                    {
                        var receipt = await CompanyBusinessResearch.RunRefreshAsync(
                            command,
                            subject,
                            Store(),
                            Tools(),
                            app.Lifetime.ApplicationStopping
                        );
                        return Results.Content(receipt, "application/json");
                    }
                    return Results.Content(Store().Apply(command, subject, out _), "application/json");
                }
            );
            if (hosted is not null)
                app.MapHostedSignIn();
            app.MapFallback(
                "/api/{**path}",
                () =>
                    Results.Json(
                        new { error = "This capability is unavailable in company-local mode." },
                        statusCode: 503
                    )
            );
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
            return app;
        }
        catch
        {
            ownership.Dispose();
            throw;
        }
    }
}
