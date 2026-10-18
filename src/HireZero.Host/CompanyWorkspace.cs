using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Integrations.Git;
using HireZero.Integrations.GitHub;

// Explicit composition profile. Native Codex inspection is opt-in; legacy workers are never registered.
public static class CompanyWorkspace
{
    public const string Flag = "--company-local";
    public const string CodexFlag = "--company-codex";

    public static CompanyRole[] Definitions() => [.. HostEmployees.Modules.Select(CompanyRole.From)];

    public static WebApplication? TryCreate(
        string[] args,
        string owner,
        bool localOwner,
        string root,
        IModelProvider? fixture = null,
        string? fixtureUrl = null,
        string? fixtureWebRoot = null,
        Action<ToolRegistry>? fixtureTools = null
    )
    {
        if (!args.Contains(Flag))
            return null;
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
                Args = args.Where(a => a != Flag && a != CodexFlag).ToArray(),
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
            IModelProvider model =
                fixture
                ?? (
                    args.Contains(CodexFlag)
                        ? new CompanyCodexConnection(
                            new(
                                Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_EXECUTABLE")
                                    ?? throw new InvalidOperationException("Pin the selected Codex executable."),
                                Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_IDENTITY")
                                    ?? throw new InvalidOperationException(
                                        "Pin the selected Codex executable bundle identity."
                                    ),
                                Path.Combine(root, "codex-scratch"),
                                Home: Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_HOME")
                                    ?? throw new InvalidOperationException(
                                        "Select the dedicated native Codex profile home."
                                    )
                            ),
                            routing
                        )
                        : new UnavailableModelProvider()
                );
            var store = new CompanyStore(
                Path.Combine(root, "workspace.sqlite"),
                owner,
                Definitions(),
                routing,
                (model as IMeasuredModelProvider)?.RuntimeIdentity
            );
            builder.Services.AddSingleton(store);
            // Tools employees may call, audited in the company store (docs/adr/0007-tool-registry.md).
            var tools = new ToolRegistry(store);
            if (fixtureTools is not null)
                fixtureTools(tools);
            else
            {
                if (GitToolRegistration.FindGit() is { } git)
                    tools.AddGitTools(new GitCheckoutInspector(git));
                // Read-only GitHub CLI tools for employee actions; there are no verifiers, so every write is refused.
                new GitHubCli().Register(tools);
            }
            builder.Services.AddSingleton(tools);
            builder.Services.AddSingleton<IModelProvider>(model);
            builder.Services.AddSingleton<CompanyScheduler>();
            builder.Services.AddHostedService(s => s.GetRequiredService<CompanyScheduler>());
            var app = builder.Build();
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
                                measuredOneJob = model is IMeasuredModelProvider,
                                conversationalReplies = model is IMeasuredModelProvider,
                                providerStatus = model.Status,
                                modelRouting = routing,
                                ceoEnabled = true,
                                meetingsEnabled = true,
                                legacyDeveloperAvailable = false,
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
