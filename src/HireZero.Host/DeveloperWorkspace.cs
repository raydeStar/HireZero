using HireZero.Integrations.AzureDevOps;
using HireZero.Integrations.AgentApps;
using HireZero.Integrations.Microsoft365;
using HireZero.Integrations.Codex;
using HireZero.Integrations.Jev;
using HireZero.Integrations.OpenClaw;
using HireZero.Roles.Developer;

// The developer profile: the original single-agent workspace with roles hired through roles.json. It is retired once
// Developer runs on the employee module contract (docs/adr/0002-employee-module-contract.md).
public static class DeveloperWorkspace
{
    public static async Task<WebApplication> CreateAsync(string[] args, string ownerLogin, bool localOwnerEnabled, string dataRoot)
    {
        const string localHost = "127.0.0.1:5064";
        var database = Path.Combine(dataRoot, "workspace.sqlite");

        // Every role this build knows about; the catalog decides which are hired.
        IRole[] available = EmployeeRegistration.AvailableRoles();
        var roles = new RoleCatalog(dataRoot, available, defaultHired: ["developer"]);
        var hired = roles.Hired();

        var builder = WebApplication.CreateBuilder(args);
        // [::1] too: Microsoft sign-in returns to http://localhost, which browsers may resolve to IPv6 first.
        // Optional direct tailnet listener (HIREZERO_TAILNET_ADDRESS); callers there are identified by tailscale whois.
        var tailnet = TailnetAccess.FromEnvironment(5064);
        builder.Services.AddSingleton(new WorkspaceRuntime(ownerLogin, Path.GetFullPath(dataRoot), tailnet?.Address.ToString(), tailnet?.Hosts ?? [], localOwnerEnabled));
        builder.WebHost.UseUrls(tailnet is null ? ["http://" + localHost, "http://[::1]:5064"] : ["http://" + localHost, "http://[::1]:5064", "http://" + tailnet.Address + ":5064"]);
        if (tailnet is not null) builder.Services.AddSingleton(tailnet);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16_384);
        builder.Services.AddSingleton(roles);

        builder.Services.AddSingleton(new RequestStore(database));
        builder.Services.AddSingleton(new BacklogStore(database));
        builder.Services.AddSingleton<IBacklogSource>(new LocalRequestBacklog(database));
        builder.Services.AddSingleton<IBacklogSource>(new UnavailableBacklogSource("github-live", "GitHub history", 30, "No approved live history adapter. Saved observations are a separate local source."));
        builder.Services.AddSingleton<IBacklogSource>(new UnavailableBacklogSource("microsoft-history", "Microsoft conversations", 40, "No approved account or conversation scope selected."));
        builder.Services.AddSingleton<IBacklogSource>(new UnavailableBacklogSource("codex-history", "Codex session history", 50, "Session contents are not collected by this backlog increment."));
        builder.Services.AddSingleton<IBacklogSource>(new UnavailableBacklogSource("claude-history", "Claude session history", 60, "Session contents are not collected by this backlog increment."));
        builder.Services.AddSingleton<BacklogCoordinator>();
        builder.Services.AddHostedService<BacklogStartup>();
        builder.Services.AddSingleton(new OwnerTaskStore(database));
        builder.Services.AddSingleton<OwnerTaskBoard>();
        builder.Services.AddSingleton(new ShiftProposalStore(database));
        builder.Services.AddSingleton<IOwnerTaskContributor>(services => services.GetRequiredService<ShiftProposalStore>());
        builder.Services.AddSingleton<IShiftSensor, RecentWorkShiftSensor>();
        builder.Services.AddSingleton<IShiftSensor, InboxShiftSensor>();
        builder.Services.AddSingleton(new WorkspaceOwner(ownerLogin));
        builder.Services.AddSingleton(new AgentStore(database));
        builder.Services.AddSingleton(services => new BudgetLedger(database, services.GetRequiredService<RequestStore>(), ownerLogin));
        builder.Services.AddSingleton<ThreadTitler>();
        builder.Services.AddSingleton<HomeFeed>();
        builder.Services.AddHostedService<ThreadTitleBackfill>();
        builder.Services.AddSingleton<IAgentTaskHandler, ConversationReplyHandler>();
        builder.Services.AddSingleton<AgentWorker>();
        builder.Services.AddHostedService(services => services.GetRequiredService<AgentWorker>());
        builder.Services.AddHostedService<RequestReplyRecovery>();
        builder.Services.AddSingleton(services => new OpenClawCells(dataRoot, services.GetRequiredService<IHttpClientFactory>()));
        builder.Services.AddSingleton<IModelGateway>(services => services.GetRequiredService<OpenClawCells>());
        builder.Services.AddSingleton<IMemoryStore, OpenClawMemory>();
        builder.Services.AddSingleton<MemoryService>();
        builder.Services.AddSingleton(new SharingPolicy(dataRoot, ownerLogin, tailnet is null ? null : await tailnet.OwnDisplayNameAsync()));
        builder.Services.AddSingleton<WorkingContext>();
        builder.Services.AddSingleton<WorkLogService>();
        builder.Services.AddSingleton(new AccessStore(database));
        builder.Services.AddSingleton(services => new AccessDesk(services.GetRequiredService<RequestStore>(), services.GetRequiredService<AccessStore>(), services.GetRequiredService<WorkspaceOwner>(), dataRoot));
        builder.Services.AddSingleton(new TriageStore(database));
        builder.Services.AddSingleton<RequestTriageService>();
        builder.Services.AddSingleton(new MemorySuggestionStore(database));
        builder.Services.AddSingleton<MemorySources>();
        builder.Services.AddSingleton<MemorySuggestionService>();
        builder.Services.AddSingleton<IShiftSensor, MemorySuggestionSensor>();
        builder.Services.AddSingleton<IAgentTaskHandler, MemorySuggestionHandler>();
        builder.Services.AddSingleton<IBriefContributor, MemorySuggestionBrief>();
        // Large diagnoses (a PR with checks, logs, threads and files) can take a few minutes; the activity log shows the wait.
        builder.Services.AddHttpClient("openclaw", client => client.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient("openclaw-probe", client => client.Timeout = TimeSpan.FromMilliseconds(400));
        builder.Services.AddWorkspaceRateLimits();
        builder.Services.AddSingleton(new IntakeStore(database));
        builder.Services.AddSingleton<IIntakeRule>(new PatternIntakeRule(IntakeRuleSet.Load(dataRoot)));
        builder.Services.AddSingleton<IntakePipeline>();
        builder.Services.AddJev();
        builder.Services.AddCodex(database);
        builder.Services.AddMicrosoft365(dataRoot);
        builder.Services.AddSingleton(new SessionStore(database));
        builder.Services.AddSingleton(new HookToken(dataRoot));
        builder.Services.AddSingleton<SessionWatcher>();
        builder.Services.AddHostedService(services => services.GetRequiredService<SessionWatcher>());
        builder.Services.AddKeyedSingleton<ISessionSource>("codex-preferred", (services, _) => new OpenClawCodexSessions(services.GetRequiredService<OpenClawCells>()));
        builder.Services.AddAgentApps();
        builder.Services.AddAzureDevOps();
        builder.Services.AddSingleton<SessionSteering>();
        builder.Services.AddSingleton(new DigestStore(database));
        builder.Services.AddSingleton(services => ActivatorUtilities.CreateInstance<BriefService>(services, dataRoot));
        var context = new RoleContext(dataRoot, database);
        foreach (var role in hired) role.AddServices(builder.Services, context);

        var app = builder.Build();
        app.Services.GetRequiredService<HomeFeed>().Attach();
        UsageMeter.Ledger = app.Services.GetRequiredService<BudgetLedger>();
        var model = app.Services.GetRequiredService<IModelGateway>();
        model.Prepare(IModelGateway.OwnerCell);
        app.Lifetime.ApplicationStopping.Register(() => (model as IDisposable)?.Dispose());
        app.UseWorkspaceCorrelation();
        app.UseWorkspaceAccess(ownerLogin, localOwnerEnabled, localHost);
        app.UseRateLimiter();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapCoreEndpoints(ownerLogin, localOwnerEnabled);

        app.MapBudgetEndpoints();
        app.MapAgentRequests();
        app.MapBacklogEndpoints();
        app.MapAzureDevOps();
        app.MapMicrosoft365();
        foreach (var role in hired) { role.MapEndpoints(app); role.Start(app.Services); }
        app.MapFallback("/api/{**path}", () => Results.NotFound());
        app.MapFallbackToFile("index.html");
        return app;
    }
}
