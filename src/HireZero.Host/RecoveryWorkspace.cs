using System.Net;
using HireZero.Core;
using HireZero.Integrations.OpenClaw;
using HireZero.Roles.Developer;

public static class RecoveryWorkspace
{
    public static WebApplication? TryCreate(string[] args, string owner, bool localOwner, string dataRoot,
        GitHubReviewClient? fixtureGitHub = null, IModelGateway? fixtureModel = null, string? fixtureUrl = null, IEmployeeModelGateway? fixtureEmployeeModel = null)
    {
        if (Environment.GetEnvironmentVariable(DiscoveryWorkspace.Variable) is not ("1")) return null;
        if (!localOwner) throw new InvalidOperationException("Recovery requires local owner access.");
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false); options.Limits.MaxRequestBodySize = 16_384;
            if (fixtureUrl is not null)
            {
                var fixture = new Uri(fixtureUrl);
                if (fixture.Scheme != "http" || !IPAddress.TryParse(fixture.Host, out var address) || !IPAddress.IsLoopback(address)) throw new InvalidOperationException("Fixture must be loopback.");
                options.Listen(address, fixture.Port);
            }
            else { options.Listen(IPAddress.Loopback, 5064); options.Listen(IPAddress.IPv6Loopback, 5064); }
        });
        var database = Path.Combine(dataRoot, "workspace.sqlite");
        var roles = new RoleCatalog(dataRoot, EmployeeRegistration.AvailableRoles(), ["developer"]);

        var requests = new RequestStore(database) { OwnerLogin = owner }; var agents = new AgentStore(database);
        _ = new HireZero.Integrations.Codex.CodingRunStore(database); // Schema only; recovery registers no coding runner or coding handler.
        var discovery = new GitHubDiscovery(database, fixtureGitHub ?? new GitHubReviewClient(), new GitHubReviewStore(database));
        var model = fixtureModel ?? new TextOnlyOpenClaw(dataRoot);
        var replies = new FreshOwnerReplies(requests, agents, model, owner);
        builder.Services.AddSingleton(requests); builder.Services.AddSingleton(agents); builder.Services.AddSingleton(discovery);
        builder.Services.AddSingleton(replies); builder.Services.AddSingleton<IModelGateway>(model);
        builder.Services.AddSingleton(services => new AgentWorker(agents, [], [replies], services.GetRequiredService<ILogger<AgentWorker>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<AgentWorker>());
        builder.Services.AddHostedService<GitHubDiscoveryWorker>();
        var app = builder.Build();
        app.Lifetime.ApplicationStopping.Register(() => (model as IDisposable)?.Dispose());
        app.UseWorkspaceCorrelation();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) || context.Request.Host.Value != "127.0.0.1:5064" ||
                context.Request.Headers.Keys.Any(key => key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase) || key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)) ||
                context.Request.Headers.Origin is { Count: > 0 } origins && origins.ToString() != "http://127.0.0.1:5064")
            { context.Response.StatusCode = 403; return; }
            context.Items["login"] = owner; context.Items["role"] = "owner";
            try { await next(); }
            catch (WorkspaceException error) { context.Response.StatusCode = error.Status; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
        });
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", mutationsHeld = true, chatEnabled = true }));
        app.MapGet("/api/runtime", () => Results.Ok(new { version = 1, ownerLogin = owner, dataRoot = Path.GetFullPath(dataRoot), tailnetAddress = (string?)null,
            tailnetNames = Array.Empty<string>(), localOwnerEnabled = true, executionHeld = true, discoveryOnly = true, chatEnabled = true, discoveryIntervalMinutes = 15 }));
        app.MapGet("/api/session", () => Results.Ok(new { login = owner, role = "owner", accessMode = "local", agentModel = model.DefaultModel,
            agentReasoning = model.Reasoning, roles = roles.HiredIds(), executionHeld = false, mutationsHeld = true, chatOnly = true }));
        app.MapGet("/api/roles", () => Results.Ok(roles.Available.Select(r => new { r.Info.Id, r.Info.Name, r.Info.Summary, hired = roles.HiredIds().Contains(r.Info.Id) })));

        app.MapGet("/api/home", () => Results.Ok(requests.Home(owner)));
        app.MapGet("/api/requests", () => Results.Ok(requests.Requests(null)));
        app.MapGet("/api/conversations", (string? q) => Results.Ok(requests.Conversations(null, q)));
        app.MapGet("/api/people", () => Results.Ok(requests.People()));
        app.MapGet("/api/intake-order", () => Results.Ok(requests.IntakeOrder()));
        app.MapGet("/api/workload", () => Results.Ok(requests.Workload()));
        app.MapGet("/api/requests/{id}", (string id) => requests.FindRequest(id) is { } request ? Results.Ok(new { request, home = requests.IsHome(id),
            messages = requests.OwnerMessages(id), requesterRole = "owner", agentAllowed = requests.AgentAllowed(id),
            agentUpgradeReviewRequired = requests.AgentUpgradeReviewRequired(id) }) : Results.NotFound());
        app.MapGet("/api/requests/{id}/draft", (string id) => Results.Ok(requests.Draft(id, owner)));
        app.MapPut("/api/requests/{id}/draft", (string id, MessageDraft input) =>
        {
            if (requests.FindRequest(id) is null || input.Body is null || input.Body.Length > 4000 || !Guid.TryParse(input.ClientRequestId, out _)) return Results.BadRequest();
            requests.SaveDraft(id, owner, input.Body, input.ClientRequestId); return Results.Ok(new { saved = true });
        });
        app.MapGet("/api/requests/{id}/agent-status", (string id) =>
        {
            var task = agents.OpenTasks().FirstOrDefault(item => item.RequestId == id) ?? agents.Latest(id);
            return Results.Ok(new { running = task?.State is "queued" or "active", state = task?.State ?? "none", ahead = 0, current = (string?)null,
                error = task?.State == "failed" ? task.Detail : null, step = task?.State == "active" ? "Writing a text-only reply" : null });
        });
        app.MapGet("/api/requests/{id}/activity", (string id) => Results.Ok(new { running = agents.OpenTasks().Any(item => item.RequestId == id), streams = Array.Empty<object>(), record = requests.Activity.ForRequest(id, 30) }));
        app.MapPost("/api/requests/{id}/messages", (string id, NewMessage input, AgentWorker worker) =>
        {
            if (requests.FindRequest(id) is not { } request || request.Requester != owner || RequestStore.AgentRequestClosed(request.Status)) return Results.Conflict(new { error = "Start a fresh owner conversation." });
            if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > 4000 || !Guid.TryParse(input.ClientRequestId, out _)) return Results.BadRequest();
            if (ContentGuard.Clean(input.Body, 4000).Flags.Length > 0) return Results.BadRequest(new { error = "Remove credential-like content before asking the model." });
            var message = requests.AddMessage(id, owner, input.Body.Trim(), input.ClientRequestId);
            if (message.RequestId != id || message.Body != input.Body.Trim()) return Results.Conflict(new { error = "Use a fresh message id." });
            replies.Admit(request, message); worker.Wake(); return Results.Created("/api/requests/" + id, message);
        });
        app.MapPost("/api/requests", (NewRequest input, AgentWorker worker) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160 || string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 4000 || string.IsNullOrWhiteSpace(input.Project) || input.Project.Length > 100 || !Guid.TryParse(input.ClientRequestId, out _)) return Results.BadRequest();
            if (ContentGuard.Clean(input.Description, 4000).Flags.Length > 0) return Results.BadRequest(new { error = "Remove credential-like content before asking the model." });
            var request = requests.CreateRequest(owner, input.Project.Trim(), input.Title.Trim(), input.Description.Trim(), input.ClientRequestId);
            if (!replies.IsFreshRequest(request) || RequestStore.AgentRequestClosed(request.Status)) return Results.Conflict(new { error = "Use a fresh conversation id; historical conversations were retained." });
            var message = requests.AddMessage(request.Id, owner, input.Description, "fresh-open:" + input.ClientRequestId);
            if (request.Description != input.Description.Trim() || message.Body != input.Description) return Results.Conflict(new { error = "Use a fresh conversation id." });
            requests.MarkTextConversation(request.Id); request = requests.FindRequest(request.Id)!;
            replies.Admit(request, message); worker.Wake(); return Results.Created("/api/requests/" + request.Id, request);
        });
        app.MapPost("/api/requests/{id}/agent-reply", (string id, System.Text.Json.JsonElement body, AgentWorker worker) =>
        {
            if (!replies.HasAdmission(id) && !agents.OpenTasks().Any(item => item.RequestId == id)) return Results.Conflict(new { error = "Send a fresh message to request a reply. Historical tasks are not replayed." });
            if (body.ValueKind == System.Text.Json.JsonValueKind.Object && body.TryGetProperty("retry", out var retry) && retry.ValueKind == System.Text.Json.JsonValueKind.True) replies.Retry(id);
            worker.Wake(); return Results.Accepted(value: new { queued = true });
        });
        app.MapPost("/api/agent/tasks/{id}/cancel", (string id) => agents.Cancel(id) ? Results.Ok(agents.Find(id)) : Results.Conflict());
        app.MapGet("/api/agent", () =>
        {
            var scan = discovery.Read();
            return Results.Ok(new { shift = agents.Shift() with { SenseNote = scan.LastError ?? scan.Note }, schedule = agents.Schedule(), tasks = agents.OpenTasks(),
                recent = agents.Recent(5), activity = AgentActivity.Recent(6), runningIds = agents.Active().Select(item => item.Id),
                progress = new { state = scan.ScanRunning ? "Scanning" : "Observing", summary = scan.LastError ?? scan.Note, at = scan.LastAttemptAt,
                    nextCheckAt = scan.NextScanAt, freshness = scan.LastError is null ? "Recurring PR discovery every 15 minutes" : "Last successful findings retained" } });
        });
        app.MapGet("/api/owner-tasks", () => Results.Ok(discovery.Read().Findings.Select(item => new OwnerTask("discovery:" + item.Repository + "#" + item.Number,
            item.Repository + " #" + item.Number, "GitHub discovery", "https://github.com/" + item.Repository + "/pull/" + item.Number, null,
            "needs-owner", discovery.Read().CheckedAt?.ToString("O") ?? "", owner, "normal", [], "Discuss this observation in chat; repairs and publication require separate activation and fresh approval.",
            "observation-only", Kind: "PR observation", Detail: item.Reason))));
        app.MapGet("/api/discovery", () => Results.Ok(discovery.Read()));
        app.MapPost("/api/discovery/scan", async (CancellationToken token) =>
        {
            try { return Results.Ok(await discovery.ScanAsync(token)); }
            catch (WorkspaceException) { return Results.Json(new { error = "Discovery did not complete; last successful findings were retained." }, statusCode: 503); }
        });
        app.MapGet("/api/github/pending-approvals", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/github/activity", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/github/workflows/chat/{id}", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/github/loops/chat/{id}", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/github/chat/{id}", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/budgets", () => Results.NotFound());
        app.MapGet("/api/sessions", () => Results.Ok(new { sessions = Array.Empty<object>(), apps = Array.Empty<object>() }));
        app.MapGet("/api/github/health", () => Results.Ok(GitHubHealth.Status()));
        app.MapFallback("/api/{**path}", () => Results.Json(new { error = "This action is held in recovery mode. Chat and read-only PR discovery are available.", code = "execution-held" }, statusCode: 503));
        app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");
        return app;
    }
}
