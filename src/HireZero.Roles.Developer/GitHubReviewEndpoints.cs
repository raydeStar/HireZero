namespace HireZero.Roles.Developer;

public static class GitHubReviewEndpoints
{
    public static void AddGitHubReviews(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new GitHubReviewStore(databasePath));
        services.AddSingleton(new PipelineWatchStore(databasePath));
        services.AddSingleton<IPipelineRunSource>(provider => provider.GetRequiredService<GitHubReviewClient>());
        services.AddHostedService<PipelineWatchWorker>();
        services.AddSingleton(provider => new TrustedPostgres(
            Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
            provider.GetRequiredService<GitHubReviewStore>(),
            provider.GetService<ToolRegistry>(),
            provider.GetService<OwnerActionApprovals>()
        ));
        services.AddSingleton(provider => new GitHubReviewClient(provider.GetService<ToolRegistry>()));
        services.AddSingleton<IToolAuthorityVerifier>(provider => new WorkflowAuthorityVerifier(
            provider.GetRequiredService<GitHubReviewStore>()
        ));
        services.AddSingleton<IToolAuthorityVerifier>(provider => new LoopAuthorityVerifier(
            provider.GetRequiredService<GitHubReviewStore>()
        ));
        services.AddSingleton<IToolAuthorityVerifier>(provider => new FixCancelVerifier(
            provider.GetRequiredService<GitHubReviewStore>()
        ));
        services.AddSingleton(provider => new GitHubReconnect(
            provider.GetService<ToolRegistry>(),
            provider.GetService<OwnerActionApprovals>()
        ));
        services.AddSingleton<GitHubFixCoordinator>();
        services.AddSingleton<GitHubReviewMonitor>();
        services.AddHostedService(services => services.GetRequiredService<GitHubReviewMonitor>());
        services.AddSingleton<PullRequestWorkflowService>();
        services.AddHostedService(services => services.GetRequiredService<PullRequestWorkflowService>());
        services.AddSingleton<FixLoopDriver>();
        services.AddHostedService(services => services.GetRequiredService<FixLoopDriver>());
        services.AddHostedService<ClosedPullRequestSweep>();
    }

    // Approving a strategy is the owner's go-ahead for agent work in that conversation, but only for the exact strategy they saw:
    // a stale snapshot is refused before any consent is recorded.
    static void GoAhead(RequestStore requests, PullRequestWorkflow workflow, string? snapshot)
    {
        if (snapshot != workflow.ApprovalSnapshot)
            throw new ReviewAccessException("The strategy changed. Refresh before approving it.");
        if (requests.FindRequest(workflow.RequestId) is { } request && !requests.AgentDeclined(request.Id))
            requests.AllowAgent(request.Id);
    }

    // The owner's workflow and fix-loop decisions, once they succeed, go to the conversation's activity log.
    static readonly Dictionary<string, string> Decisions = new()
    {
        ["workflows/confirm"] = "Approved the fix strategy",
        ["workflows/answers"] = "Answered yes to the strategy's questions",
        ["workflows/loop"] = "Started a fix loop",
        ["workflows/rules"] = "Approved the business-rule changes",
        ["workflows/publish"] = "Approved publishing the fix",
        ["workflows/merge"] = "Approved the merge",
        ["workflows/cancel"] = "Canceled the fix",
        ["workflows/resume"] = "Resumed the fix",
        ["workflows/retry"] = "Asked for another attempt",
        ["workflows/revalidate"] = "Asked to check the fix again",
        ["workflows/answer"] = "Answered a question",
        ["workflows/refresh"] = "Refreshed the strategy",
        ["workflows/confirm-sensitive"] = "Confirmed the flagged sensitive lines",
        ["workflows/confirm-proof"] = "Accepted the test proof",
        ["workflows/reconcile"] = "Reconciled the publication",
        ["loops/stop"] = "Stopped the fix loop",
        ["loops/resume"] = "Resumed the fix loop",
    };

    internal static void RecordDecision(HttpContext context)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            context.Request.Path.Value ?? "",
            "^/api/github/(workflows|loops)/([^/]+)/([a-z-]+)$"
        );
        if (!match.Success || !Decisions.TryGetValue(match.Groups[1].Value + "/" + match.Groups[3].Value, out var verb))
            return;
        var store = context.RequestServices.GetRequiredService<GitHubReviewStore>();
        var id = match.Groups[2].Value;
        var (requestId, target, workflowId) =
            match.Groups[1].Value == "workflows"
                ? store.Workflows().FirstOrDefault(item => item.Id == id) is { } workflow
                    ? (workflow.RequestId, workflow.Repository + " #" + workflow.Number, workflow.Id)
                    : default
                : store.Loops(null).FirstOrDefault(item => item.Id == id) is { } loop
                    ? (loop.RequestId, loop.Repository + " #" + loop.Number, (string?)null)
                    : default;
        if (requestId is null)
            return;
        context
            .RequestServices.GetRequiredService<RequestStore>()
            .LogOwner(
                requestId,
                match.Groups[1].Value[..^1] + "." + match.Groups[3].Value,
                verb + " for " + target + ".",
                workflowId: workflowId
            );
    }

    public static void MapGitHubReviews(this WebApplication app)
    {
        app.Services.GetRequiredService<TrustedPostgres>();
        var api = app.MapGroup("/api/github");
        api.AddEndpointFilter(
            async (context, next) =>
            {
                if ((string?)context.HttpContext.Items["role"] != "owner")
                    return Results.StatusCode(403);
                var coordinator = context.HttpContext.RequestServices.GetRequiredService<GitHubFixCoordinator>();
                var mutation =
                    context.HttpContext.Request.Method != "GET"
                    && !context.HttpContext.Request.Path.Value!.EndsWith("/check");
                if (mutation)
                    await coordinator.Gate.WaitAsync(context.HttpContext.RequestAborted);
                try
                {
                    var result = await next(context);
                    if (mutation && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
                        RecordDecision(context.HttpContext);
                    return result;
                }
                catch (ReviewAccessException error)
                {
                    return Results.Json(new { error = error.Message }, statusCode: error.Status);
                }
                catch (Exception error)
                    when (error
                            is System.Text.Json.JsonException
                                or InvalidOperationException
                                or IOException
                                or ArgumentException
                    )
                {
                    return Results.Json(
                        new
                        {
                            error = "The GitHub review operation could not be validated. Check the local checkout and account, then refresh.",
                        },
                        statusCode: 502
                    );
                }
                finally
                {
                    if (mutation)
                        coordinator.Gate.Release();
                }
            }
        );
        api.MapGet("/health", () => Results.Ok(GitHubHealth.Status()));
        api.MapGet(
            "/pipeline-watches",
            (PipelineWatchStore store) =>
                Results.Ok(new { enabled = PipelineWatchWorker.Enabled, watches = store.All() })
        );
        api.MapPost(
            "/pipeline-watches",
            async (
                PipelineScope input,
                PipelineWatchStore watches,
                GitHubReviewStore reviews,
                GitHubReviewClient github,
                CancellationToken token
            ) =>
            {
                if (!PipelineWatchWorker.Enabled)
                    return Results.Conflict(
                        new
                        {
                            error = "Pipeline watch is off. Configure the connection and explicitly enable observation first.",
                        }
                    );
                if (
                    !reviews
                        .Repositories(input.AccountId)
                        .Any(repo => repo.GitHubId == input.RepositoryId && repo.Name == input.Repository)
                )
                    return Results.StatusCode(403);
                // Validate the input before constructing an API path; then verify this exact scope through the connected read-only source.
                if (
                    !GitHubReviewPolicy.ValidRepository(input.Repository)
                    || !PullRequestPolicy.Sha(input.HeadSha)
                    || input.RunId <= 0
                    || input.Attempt <= 0
                    || input.Ref.Length is < 1 or > 200
                    || input.Ref.Any(char.IsControl)
                )
                    return Results.BadRequest();
                var observed = await github.ReadAsync(input, token);
                if (observed.Scope != input || observed.CurrentHead != input.HeadSha)
                    return Results.Conflict(
                        new { error = "The selected run or branch changed. Refresh its exact scope." }
                    );
                return Results.Ok(watches.Create(input, DateTimeOffset.UtcNow));
            }
        );
        api.MapPost(
            "/pipeline-watches/{id}/cancel",
            (string id, PipelineWatchStore watches) => Results.Ok(watches.Cancel(id, DateTimeOffset.UtcNow))
        );
        api.MapPost(
            "/reconnect",
            async (GitHubReconnect reconnect, CancellationToken token) => Results.Ok(await reconnect.StartAsync(token))
        );
        api.MapGet(
            "/reconnect",
            async (GitHubReconnect reconnect, CancellationToken token) => Results.Ok(await reconnect.StatusAsync(token))
        );
        api.MapGet(
            "/connection",
            async (GitHubReviewClient github, CancellationToken token) =>
                Results.Ok(
                    new
                    {
                        account = await github.IdentityAsync(token),
                        mode = "confirmed-pr-fixes",
                        builderModel = PullRequestPolicy.BuilderModel,
                        reasoning = "high",
                    }
                )
        );
        api.MapGet(
            "/evidence",
            async (string repository, int number, GitHubReviewClient github, CancellationToken token) =>
                Results.Ok(await github.EvidenceAsync(repository, number, true, token))
        );
        api.MapGet(
            "/activity",
            async (HttpContext context, GitHubReviewStore store, GitHubReviewClient github, CancellationToken token) =>
            {
                var active = store
                    .Workflows()
                    .Where(w => w.State is "preparing" or "scoping" or "coding" or "validating")
                    .ToArray();
                if (active.Length == 0)
                    return Results.Ok(Array.Empty<object>());
                var account = await github.IdentityAsync(token);
                var live = context
                    .RequestServices.GetServices<IOwnerTaskContributor>()
                    .SelectMany(source => source.Tasks())
                    .Where(item => item.ExecutionState == "live")
                    .Select(item => item.RequestId)
                    .ToHashSet();
                return Results.Ok(
                    active
                        .Where(w => w.AccountId == account.Id && live.Contains(w.RequestId))
                        .Select(w => new
                        {
                            requestId = w.RequestId,
                            state = w.State,
                            workerState = w.RunState,
                        })
                );
            }
        );
        api.MapGet(
            "/pending-approvals",
            async (
                HttpContext context,
                GitHubReviewClient github,
                GitHubReviewStore store,
                RequestStore requests,
                CancellationToken token
            ) =>
                Results.Ok(
                    PendingWorkflowApprovals.For(
                        store,
                        requests,
                        context.RequestServices.GetService<WorkspaceOwner>() ?? new(WorkspaceOwner.LocalIdentity),
                        (await github.IdentityAsync(token)).Id
                    )
                )
        );
        api.MapGet(
            "/workflows/chat/{requestId}",
            async (
                string requestId,
                GitHubReviewClient github,
                GitHubReviewStore store,
                RequestStore requests,
                CancellationToken token
            ) =>
            {
                var request = requests.FindRequest(requestId);
                return request is null
                    ? Results.NotFound()
                    : Results.Ok(
                        store
                            .Workflows((await github.IdentityAsync(token)).Id, requestId)
                            .Where(w =>
                                w.State is not ("proposed" or "needs-evidence")
                                || w.MessageKey
                                    == GitHubRepositoryChat.ProposalKey(request, requests.Messages(requestId))
                            )
                            .Select(w => w with { ApprovalAvailable = store.ApprovalAvailable(w) })
                    );
            }
        );
        api.MapPost(
            "/workflows/{id}/confirm",
            async (
                string id,
                WorkflowRuleApproval input,
                PullRequestWorkflowService workflows,
                RequestStore requests,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                GoAhead(requests, workflow, input.Snapshot);
                return Results.Ok(await workflows.ConfirmAsync(workflow, token, input.Snapshot));
            }
        );
        api.MapPost(
            "/workflows/{id}/publish",
            async (
                string id,
                WorkflowRuleApproval input,
                PullRequestWorkflowService workflows,
                CancellationToken token
            ) =>
                Results.Ok(
                    await workflows.PublishAsync(await workflows.AuthorizedAsync(id, token), input.Snapshot, token)
                )
        );
        api.MapPost(
            "/workflows/{id}/rules",
            async (
                string id,
                WorkflowRuleApproval input,
                PullRequestWorkflowService workflows,
                CancellationToken token
            ) =>
                Results.Ok(
                    await workflows.ApproveRulesAsync(await workflows.AuthorizedAsync(id, token), input.Snapshot, token)
                )
        );
        api.MapPost(
            "/workflows/{id}/cancel",
            async (
                string id,
                WorkflowRuleApproval input,
                PullRequestWorkflowService workflows,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                if (workflow.State is "proposed" or "needs-evidence" or "publication-proposed")
                    await workflows.ValidateDenialAsync(workflow, input.Snapshot, token);
                return Results.Ok(await workflows.CancelAsync(workflow, token));
            }
        );
        api.MapPost(
            "/workflows/{id}/resume",
            async (string id, PullRequestWorkflowService workflows, CancellationToken token) =>
                Results.Ok(await workflows.ResumeAsync(await workflows.AuthorizedAsync(id, token), token))
        );
        api.MapPost(
            "/workflows/{id}/answer",
            async (string id, WorkflowAnswer input, PullRequestWorkflowService workflows, CancellationToken token) =>
                Results.Ok(
                    await workflows.AnswerAsync(
                        await workflows.AuthorizedAsync(id, token),
                        input.QuestionId ?? "",
                        input.Answer ?? "",
                        token
                    )
                )
        );
        api.MapPost(
            "/workflows/{id}/revalidate",
            async (string id, WorkflowRetry input, PullRequestWorkflowService workflows, CancellationToken token) =>
                Results.Ok(
                    await workflows.RevalidateAsync(await workflows.AuthorizedAsync(id, token), input.Note, token)
                )
        );
        api.MapPost(
            "/workflows/{id}/confirm-sensitive",
            async (
                string id,
                SensitiveConfirmation input,
                PullRequestWorkflowService workflows,
                CancellationToken token
            ) =>
                Results.Ok(
                    await workflows.ConfirmSensitiveAsync(await workflows.AuthorizedAsync(id, token), input.Hash, token)
                )
        );
        api.MapPost(
            "/workflows/{id}/confirm-proof",
            async (string id, WorkflowRetry input, PullRequestWorkflowService workflows, CancellationToken token) =>
                Results.Ok(
                    await workflows.ConfirmProofAsync(await workflows.AuthorizedAsync(id, token), input.Note, token)
                )
        );
        api.MapPost(
            "/workflows/{id}/reconcile",
            async (string id, PullRequestWorkflowService workflows, CancellationToken token) =>
                Results.Ok(await workflows.ReconcilePublicationAsync(await workflows.AuthorizedAsync(id, token), token))
        );
        // A stale proposal (older consent or conversation) is re-inspected into a current one; nothing runs until it's approved.
        api.MapPost(
            "/workflows/{id}/refresh",
            async (
                string id,
                PullRequestWorkflowService workflows,
                GitHubReviewClient github,
                GitHubReviewStore store,
                RequestStore requests,
                IServiceProvider services,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                if (workflow.State != "proposed")
                    throw new ReviewAccessException("Only a proposed strategy can be refreshed.");
                var request =
                    requests.FindRequest(workflow.RequestId)
                    ?? throw new ReviewAccessException("The conversation no longer exists.", 404);
                var answer = await GitHubRepositoryChat.InspectAsync(
                    request,
                    requests.Messages(request.Id),
                    workflow.Repository,
                    workflow.Number,
                    services.GetRequiredService<DeveloperPrompts>(),
                    github,
                    store,
                    requests,
                    token
                );
                requests.AddMessage(
                    request.Id,
                    "workspace-agent",
                    answer,
                    request.Id + ":refresh:" + Guid.NewGuid().ToString("N")
                );
                var key = GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id));
                if (
                    store
                        .Workflows(workflow.AccountId, request.Id)
                        .Any(item => item.Id != workflow.Id && item.Number == workflow.Number && item.MessageKey == key)
                )
                    store.SaveWorkflow(
                        workflow with
                        {
                            State = "canceled",
                            Error = "Replaced by a refreshed strategy.",
                        }
                    );
                return Results.Ok(new { answer });
            }
        );
        // The owner said yes to every question on this exact strategy: record the answers on it and start fixing. Any other answer
        // goes to chat as a message, so the agent proposes a revised strategy instead.
        api.MapPost(
            "/workflows/{id}/answers",
            async (
                string id,
                WorkflowAnswers input,
                PullRequestWorkflowService workflows,
                FixLoopDriver loops,
                RequestStore requests,
                GitHubReviewStore store,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                var questions = workflow.Strategy.Questions;
                if (workflow.State != "proposed" || questions.Length == 0)
                    throw new ReviewAccessException("This strategy has no open questions.");
                if (input.Snapshot != workflow.ApprovalSnapshot)
                    throw new ReviewAccessException("The strategy changed. Refresh before answering.");
                if (
                    input.Answers is not { } answers
                    || answers.Length != questions.Length
                    || answers.Any(answer => answer != "Yes")
                )
                    throw new ReviewAccessException(
                        "Only a yes to every question starts the fix here. Send other answers in chat for a revised strategy."
                    );
                if (workflow.Strategy.BusinessRuleChanges.Length > 0 && !workflow.RulesApproved)
                    throw new ReviewAccessException("Approve the business-rule changes first.");
                var answered = workflow with
                {
                    Strategy = workflow.Strategy with { Questions = [] },
                    OwnerNotes =
                    [
                        .. workflow.OwnerNotes ?? [],
                        .. questions.Select(question => "Asked: " + question + " Owner answered: Yes."),
                    ],
                };
                store.SaveWorkflow(answered);
                requests.AddMessage(
                    workflow.RequestId,
                    "workspace-agent",
                    "You answered yes to the strategy's questions:\n"
                        + string.Join("\n", questions.Select(question => "• " + question)),
                    workflow.RequestId + ":answers:" + workflow.Id
                );
                GoAhead(requests, answered, answered.ApprovalSnapshot);
                return Results.Ok(await loops.StartAsync(answered, token, answered.ApprovalSnapshot));
            }
        );
        // Fix loops: start one on a strategy (approves round one) or a delivered fix, stop it now or after this round, resume it.
        api.MapGet(
            "/loops/chat/{requestId}",
            (string requestId, GitHubReviewStore store) => Results.Ok(store.Loops(requestId))
        );
        api.MapPost(
            "/workflows/{id}/loop",
            async (
                string id,
                WorkflowRuleApproval input,
                PullRequestWorkflowService workflows,
                FixLoopDriver loops,
                RequestStore requests,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                GoAhead(requests, workflow, input.Snapshot);
                return Results.Ok(await loops.StartAsync(workflow, token, input.Snapshot));
            }
        );
        api.MapPost(
            "/loops/{id}/stop",
            async (string id, LoopStop input, GitHubReviewStore store, FixLoopDriver loops, CancellationToken token) =>
                store.Loops(null).FirstOrDefault(loop => loop.Id == id) is { } loop
                    ? Results.Ok(await loops.StopAsync(loop, input.Now, token))
                    : Results.NotFound()
        );
        api.MapPost(
            "/loops/{id}/resume",
            (string id, GitHubReviewStore store, FixLoopDriver loops) =>
                store.Loops(null).FirstOrDefault(loop => loop.Id == id) is { } loop
                    ? Results.Ok(loops.Resume(loop))
                    : Results.NotFound()
        );
        // Trusted PostgreSQL tests: status, one-time install, and the owner's per-repository profile.
        api.MapGet(
            "/postgres",
            (string? repository, TrustedPostgres trusted) => Results.Ok(trusted.Status(repository))
        );
        api.MapPost(
            "/postgres/install",
            async (TrustedPostgres trusted, CancellationToken token) =>
            {
                await trusted.InstallAsync(token);
                return Results.Ok(trusted.Status(null));
            }
        );
        api.MapPut(
            "/postgres/profile",
            (PostgresProfileInput input, GitHubReviewStore store, TrustedPostgres trusted) =>
            {
                if (!GitHubReviewPolicy.ValidRepository(input.Repository ?? ""))
                    return Results.BadRequest(new { error = "Use owner/name for the repository." });
                if (input.Enabled != true)
                {
                    store.SavePostgresProfile(null, input.Repository!);
                    return Results.Ok(trusted.Status(input.Repository));
                }
                var env = input.Env ?? [];
                if (
                    !System.Text.RegularExpressions.Regex.IsMatch(input.Database ?? "", "^[a-z_][a-z0-9_]{0,62}$")
                    || env.Count > 40
                    || env.Any(item =>
                        !System.Text.RegularExpressions.Regex.IsMatch(item.Key, "^[A-Z_][A-Z0-9_]{0,63}$")
                        || item.Value.Length > 300
                    )
                )
                    return Results.BadRequest(
                        new { error = "Use a lowercase database name and up to 40 UPPER_CASE variables." }
                    );
                store.SavePostgresProfile(
                    new PostgresProfile(input.Repository!, input.Database!, env),
                    input.Repository!
                );
                return Results.Ok(trusted.Status(input.Repository));
            }
        );
        api.MapPost(
            "/workflows/{id}/retry",
            async (string id, WorkflowRetry input, PullRequestWorkflowService workflows, CancellationToken token) =>
                Results.Ok(await workflows.RetryAsync(await workflows.AuthorizedAsync(id, token), input.Note, token))
        );
        api.MapGet(
            "/workflows/{id}/merge-check",
            async (
                string id,
                PullRequestWorkflowService workflows,
                GitHubReviewClient github,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                return Results.Ok(
                    PullRequestPolicy.Merge(
                        await github.EvidenceAsync(workflow.Repository, workflow.Number, false, token),
                        await github.IdentityAsync(token)
                    )
                );
            }
        );
        api.MapPost(
            "/workflows/{id}/merge",
            async (
                string id,
                WorkflowMergeApproval input,
                PullRequestWorkflowService workflows,
                CancellationToken token
            ) =>
                Results.Ok(
                    await workflows.MergeAsync(
                        await workflows.AuthorizedAsync(id, token),
                        input.HeadSha,
                        input.Snapshot,
                        token
                    )
                )
        );
        api.MapGet(
            "/workflows/{id}/evidence",
            async (
                HttpContext context,
                string id,
                PullRequestWorkflowService workflows,
                GitHubReviewStore store,
                GitHubFixCoordinator coordinator,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                if (
                    workflow.CodingRunId is { } runId
                    && workflow.RepositoryId is not null
                    && context.RequestServices.GetService<ICodingRunner>()?.Find(runId) is { } coded
                )
                    return Results.Ok(
                        new
                        {
                            screenshots = GitHubPublication.Screenshots(
                                (await coordinator.AuthorizedAsync(workflow.RepositoryId, token)).RootPath
                            ),
                            run = DirectCoding.View(coded),
                        }
                    );
                if (workflow.RepositoryId is null || workflow.FixId is null)
                    return Results.Ok(new { screenshots = Array.Empty<string>(), run = (object?)null });
                var repository = await coordinator.AuthorizedAsync(workflow.RepositoryId, token);
                var fix = store.Fixes(repository.Id).Single(f => f.Id == workflow.FixId);
                return Results.Ok(
                    new
                    {
                        screenshots = GitHubPublication.Screenshots(repository.RootPath),
                        run = await coordinator.StatusAsync(fix, token),
                    }
                );
            }
        );
        api.MapGet(
            "/workflows/{id}/screenshots/{name}",
            async (
                string id,
                string name,
                PullRequestWorkflowService workflows,
                GitHubFixCoordinator coordinator,
                CancellationToken token
            ) =>
            {
                var workflow = await workflows.AuthorizedAsync(id, token);
                if (workflow.RepositoryId is null || name != Path.GetFileName(name))
                    return Results.NotFound();
                var repository = await coordinator.AuthorizedAsync(workflow.RepositoryId, token);
                if (!GitHubPublication.Screenshots(repository.RootPath).Contains(name, StringComparer.Ordinal))
                    return Results.NotFound();
                return Results.File(
                    Path.Combine(repository.RootPath, ".developer-workspace-evidence", name),
                    "image/png"
                );
            }
        );
        api.MapGet(
            "/suggestions",
            async (GitHubReviewClient github, CancellationToken token) =>
                Results.Ok(await github.SuggestionsAsync(await github.IdentityAsync(token), token))
        );
        api.MapGet(
            "/repositories",
            async (GitHubReviewStore store, GitHubReviewClient github, CancellationToken token) =>
                Results.Ok(store.Repositories((await github.IdentityAsync(token)).Id))
        );
        api.MapGet(
            "/reviews",
            async (GitHubReviewClient github, CancellationToken token) =>
                Results.Ok((await github.MyReviewsAsync(await github.IdentityAsync(token), token)).Select(View))
        );
        api.MapGet(
            "/resolve",
            async (string name, GitHubReviewClient github, CancellationToken token) =>
                Results.Ok(await github.ResolveAsync(name, token))
        );
        api.MapGet(
            "/chat/{requestId}",
            async (
                string requestId,
                GitHubReviewStore store,
                GitHubReviewClient github,
                RequestStore requests,
                CancellationToken token
            ) =>
            {
                var request = requests.FindRequest(requestId);
                return request is null
                    ? Results.NotFound()
                    : Results.Ok(
                        store
                            .Proposals(requestId, (await github.IdentityAsync(token)).Id)
                            .Where(item =>
                                item.Confirmed
                                || GitHubRepositoryChat.CurrentProposal(item, request, requests.Messages(requestId))
                            )
                    );
            }
        );
        api.MapPost("/chat/{requestId}/confirm", ConfirmChatAsync);
        api.MapPost(
            "/repositories",
            async (
                ReviewRepositoryInput input,
                GitHubReviewStore store,
                GitHubReviewClient github,
                CancellationToken token
            ) =>
            {
                if (
                    !GitHubReviewPolicy.ValidRepository(input.Name)
                    || input.RootPath is null
                    || input.RootPath.Length > 500
                    || input.RootPath.Length > 0 && !Path.IsPathFullyQualified(input.RootPath)
                )
                    return Results.BadRequest(new { error = "Choose owner/name and an absolute local Git root." });
                var account = await github.IdentityAsync(token);
                if (store.Repositories(account.Id).Length >= 10)
                    return Results.BadRequest(
                        new { error = "This field test allows at most ten repository checkouts." }
                    );
                var remote = await github.RepositoryAsync(input.Name, token);
                var id = Guid.NewGuid().ToString("N");
                var repository = new ReviewRepository(
                    id,
                    account.Id,
                    remote.Id,
                    remote.Name,
                    input.RootPath.Length == 0 ? "" : Path.GetFullPath(input.RootPath.Trim()).TrimEnd('\\', '/'),
                    "github-review-" + account.Id + "-" + id
                );
                if (repository.RootPath.Length > 0)
                    await github.InspectAsync(repository, null, false, token);
                return Results.Ok(store.Add(repository));
            }
        );
        api.MapPut(
            "/repositories/{id}/checkout",
            async (
                string id,
                ReviewCheckoutInput input,
                GitHubFixCoordinator coordinator,
                GitHubReviewStore store,
                GitHubReviewClient github,
                CancellationToken token
            ) =>
            {
                if (
                    string.IsNullOrWhiteSpace(input.RootPath)
                    || input.RootPath.Length > 500
                    || !Path.IsPathFullyQualified(input.RootPath)
                )
                    return Results.BadRequest(new { error = "Choose an absolute local Git root." });
                var repository = await coordinator.AuthorizedAsync(id, token);
                await coordinator.EnsureNoActiveFixAsync(repository, token);
                repository = repository with { RootPath = Path.GetFullPath(input.RootPath.Trim()).TrimEnd('\\', '/') };
                await github.InspectAsync(repository, null, false, token);
                store.SetRoot(id, repository.RootPath);
                store.SetAutomatic(id, false);
                return Results.Ok(repository);
            }
        );
        api.MapGet(
            "/repositories/{id}/policy",
            async (
                string id,
                GitHubFixCoordinator coordinator,
                GitHubReviewStore store,
                GitHubReviewMonitor monitor,
                CancellationToken token
            ) =>
            {
                await coordinator.AuthorizedAsync(id, token);
                return Results.Ok(new { automatic = store.Automatic(id), status = monitor.Status(id) });
            }
        );
        api.MapPut(
            "/repositories/{id}/policy",
            async (
                string id,
                ReviewAutomaticInput input,
                GitHubFixCoordinator coordinator,
                GitHubReviewClient github,
                GitHubReviewStore store,
                CancellationToken token
            ) =>
            {
                var repository = await coordinator.AuthorizedAsync(id, token);
                if (input.Automatic)
                    await github.InspectAsync(repository, null, true, token);
                store.SetAutomatic(id, input.Automatic);
                return Results.Ok(new { automatic = input.Automatic });
            }
        );
        api.MapPost(
            "/repositories/{id}/check",
            async (string id, GitHubReviewMonitor monitor, CancellationToken token) =>
            {
                await monitor.CheckAsync(id, token);
                return Results.Ok(new { status = monitor.Status(id) });
            }
        );
        api.MapDelete(
            "/repositories/{id}",
            async (string id, GitHubReviewStore store, GitHubFixCoordinator coordinator, CancellationToken token) =>
            {
                var repository = await coordinator.AuthorizedAsync(id, token);
                await coordinator.EnsureNoActiveFixAsync(repository, token);
                store.Remove(id);
                return Results.Ok(new { removed = true });
            }
        );
        api.MapGet(
            "/repositories/{id}/reviews",
            async (string id, GitHubReviewClient github, GitHubFixCoordinator coordinator, CancellationToken token) =>
            {
                var repository = await coordinator.AuthorizedAsync(id, token);
                var reviews = await github.ReviewsAsync(repository, await github.IdentityAsync(token), token);
                return Results.Ok(reviews.Select(View));
            }
        );
        api.MapPost(
                "/repositories/{id}/scope",
                async (string id, ReviewScopeInput input, GitHubFixCoordinator coordinator, CancellationToken token) =>
                {
                    if (
                        input.ThreadIds is null
                        || input.HeadSha is null
                        || input.HeadSha.Length != 40
                        || input.ThreadIds.Any(value => string.IsNullOrEmpty(value) || value.Length > 100)
                    )
                        return Results.BadRequest(
                            new { error = "Choose a displayed revision and its review threads." }
                        );
                    var repository = await coordinator.AuthorizedAsync(id, token);
                    return Results.Ok(
                        await coordinator.PrepareAsync(repository, input.Number, input.HeadSha, input.ThreadIds, token)
                    );
                }
            )
            .RequireRateLimiting("agent");
        api.MapGet(
            "/repositories/{id}/fixes",
            async (string id, GitHubReviewStore store, GitHubFixCoordinator coordinator, CancellationToken token) =>
            {
                await coordinator.AuthorizedAsync(id, token);
                return Results.Ok(store.Fixes(id));
            }
        );
        api.MapGet(
            "/repositories/{id}/fixes/{fixId}",
            async (
                string id,
                string fixId,
                GitHubReviewStore store,
                GitHubFixCoordinator coordinator,
                CancellationToken token
            ) =>
            {
                await coordinator.AuthorizedAsync(id, token);
                var fix = store.Fixes(id).SingleOrDefault(item => item.Id == fixId);
                return fix is null ? Results.NotFound() : Results.Ok(await coordinator.StatusAsync(fix, token));
            }
        );
        api.MapPost(
                "/repositories/{id}/fixes/{fixId}/approve",
                async (
                    string id,
                    string fixId,
                    ReviewApprovalInput input,
                    GitHubReviewStore store,
                    GitHubFixCoordinator coordinator,
                    CancellationToken token
                ) =>
                {
                    var repository = await coordinator.AuthorizedAsync(id, token);
                    var fix = store.Fixes(id).SingleOrDefault(item => item.Id == fixId);
                    return fix is null
                        ? Results.NotFound()
                        : Results.Ok(
                            await coordinator.ApproveAsync(
                                repository,
                                fix,
                                input.ContextHash,
                                input.ProposalVersion,
                                token
                            )
                        );
                }
            )
            .RequireRateLimiting("agent");
        api.MapPost(
                "/repositories/{id}/fixes/{fixId}/rescope",
                async (
                    string id,
                    string fixId,
                    GitHubReviewStore store,
                    GitHubFixCoordinator coordinator,
                    CancellationToken token
                ) =>
                {
                    var repository = await coordinator.AuthorizedAsync(id, token);
                    var fix = store.Fixes(id).SingleOrDefault(item => item.Id == fixId);
                    return fix is null
                        ? Results.NotFound()
                        : Results.Ok(await coordinator.RescopeAsync(repository, fix, token));
                }
            )
            .RequireRateLimiting("agent");
        api.MapPost(
            "/repositories/{id}/fixes/{fixId}/cancel",
            async (
                string id,
                string fixId,
                GitHubReviewStore store,
                GitHubReviewClient github,
                GitHubFixCoordinator coordinator,
                CancellationToken token
            ) =>
            {
                if (!store.Repositories((await github.IdentityAsync(token)).Id).Any(item => item.Id == id))
                    return Results.NotFound();
                var fix = store.Fixes(id).SingleOrDefault(item => item.Id == fixId);
                return fix is null ? Results.NotFound() : Results.Ok(await coordinator.CancelAsync(fix, token));
            }
        );
    }

    private static async Task<IResult> ConfirmChatAsync(
        string requestId,
        RepositoryConfirmation input,
        GitHubReviewStore store,
        GitHubReviewClient github,
        RequestStore requests,
        GitHubFixCoordinator coordinator,
        CancellationToken token
    )
    {
        var account = await github.IdentityAsync(token);
        var proposal = store.Proposals(requestId, account.Id).SingleOrDefault(item => item.Id == input.ProposalId);
        var choice = proposal?.Choices.SingleOrDefault(item => item.Id == input.RepositoryId);
        var request = requests.FindRequest(requestId);
        if (proposal is null || choice is null || request is null)
            return Results.NotFound();
        if (proposal.Confirmed)
            return Results.Ok(new { confirmed = true });
        if (!GitHubRepositoryChat.CurrentProposal(proposal, request, requests.Messages(requestId)))
            return Results.Conflict(
                new { error = "The conversation changed. Ask again before confirming that setting." }
            );
        var remote = await github.RepositoryAsync(choice.Name, token);
        if (remote.Id != choice.Id)
            return Results.Conflict(new { error = "The repository identity changed. Ask to connect it again." });
        var repositories = store.Repositories(account.Id);
        if (proposal.Action != "connect")
        {
            var connected = repositories.SingleOrDefault(item =>
                item.Id == choice.CheckoutId && item.GitHubId == choice.Id
            );
            if (connected is null || !GitHubRepositoryChat.SameCheckout(choice, connected))
                return Results.Conflict(
                    new { error = "The connected checkout changed. Ask again before changing automatic fixes." }
                );
            connected = await coordinator.AuthorizedAsync(connected.Id, token);
            if (proposal.Action == "enableAutomatic")
                await github.InspectAsync(connected, null, true, token);
            if (!GitHubRepositoryChat.CurrentProposal(proposal, request, requests.Messages(requestId)))
                return Results.Conflict(
                    new { error = "The conversation changed. Ask again before confirming that setting." }
                );
            store.SetAutomatic(connected.Id, proposal.Action == "enableAutomatic");
            store.SaveProposal(proposal with { Confirmed = true });
            return Results.Ok(new { confirmed = true, automatic = proposal.Action == "enableAutomatic" });
        }
        if (repositories.Length >= 10 && !repositories.Any(item => item.GitHubId == remote.Id))
            return Results.BadRequest(new { error = "The field test allows ten repository connections." });
        var existing = repositories.FirstOrDefault(item => item.GitHubId == remote.Id);
        if (!GitHubRepositoryChat.CurrentProposal(proposal, request, requests.Messages(requestId)))
            return Results.Conflict(
                new { error = "The conversation changed. Ask again before confirming that connection." }
            );
        var id = Guid.NewGuid().ToString("N");
        var repository =
            existing
            ?? store.Add(
                new ReviewRepository(
                    id,
                    account.Id,
                    remote.Id,
                    remote.Name,
                    "",
                    "github-review-" + account.Id + "-" + id
                )
            );
        store.SaveProposal(proposal with { Confirmed = true });
        return Results.Ok(repository);
    }

    private static object View(CodeReview review) =>
        new
        {
            review.Repository,
            review.Number,
            review.Title,
            review.HeadSha,
            review.Complete,
            threads = review
                .Threads.Where(thread => !thread.Resolved)
                .Select(thread => new
                {
                    thread.Id,
                    thread.Path,
                    thread.Line,
                    thread.Outdated,
                    thread.Complete,
                    actionable = GitHubReviewPolicy.Actionable(thread),
                    comments = thread.Comments.Select(comment => new
                    {
                        comment.Author,
                        body = GitHubReviewPolicy.SafeText(comment.Body)
                            ? comment.Body[..Math.Min(comment.Body.Length, 2000)]
                            : "Comment withheld because it appears to contain credentials.",
                    }),
                }),
        };
}

public sealed record ReviewRepositoryInput(string Name, string RootPath);

public sealed record ReviewScopeInput(int Number, string HeadSha, string[] ThreadIds);

public sealed record ReviewApprovalInput(string ContextHash, int ProposalVersion);

public sealed record ReviewCheckoutInput(string RootPath);

public sealed record ReviewAutomaticInput(bool Automatic);

public sealed record RepositoryConfirmation(string ProposalId, long RepositoryId);

public sealed record WorkflowRuleApproval(string Snapshot);

public sealed record WorkflowMergeApproval(string HeadSha, string Snapshot);

public sealed record WorkflowAnswer(string? QuestionId, string? Answer);

public sealed record WorkflowRetry(string? Note);

public sealed record WorkflowAnswers(string Snapshot, string[]? Answers);

public sealed record SensitiveConfirmation(string? Hash);

public sealed record LoopStop(bool Now);

public sealed record PostgresProfileInput(
    string? Repository,
    bool? Enabled,
    string? Database,
    Dictionary<string, string>? Env
);
