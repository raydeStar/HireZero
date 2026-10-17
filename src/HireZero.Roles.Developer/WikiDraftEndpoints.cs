using HireZero.Tools.Wiki;

namespace HireZero.Roles.Developer;

public static class WikiDraftEndpoints
{
    public static void AddWikiDrafts(this IServiceCollection services, string path)
    {
        services.AddSingleton(new WikiDraftStore(path));
        services.AddSingleton<WikiPublisher>();
        services.AddHostedService<WikiPublicationMonitor>();
        services.AddSingleton<IToolAuthorityVerifier>(provider => new WikiDraftAuthorityVerifier(
            provider.GetRequiredService<WikiDraftStore>()
        ));
        services.AddSingleton(provider => new WikiProposalAdapter(provider.GetService<ToolRegistry>()));
    }

    public static void MapWikiDrafts(this WebApplication app)
    {
        var api = app.MapGroup("/api/wiki");
        api.AddEndpointFilter(
            async (context, next) =>
                (string?)context.HttpContext.Items["role"] == "owner" ? await next(context) : Results.StatusCode(403)
        );
        api.MapGet(
            "/connection",
            async (HttpContext context, CancellationToken token) =>
                context.RequestServices.GetService<ToolRegistry>() is not { } tools
                    ? Results.Ok(new { url = (string?)null })
                    : Results.Ok(
                        new
                        {
                            url = await tools.For(DeveloperModule.Shared).InvokeAsync(WikiTools.Origin, new(), token),
                        }
                    )
        );
        api.MapGet("/projects", (WikiDraftStore store) => Results.Ok(store.Projects()));
        api.MapPut(
            "/projects",
            (WikiProject input, WikiDraftStore store, RequestStore requests) =>
            {
                if (
                    !requests.Requests(null).Any(item => item.Project == input.Project)
                    || !WikiDraftPolicy.ValidKey(input.WikiProjectId)
                    || input.Synthetic && !input.WikiProjectId.StartsWith("synthetic-", StringComparison.Ordinal)
                )
                    return Results.BadRequest(
                        new
                        {
                            error = "Choose an existing project and a stable Wiki identifier. Synthetic projects use synthetic- identifiers.",
                        }
                    );
                return store.SaveProject(input)
                    ? Results.Ok(input)
                    : Results.Conflict(
                        new
                        {
                            error = "A Wiki project mapping with retained drafts cannot change or overlap another project.",
                        }
                    );
            }
        );
        api.MapGet(
            "/targets",
            async (string project, WikiDraftStore store, WikiProposalAdapter adapter, CancellationToken token) =>
            {
                var destination = store.Project(project);
                if (destination is null)
                    return Results.NotFound();
                try
                {
                    return Results.Ok(await adapter.TargetsAsync(destination, token));
                }
                catch (WikiAdapterException error)
                {
                    return Failure(error);
                }
            }
        );
        api.MapGet("/drafts", (WikiDraftStore store) => Results.Ok(store.Drafts()));
        api.MapGet("/scope-reviews", (WikiDraftStore store) => Results.Ok(store.ScopeReviews()));
        api.MapPost(
            "/scope-reviews/acknowledge",
            (WikiScopeAcknowledgement input, WikiDraftStore store) =>
            {
                store.AcknowledgeScope(input.DraftId, input.RequestId);
                return Results.Ok(new { acknowledged = true });
            }
        );
        api.MapPost("/drafts", CreateAsync);
        api.MapPut(
            "/drafts/{id}",
            async (
                string id,
                WikiDraftEdit input,
                WikiDraftStore store,
                WikiPublisher publisher,
                CancellationToken token
            ) =>
            {
                if (!WikiDraftPolicy.ValidContent(input.Content))
                    return Results.BadRequest(new { error = "Check the Wiki draft fields." });
                if (store.Find(id) is null)
                    return Results.NotFound();
                var draft = store.Edit(id, input.ExpectedVersion, input.Content);
                if (draft is null)
                    return Results.Conflict(
                        new
                        {
                            error = "The draft changed or is locked. Clearing a conflict or gap also requires a resolution note.",
                        }
                    );
                if (WikiProposalAdapter.Enabled && WikiDraftPolicy.CanSubmit(draft))
                    draft = await publisher.PublishAsync(draft.Id, draft.Version, token);
                return Results.Ok(draft);
            }
        );
        api.MapPost("/drafts/{id}/submit", SubmitAsync);
        api.MapPost("/drafts/{id}/sync", SyncAsync);
        api.MapPost("/suggest", SuggestAsync).RequireRateLimiting("agent");
    }

    private static WikiEvidence[] Evidence(WorkRequest request, RequestStore store, WikiTarget? before) =>
        [
            new("request", request.Id),
            .. store.Messages(request.Id).TakeLast(8).Select(item => new WikiEvidence("message", item.Id)),
            .. request.SemaForgeId is { } task ? new[] { new WikiEvidence("semaforge", task) } : [],
            .. before is { } target ? new[] { new WikiEvidence("wiki", target.RevisionId) } : [],
        ];

    private static async Task<IResult> CreateAsync(
        WikiDraftCreate input,
        WikiDraftStore store,
        RequestStore requests,
        WikiProposalAdapter adapter,
        WikiPublisher publisher,
        CancellationToken token
    )
    {
        var request = requests.FindRequest(input.RequestId);
        if (request is null)
            return Results.NotFound();
        var destination = store.Project(request.Project);
        if (destination is null)
            return Results.Conflict(new { error = "Connect this project to a Wiki identifier first." });
        if (!Guid.TryParse(input.ClientRequestId, out _) || !WikiDraftPolicy.ValidContent(input.Content))
            return Results.BadRequest();
        if (store.FindCreate(input.ClientRequestId) is { } retained)
        {
            if (
                retained.RequestId != request.Id
                || retained.Content != input.Content
                || retained.Before?.EntryId != input.TargetEntryId
            )
                return Results.Conflict(new { error = "This retry identifier already holds different content." });
            if (WikiProposalAdapter.Enabled && WikiDraftPolicy.CanSubmit(retained))
                retained = await publisher.PublishAsync(retained.Id, retained.Version, token);
            return Results.Created("/api/wiki/drafts/" + retained.Id, retained);
        }
        WikiTarget? before = null;
        if (input.TargetEntryId is not null)
        {
            try
            {
                before = (await adapter.TargetsAsync(destination, token)).FirstOrDefault(item =>
                    item.EntryId == input.TargetEntryId
                );
            }
            catch (WikiAdapterException error)
            {
                return Failure(error);
            }
            if (
                before is null
                || !before.Supported
                || before.Status != "current"
                || before.Kind != input.Content.Kind
                || before.KnowledgeKey != input.Content.KnowledgeKey
            )
                return Results.Conflict(
                    new
                    {
                        error = "The selected Wiki revision is unavailable, unresolved, or needs its native revision workflow.",
                    }
                );
        }
        var draft = store.Create(
            request,
            destination,
            input.Content,
            before,
            Evidence(request, requests, before),
            input.ClientRequestId
        );
        if (draft is null)
            return Results.Conflict(new { error = "This retry identifier already holds a different draft." });
        if (WikiProposalAdapter.Enabled && WikiDraftPolicy.CanSubmit(draft))
            draft = await publisher.PublishAsync(draft.Id, draft.Version, token);
        return Results.Created("/api/wiki/drafts/" + draft.Id, draft);
    }

    private static async Task<IResult> SuggestAsync(
        WikiSuggest input,
        WikiDraftStore store,
        RequestStore requests,
        DeveloperPrompts cells,
        LocalSources sources,
        WikiProposalAdapter adapter,
        WikiPublisher publisher,
        CancellationToken token
    )
    {
        var request = requests.FindRequest(input.RequestId);
        if (request is null)
            return Results.NotFound();
        var destination = store.Project(request.Project);
        if (destination is null)
            return Results.Conflict(new { error = "Connect this project to a Wiki identifier first." });
        if (!Guid.TryParse(input.ClientRequestId, out _))
            return Results.BadRequest();
        if (input.DraftId is null && store.FindCreate(input.ClientRequestId) is { } retained)
            return retained.RequestId == request.Id && retained.Before?.EntryId == input.TargetEntryId
                ? Results.Ok(retained)
                : Results.Conflict();
        var existing = input.DraftId is null ? null : store.Find(input.DraftId);
        if (
            input.DraftId is not null
            && (
                existing is null
                || existing.RequestId != request.Id
                || existing.Version != input.ExpectedVersion
                || existing.Before?.EntryId != input.TargetEntryId
                || !WikiDraftPolicy.Editable(existing.Status)
            )
        )
            return Results.Conflict(new { error = "The saved update changed. Refresh before asking OpenClaw again." });
        WikiTarget? before = null;
        try
        {
            if (input.TargetEntryId is not null)
            {
                before = (await adapter.TargetsAsync(destination, token)).FirstOrDefault(item =>
                    item.EntryId == input.TargetEntryId
                );
                if (before is null || !before.Supported || before.Status != "current")
                    return Results.Conflict(new { error = "The selected Wiki revision needs fresh review." });
            }
            var context = await sources.ContextAsync(request, token);
            var content = await cells.SuggestWikiDraftAsync(
                request,
                requests.Messages(request.Id),
                context,
                before,
                token,
                existing?.Content
            );
            if (content is null)
                return Results.Json(
                    new { error = "The agent returned an incomplete Wiki update. No addition was made." },
                    statusCode: 502
                );
            var draft = existing is null
                ? store.Create(
                    request,
                    destination,
                    content,
                    before,
                    Evidence(request, requests, before),
                    input.ClientRequestId
                )
                : store.Edit(existing.Id, existing.Version, content);
            if (draft is null)
                return Results.Conflict(
                    new { error = "The update changed, or an unresolved gap still needs an explanation." }
                );
            if (WikiProposalAdapter.Enabled && WikiDraftPolicy.CanSubmit(draft))
                draft = await publisher.PublishAsync(draft.Id, draft.Version, token);
            return Results.Ok(draft);
        }
        catch (WikiAdapterException error)
        {
            return Failure(error);
        }
        catch (ModelUnavailableException)
        {
            return Results.Json(
                new { error = "The private drafting agent is unavailable or needs sign-in." },
                statusCode: 503
            );
        }
    }

    private static async Task<IResult> SubmitAsync(
        string id,
        WikiDraftVersion input,
        WikiDraftStore store,
        WikiPublisher publisher,
        CancellationToken token
    )
    {
        var prior = store.Find(id);
        if (prior is null)
            return Results.NotFound();
        if (prior.Version != input.ExpectedVersion)
            return Results.Conflict(new { error = "The saved version changed. Refresh before retrying." });
        if (!WikiDraftPolicy.CanSubmit(prior))
            return Results.Conflict(
                new
                {
                    error = "Resolve conflicts and information gaps first. Real knowledge needs authenticated Wiki access.",
                }
            );
        return Results.Ok(await publisher.PublishAsync(id, input.ExpectedVersion, token));
    }

    private static async Task<IResult> SyncAsync(
        string id,
        WikiDraftVersion input,
        WikiDraftStore store,
        WikiPublisher publisher,
        CancellationToken token
    )
    {
        var draft = store.Find(id);
        if (draft is null)
            return Results.NotFound();
        if (draft.Version != input.ExpectedVersion || draft.RevisionId is null)
            return Results.Conflict();
        try
        {
            var confirmed = await publisher.ConfirmAsync(draft, token);
            return Results.Ok(
                new
                {
                    draft = confirmed,
                    decision = confirmed.Status == "approved" ? "approved" : "unconfirmed",
                    message = confirmed.Status == "approved"
                        ? "Wiki addition confirmed. A private one-sentence notice is in the source chat."
                        : "This revision is not current knowledge. Publication may be pending, blocked, withdrawn, or unavailable.",
                }
            );
        }
        catch (WikiAdapterException error)
        {
            return Failure(error);
        }
    }

    private static IResult Failure(WikiAdapterException error) =>
        Results.Json(
            new
            {
                error = error.Status == 409
                    ? "The Wiki destination or base revision needs review. The draft is retained. Compare the current Wiki before retrying."
                    : "The Wiki could not confirm this operation. The private draft is retained; an uncertain submission can retry the same version safely.",
            },
            statusCode: error.Status
        );
}

public sealed record WikiDraftCreate(
    string RequestId,
    string ClientRequestId,
    string? TargetEntryId,
    WikiDraftContent Content
);

public sealed record WikiDraftEdit(int ExpectedVersion, WikiDraftContent Content);

public sealed record WikiDraftVersion(int ExpectedVersion);

public sealed record WikiSuggest(
    string RequestId,
    string? TargetEntryId,
    string ClientRequestId,
    string? DraftId,
    int? ExpectedVersion
);

public sealed record WikiScopeAcknowledgement(string DraftId, string RequestId);
