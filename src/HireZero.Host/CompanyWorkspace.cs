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
        ICompanyCodingFactory? fixtureCoding = null
    )
    {
        if (!args.Contains(Flag))
            return null;
        if (args.Contains(CodingFlag) && !args.Contains(CodexFlag) && fixtureCoding is null)
            throw new InvalidOperationException(
                "Live company coding needs the Codex connection profile (--company-codex)."
            );
        if (!localOwner)
            throw new InvalidOperationException("Company local mode requires explicit local owner mode.");
        if (fixtureUrl is null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIREZERO_DATA_ROOT")))
            throw new InvalidOperationException("Select an explicit new HIREZERO_DATA_ROOT for company-local mode.");
        if (HeldWorkspace.Requested(Environment.GetEnvironmentVariable(HeldWorkspace.Variable)))
            throw new InvalidOperationException(
                "Execution hold takes precedence. Use the held profile or explicitly select company-local with a separate data root."
            );
        var url = fixtureUrl ?? "http://127.0.0.1:5065";
        var uri = LocalHosting.LoopbackUri(url, "Company mode must use a loopback listener.");
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args.Where(a => a != Flag && a != CodexFlag && a != CodingFlag).ToArray(),
                WebRootPath = fixtureUrl is not null ? fixtureWebRoot : null,
            }
        );
        builder.ListenLoopbackOnly(uri, uri.Port, 16_384);
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "workspace.sqlite");
        if (File.Exists(database))
        {
            using var check = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = database,
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
                Environment.GetEnvironmentVariable("HIREZERO_COMPANY_TEXT_MODEL")
                    ?? ModelRoutes.Route(ModelRoutes.CompanyText),
                Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODING_MODEL")
                    ?? ModelRoutes.Route(ModelRoutes.CompanyCoding)
            );
            routing.Validate();
            CompanyCodexOptions? codex =
                fixture is null && args.Contains(CodexFlag)
                    ? new(
                        Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_EXECUTABLE")
                            ?? throw new InvalidOperationException("Pin the selected Codex executable."),
                        Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_IDENTITY")
                            ?? throw new InvalidOperationException(
                                "Pin the selected Codex executable bundle identity."
                            ),
                        Path.Combine(root, "codex-scratch"),
                        Home: Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_HOME")
                            ?? throw new InvalidOperationException("Select the dedicated native Codex profile home.")
                    )
                    : null;
            IModelProvider model =
                fixture
                ?? (codex is not null ? new CompanyCodexConnection(codex, routing) : new UnavailableModelProvider());
            var coding = fixtureCoding;
            if (coding is null && codex is not null && args.Contains(CodingFlag))
            {
                var live = new CompanyCodexCodingFactory(
                    codex,
                    routing,
                    (Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODING_READ") ?? "").Split(
                        ';',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                );
                coding = live.Qualified
                    ? live
                    : throw new InvalidOperationException("Live company coding is not qualified: " + live.Refusal);
            }
            var store = new CompanyStore(
                Path.Combine(root, "workspace.sqlite"),
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
            }
            builder.Services.AddSingleton(tools);
            builder.Services.AddSingleton<IModelProvider>(model);
            builder.Services.AddSingleton<CompanyScheduler>();
            builder.Services.AddHostedService(s => s.GetRequiredService<CompanyScheduler>());
            // The synthetic Wiki pilot, only when it is switched on (docs/adr/0015-publishing-an-approved-artifact.md).
            var wiki = fixtureTools is null && Environment.GetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI") == "1";
            // Microsoft 365: owner-started reads only, no polling; its tools register once a client ID is saved.
            builder.Services.AddMicrosoft365Company(root);
            var app = builder.Build();
            var microsoft = app.Services.GetRequiredService<Microsoft365ToolProvider>();
            void Microsoft()
            {
                if (fixtureTools is null)
                    microsoft.RegisterOnce(tools);
            }
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
            app.Lifetime.ApplicationStopped.Register(ownership.Dispose);
            app.UseWorkspaceCorrelation();
            app.Use(
                async (c, next) =>
                {
                    LocalHosting.ApplySecurityHeaders(c);
                    // Fixture port zero is bound by Kestrel, but Host still proves the chosen profile's authority.
                    var expected = fixtureUrl is null || uri.Port != 0 ? uri.Authority : "127.0.0.1:5065";
                    if (!LocalHosting.IsDirectLocalRequest(c, expected))
                    {
                        c.Response.StatusCode = 403;
                        return;
                    }
                    if (
                        c.Request.Method is not ("GET" or "HEAD")
                        && (
                            c.Request.Headers.Origin != "http://" + expected
                            || c.Request.Headers["X-Dev-Request"] != "1"
                            || !c.Request.HasJsonContentType()
                        )
                    )
                    {
                        c.Response.StatusCode = 403;
                        return;
                    }
                    c.Items["login"] = owner;
                    c.Items["role"] = "owner";
                    try
                    {
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
                () =>
                    Results.Ok(
                        new
                        {
                            login = owner,
                            role = "owner",
                            accessMode = "local",
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
                        tools.Has("git.read"),
                        tools.Has("git.read") ? "Git is available." : "Git was not found."
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
                                providerStatus = model.Status,
                                modelRouting = routing,
                                ceoEnabled = true,
                                meetingsEnabled = true,
                                legacyDeveloperAvailable = false,
                                integrations = Integrations(),
                                tools = tools.Contracts.Select(c => new
                                {
                                    c.Id,
                                    risk = c.Risk.ToString().ToLowerInvariant(),
                                    c.Description,
                                }),
                            },
                            data = store.Dashboard(),
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
            app.MapGet("/api/company/tool-calls", (int? limit) => Results.Ok(store.ToolCalls(limit ?? 100)));
            // Runs a declared employee action once (docs/adr/0011-employee-actions.md). A retried request returns the
            // recorded task without running the action again.
            app.MapPost(
                "/api/company/actions",
                async (System.Text.Json.JsonElement body, CancellationToken token) =>
                {
                    var command = CompanyJson.Decode<CompanyCommand>(body.GetRawText()) with
                    {
                        Action = "action.start",
                    };
                    var task = CompanyJson.Decode<CompanyTask>(store.Apply(command, out var fresh));
                    if (!fresh)
                        return Results.Ok(store.TaskDetail(task.Id));
                    var employee = store.EmployeeOf(task.EmployeeId);
                    var module = HostEmployees.Modules.Single(m => m.Manifest.Id == employee.RoleId);
                    var input = CompanyJson.Decode<Dictionary<string, string>>(task.Input);
                    try
                    {
                        var output = await module.RunActionAsync(
                            task.Skill,
                            input,
                            tools.For(module.Manifest, employee.Id, task.Id).ReadOnly(),
                            token
                        );
                        store.CompleteAction(task.Id, output);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        // Expected refusals keep their message; anything else is reported without internals.
                        store.FailAction(
                            task.Id,
                            error is WorkspaceException or ModelUnavailableException
                                ? error.Message
                                : "The action failed unexpectedly (" + error.GetType().Name + ")."
                        );
                    }
                    return Results.Ok(store.TaskDetail(task.Id));
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
                        ExecutionStep.Prepare => store.Work(id).EmployeeId,
                        ExecutionStep.Run => store.Execution(id).EmployeeId,
                        ExecutionStep.Propose => store.FindExecution(id)?.EmployeeId ?? store.Artifact(id).EmployeeId,
                        _ => store.Publication(id).EmployeeId,
                    };
                    var employee = store.EmployeeOf(employeeId);
                    var module = HostEmployees.Modules.Single(m => m.Manifest.Id == employee.RoleId);
                    try
                    {
                        return Results.Ok(await module.ExecuteAsync(parsed, id, new(store, tools, coding), token));
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
            app.MapGet(
                "/api/company/feed",
                (long? after, int? limit) => Results.Ok(store.Feed(after ?? 0, limit ?? 100))
            );
            app.MapGet(
                "/api/company/employees/{employee}/threads/{thread}",
                (string employee, string thread) => Results.Ok(store.Thread(employee, thread))
            );
            app.MapGet("/api/company/tasks/{id}", (string id) => Results.Ok(store.TaskDetail(id)));
            app.MapGet("/api/company/measured/{id}", (string id) => Results.Ok(store.MeasuredDetail(id)));
            app.MapGet(
                "/api/company/operations/{id}",
                (string id) =>
                    store.Operation(id) is { } result ? Results.Content(result, "application/json") : Results.NotFound()
            );
            app.MapPost(
                "/api/company/commands",
                async (System.Text.Json.JsonElement body) =>
                {
                    var command = CompanyJson.Decode<CompanyCommand>(body.GetRawText());
                    if (command.Action == "measured.run")
                    {
                        if (model is not IMeasuredModelProvider measured)
                            throw new WorkspaceException("A pinned measured runtime is not configured.");
                        var receipt = await app
                            .Services.GetRequiredService<CompanyScheduler>()
                            .RunMeasuredOnceAsync(command, measured, app.Lifetime.ApplicationStopping);
                        return Results.Content(receipt, "application/json");
                    }
                    return Results.Content(store.Apply(command), "application/json");
                }
            );
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
