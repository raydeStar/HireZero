using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Tools.SemaForge;
using static HireZero.Core.WorkspaceAccess;

namespace HireZero.Roles.Developer;

// The developer employee: GitHub PR diagnosis and confirmed fixes, request preparation, and project Wiki
// drafts. Everything it contributes is registered here, so firing the role removes all of it.
public sealed class DeveloperRole : IRole
{
    public RoleInfo Info { get; } =
        new(
            "developer",
            "Developer",
            "Observes your GitHub PRs on shift. Diagnosis, repairs and publication each wait for your approval."
        );

    public void AddServices(IServiceCollection services, RoleContext context)
    {
        services.AddWikiDrafts(context.DatabasePath);
        services.AddGitHubReviews(context.DatabasePath);
        services.AddSingleton<IBacklogSource>(new LocalPipelineBacklog(context.DatabasePath));
        services.AddSingleton(new PreparationStore(context.DatabasePath));
        services.AddSingleton<DeveloperPrompts>();
        services.AddSingleton<IPullRequestReasoner>(provider => provider.GetRequiredService<DeveloperPrompts>());
        services.AddSingleton<LocalSources>();
        services.AddSingleton<GitHubChatClassifier>();
        services.AddSingleton<IShiftSensor, GitHubShiftSensor>();
        services.AddSingleton<IBriefContributor, DeveloperBrief>();
        services.AddSingleton<IOwnerTaskContributor, DeveloperOwnerTasks>();
        services.AddSingleton<IConversationActivity, ConversationCodingActivity>();
        services.AddSingleton<ITaskThreadSignal, PullRequestTaskSignal>();
        // The checkout comes first, then PR fixes and open PRs.
        services.AddSingleton<IProjectContextContributor, RepositoryContext>();
        services.AddSingleton<IProjectContextContributor, DeveloperProjectContext>();
        services.AddSingleton<IWorkLogSource, GitHubWorkLog>();
        services.AddSingleton<IAgentTaskHandler, DiagnosePullRequestHandler>();
        // Order matters: GitHub requests first, then the developer's conversational reply.
        services.AddSingleton<IConversationResponder, RepositoryChatResponder>();
        services.AddSingleton<IConversationResponder, DeveloperConversationResponder>();
        // SemaForge, Jev and the Wiki are integrations; the Developer reaches them only through their tools.
    }

    public void Start(IServiceProvider services) => services.GetRequiredService<WikiDraftStore>().RecoverInterrupted();

    public void MapEndpoints(WebApplication app)
    {
        app.MapWikiDrafts();
        app.MapGitHubReviews();
        app.MapGet(
            "/api/source-status",
            async (HttpContext context, LocalSources sources, CancellationToken token) =>
                IsOwner(context) ? Results.Ok(await sources.AvailabilityAsync(token)) : Results.StatusCode(403)
        );
        app.MapGet(
            "/api/preparation",
            (HttpContext context, PreparationStore store) =>
                IsOwner(context) ? Results.Ok(store.Preparations()) : Results.StatusCode(403)
        );
        app.MapPut(
            "/api/requests/{id}/preparation",
            (HttpContext context, RequestStore requests, PreparationStore store, string id, PreparationChange input) =>
            {
                if (!IsOwner(context))
                    return Results.StatusCode(403);
                if (requests.FindRequest(id) is null)
                    return Results.NotFound();
                if (
                    !PreparationAdvice.ValidStage(input.Stage)
                    || input.Blocker is null
                    || input.Blocker.Length > 1000
                    || input.NextAction is null
                    || input.NextAction.Length > 1000
                )
                    return Results.BadRequest();
                return Results.Ok(
                    store.SavePreparation(id, input.Stage!, input.Blocker.Trim(), input.NextAction.Trim())
                );
            }
        );
        app.MapPost(
                "/api/requests/{id}/preparation/suggest",
                async (
                    HttpContext context,
                    RequestStore requests,
                    DeveloperPrompts prompts,
                    LocalSources sources,
                    string id,
                    CancellationToken token
                ) =>
                {
                    if (!IsOwner(context))
                        return Results.StatusCode(403);
                    var item = requests.FindRequest(id);
                    if (item is null)
                        return Results.NotFound();
                    var owner =
                        context.RequestServices.GetService<WorkspaceOwner>() ?? new(WorkspaceOwner.LocalIdentity);
                    if (!requests.CanUseAgent(item, owner))
                        return Results.StatusCode(403);
                    try
                    {
                        using var consent = requests.LeaseAgentConsent(item, owner, token);
                        token = consent.Token;
                        var sourceContext = await sources.ContextAsync(item, token);
                        var suggestion = await requests.WithAgentPermission(
                            item,
                            owner,
                            () => prompts.SuggestPreparationAsync(item, requests.Messages(id), sourceContext, token)
                        );
                        return requests.WithAgentPermission(
                            item,
                            owner,
                            () =>
                            {
                                token.ThrowIfCancellationRequested();
                                return suggestion is null
                                    ? Results.Json(
                                        new
                                        {
                                            error = "The agent returned an uncertain preparation result. Review requirements manually or ask again.",
                                        },
                                        statusCode: 502
                                    )
                                    : (IResult)
                                        Results.Ok(
                                            new
                                            {
                                                suggestion,
                                                sources = new
                                                {
                                                    semaForge = sourceContext.SemaForge.Status,
                                                    wiki = sourceContext.Wiki.Status,
                                                },
                                            }
                                        );
                            }
                        );
                    }
                    catch (WorkspaceException)
                    {
                        return Results.StatusCode(403);
                    }
                    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                    {
                        return Results.Conflict(
                            new { error = "Consent or conversation changed. The preparation result was discarded." }
                        );
                    }
                    catch (ModelUnavailableException)
                    {
                        return Results.Json(
                            new { error = "The preparation agent is unavailable or needs OpenAI sign-in." },
                            statusCode: 503
                        );
                    }
                }
            )
            .RequireRateLimiting("agent");
        app.MapGet(
            "/api/requests/{id}/triage",
            async (
                HttpContext context,
                RequestStore requests,
                LocalSources sources,
                string id,
                CancellationToken token
            ) =>
            {
                if (!IsOwner(context))
                    return Results.StatusCode(403);
                var item = requests.FindRequest(id);
                return item is null ? Results.NotFound() : Results.Ok(await sources.SuggestAsync(item, token));
            }
        );
        app.MapGet(
            "/api/requests/{id}/context",
            async (
                HttpContext context,
                RequestStore requests,
                LocalSources sources,
                string id,
                CancellationToken token
            ) =>
            {
                if (!IsOwner(context))
                    return Results.StatusCode(403);
                var item = requests.FindRequest(id);
                return item is null ? Results.NotFound() : Results.Ok(await sources.ContextAsync(item, token));
            }
        );
        // Records a task in SemaForge (optional; execution approval is separate).
        app.MapPost(
            "/api/requests/{id}/approve",
            async (HttpContext context, RequestStore requests, string id, CancellationToken token) =>
            {
                if (!IsOwner(context))
                    return Results.StatusCode(403);
                var item = requests.FindRequest(id);
                if (item is null)
                    return Results.NotFound();
                if (!requests.ReservePromotion(id))
                    return Results.Conflict(
                        new { error = "This request was already approved or needs reconciliation." }
                    );
                try
                {
                    var tools =
                        context.RequestServices.GetService<ToolRegistry>()?.For(DeveloperModule.Shared)
                        ?? throw new ToolFailureException("unavailable", "SemaForge is not configured.");
                    var reply = await tools.InvokeAsync(
                        SemaForgeTools.Write,
                        new ServiceCall(
                            "POST",
                            "/api/work-intelligence/work-items",
                            JsonSerializer.Serialize(
                                new
                                {
                                    title = item.Title,
                                    nextAction = item.Description,
                                    project = item.Project,
                                    state = "hot",
                                },
                                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                            )
                        ),
                        token,
                        OwnerRequests.Current
                    );
                    if (!reply.Success)
                        return Results.Json(
                            new { error = "SemaForge did not confirm the task. Reconcile before retrying." },
                            statusCode: 502
                        );
                    using var content = JsonDocument.Parse(reply.Body);
                    if (
                        content.RootElement.TryGetProperty("id", out var idElement) != true
                        || idElement.GetString() is not { Length: > 0 } semaId
                    )
                        return Results.Json(
                            new { error = "SemaForge returned an unrecognized result. Reconcile before retrying." },
                            statusCode: 502
                        );
                    requests.FinishPromotion(id, semaId);
                    return Results.Ok(requests.FindRequest(id));
                }
                catch (Exception error) when (error is ToolFailureException or ToolRefusedException or JsonException)
                {
                    return Results.Json(
                        new { error = "SemaForge is unavailable. Reconcile before retrying." },
                        statusCode: 502
                    );
                }
            }
        );
    }
}

public sealed record PreparationChange(string? Stage, string? Blocker, string? NextAction);
