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
    public const string Flag = "--company-local";
    public const string CodexFlag = "--company-codex";

    // Live company coding on the pinned Codex runtime (docs/adr/0014-pinned-codex-runtime.md). Off unless asked for,
    // and only together with the Codex connection profile.
    public const string CodingFlag = "--company-coding";

    // The least role a change through a route needs; commands carry their own check in the store.
    internal static string? RouteRole(PathString path) =>
        path.StartsWithSegments("/api/company/commands") ? null
        : path.StartsWithSegments("/api/company/actions") || path.StartsWithSegments("/api/company/executions")
            ? CompanyAccess.Steps
        : CompanyRoles.Owner;

    public static CompanyRole[] Definitions() => [.. HostEmployees.Modules.Select(CompanyRole.From)];

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
        CompanySettings? settings = null
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
        var url = hosted?.Listen.ToString() ?? fixtureUrl ?? "http://127.0.0.1:5065";
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
            );
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
            IModelProvider model =
                fixture
                ?? (
                    codex is not null ? new CompanyCodexConnection(codex, routing)
                    // The pinned Codex CLI stays the model when it is selected; an API model only on explicit opt-in
                    // (docs/adr/0036-api-model-provider.md).
                    : settings.ApiModels ? new HireZero.Integrations.ModelApis.ApiModelProvider()
                    : new UnavailableModelProvider()
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
            var store = new CompanyStore(
                database,
                owner,
                Definitions(),
                routing,
                (model as IMeasuredModelProvider)?.RuntimeIdentity
            );
            builder.Services.AddSingleton(store);
            // Tools employees may call, audited in the company store (docs/adr/0007-tool-registry.md).
            // The only verifier is the publication's: a write or push runs only as a call of an approved
            // publication (docs/adr/0012-company-publication.md); every other write is refused.
            var tools = new ToolRegistry(store, [store.PublicationAuthority]);
            if (fixtureTools is not null)
                fixtureTools(tools);
            else
            {
                if (GitToolRegistration.FindGit() is { } git)
                    tools.AddGitTools(new GitCheckoutInspector(git));
                new GitHubCli().Register(tools);
                // Owner-started source reads for context drafts (docs/adr/0041-context-drafted-from-sources.md).
                new HireZero.Integrations.Web.WebReader().Register(tools);
                // The trusted PostgreSQL toolchain: installed only as an owner-approved publication.
                new HireZero.Integrations.Node.NpmInstaller().Register(tools);
                new HireZero.Integrations.Node.TrustedPostgresToolchain(root).Register(tools);
                // Its per-repository test profiles, in company storage; saved only as an owner-approved publication.
                new HireZero.Integrations.Node.TrustedPostgresProfiles(file).Register(tools);
            }
            builder.Services.AddSingleton(tools);
            builder.Services.AddSingleton<IModelProvider>(model);
            builder.Services.AddSingleton<CompanyScheduler>();
            builder.Services.AddHostedService(s => s.GetRequiredService<CompanyScheduler>());
            // The synthetic Wiki pilot, only when it is switched on (docs/adr/0015-publishing-an-approved-artifact.md).
            var wiki = fixtureTools is null && settings.SyntheticWiki;
            // Microsoft 365: owner-started reads only, no polling; its tools register once a client ID is saved.
            builder.Services.AddSingleton(global::Microsoft.Extensions.Options.Options.Create(settings));
            builder.Services.AddCompanyKeyProtection(root);
            builder.Services.AddMicrosoft365Company(root);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddCompanyTelemetry(CompanyTelemetryHosting.Enabled);
            var membership = new CompanyMembership();
            builder.Services.AddSingleton(membership);
            var app = builder.Build();
            // Local mode: secrets the owner pastes in the app, read last, before any integration checks its secrets
            // (docs/adr/0038-integration-setup-in-the-app.md). Hosted mode takes secrets only from its vault or platform.
            var pastedSecrets = hosted is null
                ? new LocalSecretStore(
                    root,
                    app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
                    LocalSecretStore.Names
                )
                : null;
            if (pastedSecrets is not null)
                SecretSource.UseAppSecrets(pastedSecrets.Read);
            foreach (var (name, source) in settings.Sources.OrderBy(s => s.Key, StringComparer.Ordinal))
                app.Logger.LogInformation("Setting {Name}: {Source}", name, source);
            foreach (var line in CompanySecrets.Report())
                app.Logger.LogInformation("{Secret}", line);
            if (fixtureDatabase is null && database is IDisposable owned)
                app.Lifetime.ApplicationStopped.Register(owned.Dispose);
            var microsoft = app.Services.GetRequiredService<Microsoft365ToolProvider>();
            System.Collections.Concurrent.ConcurrentDictionary<string, CompanyRuntime>? openRuntimes = null;
            void Microsoft()
            {
                if (fixtureTools is not null)
                    return;
                microsoft.RegisterOnce(tools);
                foreach (var runtime in RuntimesOpen())
                    microsoft.RegisterOnce(runtime.Tools);
            }
            IEnumerable<CompanyRuntime> RuntimesOpen() => openRuntimes?.Values ?? [];
            if (app.Services.GetRequiredService<MicrosoftStore>().Settings().ClientId is { Length: > 0 })
                Microsoft();
            if (wiki)
                WikiRegistration.CompanyProvider().Register(tools);
            // Coding-agent session listing, only for the apps the owner lists in agent-apps.json.
            var agentApps = AgentAppsToolProvider.Company(root);
            if (fixtureTools is null && agentApps.Status().Active)
                agentApps.Register(tools);
            // Azure DevOps, only for the organizations the owner lists in azure-devops.json; read-only, no watch.
            var azureDevOps = new AzureDevOpsToolProvider(root);
            if (fixtureTools is null && azureDevOps.Status().Active)
                azureDevOps.Register(tools);
            // Jev, only when its key is set; text is sent to it only when the owner starts a Jev action.
            var jev = JevToolProvider.Company();
            if (fixtureTools is null && jev.Status().Active)
                jev.Register(tools);
            // OpenRouter, only when its key and the owner's chosen model are set; called only from owner-started sorts.
            var openRouter = new OpenRouterToolProvider(root);
            if (fixtureTools is null && openRouter.Status().Active)
                openRouter.Register(tools);
            // Analytics for Marketing (docs/adr/0029-marketing-analytics-reads.md): Google when google.json and its key
            // are set, otherwise the fictional stand-in when it is switched on. Read-only, owner-started.
            var google = new HireZero.Integrations.Google.GoogleAnalyticsProvider(root);
            var syntheticAnalytics = settings.SyntheticAnalytics
                ? new HireZero.Integrations.Synthetic.SyntheticAnalyticsProvider()
                : null;
            IToolProvider? analytics = google.Status().Active ? google : syntheticAnalytics;
            if (fixtureTools is null)
                analytics?.Register(tools);
            // Ads for Marketing (docs/adr/0031-ads-read-contracts.md, docs/adr/0032-google-ads-provider.md): Google Ads
            // when google-ads.json and its secrets are set, otherwise the fictional stand-in when it is switched on.
            var googleAds = new HireZero.Integrations.Google.GoogleAdsProvider(root);
            var syntheticAds = settings.SyntheticAds
                ? new HireZero.Integrations.Synthetic.SyntheticAdsProvider()
                : null;
            IToolProvider? ads = googleAds.Status().Active ? googleAds : syntheticAds;
            if (fixtureTools is null)
                ads?.Register(tools);
            // Email and CRM for Marketing (docs/adr/0035-email-and-crm-read-contracts.md): aggregates only, served for now
            // by the fictional stand-ins when they are switched on.
            var syntheticEmail = settings.SyntheticEmail
                ? new HireZero.Integrations.Synthetic.SyntheticEmailProvider()
                : null;
            var syntheticCrm = settings.SyntheticCrm
                ? new HireZero.Integrations.Synthetic.SyntheticCrmProvider()
                : null;
            if (fixtureTools is null)
            {
                syntheticEmail?.Register(tools);
                syntheticCrm?.Register(tools);
            }
            // Every company in this data root runs with its own store, tool registry and scheduler over the same
            // database (docs/adr/0021-companies-per-data-root.md). The first company's runtime is the one above.
            void RegisterTools(ToolRegistry registry)
            {
                if (fixtureTools is not null)
                {
                    fixtureTools(registry);
                    return;
                }
                if (GitToolRegistration.FindGit() is { } found)
                    registry.AddGitTools(new GitCheckoutInspector(found));
                new GitHubCli().Register(registry);
                // Owner-started source reads for context drafts (docs/adr/0041-context-drafted-from-sources.md).
                new HireZero.Integrations.Web.WebReader().Register(registry);
                new HireZero.Integrations.Node.NpmInstaller().Register(registry);
                new HireZero.Integrations.Node.TrustedPostgresToolchain(root).Register(registry);
                if (app.Services.GetRequiredService<MicrosoftStore>().Settings().ClientId is { Length: > 0 })
                    microsoft.RegisterOnce(registry);
                if (wiki)
                    WikiRegistration.CompanyProvider().Register(registry);
                if (agentApps.Status().Active)
                    agentApps.Register(registry);
                if (azureDevOps.Status().Active)
                    azureDevOps.Register(registry);
                if (jev.Status().Active)
                    jev.Register(registry);
                if (openRouter.Status().Active)
                    openRouter.Register(registry);
                analytics?.Register(registry);
                ads?.Register(registry);
                syntheticEmail?.Register(registry);
                syntheticCrm?.Register(registry);
            }
            var runtimes = new System.Collections.Concurrent.ConcurrentDictionary<string, CompanyRuntime>(
                StringComparer.Ordinal
            );
            runtimes[store.CompanyId] = new(store, tools, app.Services.GetRequiredService<CompanyScheduler>());
            openRuntimes = runtimes;
            CompanyRuntime Open(string id) =>
                runtimes.GetOrAdd(
                    id,
                    key =>
                    {
                        var listing =
                            CompanyStore.Companies(database).SingleOrDefault(c => c.Id == key)
                            ?? throw new WorkspaceException("No such company.", 404);
                        var companyStore = new CompanyStore(
                            database,
                            listing.Creator,
                            Definitions(),
                            routing,
                            (model as IMeasuredModelProvider)?.RuntimeIdentity,
                            key
                        );
                        var registry = new ToolRegistry(companyStore, [companyStore.PublicationAuthority]);
                        RegisterTools(registry);
                        var scheduler = new CompanyScheduler(companyStore, model);
                        _ = scheduler.StartAsync(app.Lifetime.ApplicationStopping);
                        app.Lifetime.ApplicationStopping.Register(() =>
                            scheduler.StopAsync(CancellationToken.None).Wait()
                        );
                        return new(companyStore, registry, scheduler);
                    }
                );
            // Hosted sign-in binds a waiting invitation in whichever company issued it.
            membership.SignIn = (subject, email, verified, name) =>
                CompanyStore
                    .Companies(database)
                    .Select(c => Open(c.Id).Store.SignIn(subject, email, verified, name))
                    .ToArray()
                    .FirstOrDefault(a => a is not null);
            var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
            CompanyRuntime Runtime() =>
                accessor.HttpContext?.Items["company"] as CompanyRuntime ?? runtimes[CompanyStore.DefaultCompanyId];
            CompanyStore Store() => Runtime().Store;
            ToolRegistry Tools() => Runtime().Tools;
            app.Lifetime.ApplicationStopped.Register(ownership.Dispose);
            app.UseWorkspaceCorrelation();
            if (hosted is not null)
                app.UseHostedSignIn(hosted);
            app.Use(
                async (c, next) =>
                {
                    LocalHosting.ApplySecurityHeaders(c);
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
                        )
                        {
                            c.Response.StatusCode = 401;
                            await c.Response.WriteAsJsonAsync(new { error = "Sign in first.", signIn = "/signin" });
                            return;
                        }
                        c.Items["login"] = subject;
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
                            var company = c.Request.Headers["X-HireZero-Company"].FirstOrDefault();
                            var runtime = Open(string.IsNullOrEmpty(company) ? CompanyStore.DefaultCompanyId : company);
                            c.Items["company"] = runtime;
                            System.Diagnostics.Activity.Current?.SetTag("hirezero.company", runtime.Store.CompanyId);
                            var actor = runtime.Store.Actor((string)c.Items["login"]!);
                            c.Items["role"] = actor.Role;
                            if (c.Request.Method is not ("GET" or "HEAD") && RouteRole(c.Request.Path) is { } required)
                                CompanyAccess.Demand(actor, required, "change " + c.Request.Path);
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
            app.MapGet(
                "/health",
                () =>
                    Results.Ok(
                        new
                        {
                            status = "healthy",
                            companyLocal = true,
                            modelExecution = model.Qualified,
                            liveCoding = coding?.Qualified == true,
                        }
                    )
            );
            app.MapGet(
                "/api/session",
                (HttpContext context) =>
                    Results.Ok(
                        new
                        {
                            login = (string?)context.Items["login"],
                            role = (string?)context.Items["role"],
                            signedIn = context.Items["login"] is string,
                            accessMode = hosted is null ? "local" : "hosted",
                            roles = Array.Empty<string>(),
                            agentModel = model is IMeasuredModelProvider
                                ? CompanyModelRouting.Native(routing.TextModel) + " (manual only)"
                                : "Unavailable",
                            agentReasoning = "",
                            companyEnabled = true,
                            executionHeld = false,
                        }
                    )
            );
            // Every optional integration and why it is or is not active (docs/adr/0009).
            IntegrationStatus[] Integrations() =>
                [
                    microsoft.Status(),
                    agentApps.Status(),
                    azureDevOps.Status(),
                    jev.Status(),
                    openRouter.Status(),
                    google.Status(),
                    syntheticAnalytics?.Status()
                        ?? new(
                            "synthetic-analytics",
                            "Synthetic analytics",
                            false,
                            "Set HIREZERO_SYNTHETIC_ANALYTICS=1 for fictional analytics data."
                        ),
                    googleAds.Status(),
                    syntheticEmail?.Status()
                        ?? new(
                            "synthetic-email",
                            "Synthetic email",
                            false,
                            "Set HIREZERO_SYNTHETIC_EMAIL=1 for fictional email campaign results."
                        ),
                    syntheticCrm?.Status()
                        ?? new(
                            "synthetic-crm",
                            "Synthetic CRM",
                            false,
                            "Set HIREZERO_SYNTHETIC_CRM=1 for a fictional sales pipeline."
                        ),
                    syntheticAds?.Status()
                        ?? new(
                            "synthetic-ads",
                            "Synthetic ads",
                            false,
                            "Set HIREZERO_SYNTHETIC_ADS=1 for fictional ad data."
                        ),
                    new(
                        "wiki",
                        "Synthetic Wiki",
                        wiki,
                        wiki
                            ? "The synthetic Wiki pilot is switched on."
                            : "Set HIREZERO_SYNTHETIC_WIKI=1 to use the Wiki pilot."
                    ),
                    new(
                        "git",
                        "Git",
                        Tools().Has("git.read"),
                        Tools().Has("git.read") ? "Git is available." : "Git was not found."
                    ),
                    new(
                        "live-coding",
                        "Live coding",
                        coding?.Qualified == true,
                        coding?.Qualified == true ? "A qualified coding runner is configured."
                            : coding is CompanyCodexCodingFactory { Refusal: { } why } ? why
                            : "Start with -Coding and the pinned Codex runtime to enable live coding."
                    ),
                ];
            app.MapMicrosoft365(Microsoft);
            app.MapGet(
                "/api/company",
                () =>
                    Results.Ok(
                        new
                        {
                            capabilities = new
                            {
                                localManagement = true,
                                modelExecution = model.Qualified,
                                liveCoding = coding?.Qualified == true,
                                measuredOneJob = model is IMeasuredModelProvider,
                                conversationalReplies = model is IMeasuredModelProvider,
                                allowanceUnit = model is HireZero.Integrations.ModelApis.ApiModelProvider
                                    ? "usd-cents"
                                    : "credits",
                                providerStatus = model.Status,
                                modelRouting = routing,
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
                    model is CompanyCodexConnection connection
                        ? Results.Ok(await connection.InspectAsync(token))
                        : Results.Json(new { error = "Select the explicit Codex connection profile." }, statusCode: 503)
            );
            // Setting up integrations in the app (docs/adr/0038-integration-setup-in-the-app.md).
            // After a setup change, every integration that is now active starts working without a restart, in every
            // open company (docs/adr/0039-integrations-start-without-a-restart.md).
            void Activate()
            {
                if (google.Status().Active)
                    analytics = google;
                if (googleAds.Status().Active)
                    ads = googleAds;
                foreach (var registry in RuntimesOpen().Select(r => r.Tools).Append(tools).Distinct())
                {
                    if (agentApps.Status().Active)
                        registry.Adopt(agentApps);
                    if (azureDevOps.Status().Active)
                        registry.Adopt(azureDevOps);
                    if (openRouter.Status().Active)
                        registry.Adopt(openRouter);
                    if (analytics == google && google.Status().Active)
                        registry.Adopt(google, syntheticAnalytics);
                    if (ads == googleAds && googleAds.Status().Active)
                        registry.Adopt(googleAds, syntheticAds);
                }
            }
            app.MapIntegrationSetup(
                [google, googleAds, azureDevOps, agentApps, openRouter],
                Integrations,
                root,
                pastedSecrets,
                Store,
                Activate
            );
            app.MapContextSources(Store, Tools);
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
                    var mine = new List<object>();
                    foreach (var listing in CompanyStore.Companies(database))
                    {
                        var companyStore = Open(listing.Id).Store;
                        try
                        {
                            mine.Add(
                                new
                                {
                                    id = listing.Id,
                                    name = companyStore.Name(),
                                    role = companyStore.Actor(subject).Role,
                                }
                            );
                        }
                        catch (WorkspaceException) { }
                    }
                    return Results.Ok(mine);
                }
            );
            app.MapPost(
                "/api/companies",
                (CompanyName body, HttpContext context) =>
                {
                    var subject = (string)context.Items["login"]!;
                    var id = CompanyStore.NewCompanyId();
                    var created = new CompanyStore(
                        database,
                        subject,
                        Definitions(),
                        routing,
                        (model as IMeasuredModelProvider)?.RuntimeIdentity,
                        id
                    );
                    created.Rename(body.Name ?? "");
                    Open(id);
                    return Results.Ok(
                        new
                        {
                            id,
                            name = created.Name(),
                            role = CompanyRoles.Owner,
                        }
                    );
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
            // GitHub sign-in through the GitHub CLI's own browser flow (ported from the legacy profile, 2B.5j). The
            // owner's click starts it on this computer; HireZero never sees a password or token.
            // A fixture never reads this computer's real GitHub sign-in: its pages and screenshots stay fictional.
            var gitHubSignIn = fixtureUrl is not null ? new GitHubSignIn(() => null) : new GitHubSignIn();
            app.MapGet("/api/company/github/sign-in", () => Results.Ok(gitHubSignIn.Status()));
            app.MapPost("/api/company/github/sign-in", () => Results.Ok(gitHubSignIn.Start()));
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
                                    : "The action failed unexpectedly (" + error.GetType().Name + ")."
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
                        return Results.Ok(await module.ExecuteAsync(parsed, id, new(Store(), Tools(), coding), token));
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
                "/api/company/operations/{id}",
                (string id) =>
                    Store().Operation(id) is { } result
                        ? Results.Content(result, "application/json")
                        : Results.NotFound()
            );
            app.MapPost(
                "/api/company/commands",
                async (System.Text.Json.JsonElement body, HttpContext context) =>
                {
                    var command = CompanyJson.Decode<CompanyCommand>(body.GetRawText());
                    var subject = (string)context.Items["login"]!;
                    if (command.Action == "measured.run")
                    {
                        CompanyAccess.Demand(
                            Store().Actor(subject),
                            CompanyAccess.Required(command.Action),
                            "run work"
                        );
                        if (model is not IMeasuredModelProvider measured)
                            throw new WorkspaceException("A pinned measured runtime is not configured.");
                        var receipt = await Runtime()
                            .Scheduler.RunMeasuredOnceAsync(command, measured, app.Lifetime.ApplicationStopping);
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
