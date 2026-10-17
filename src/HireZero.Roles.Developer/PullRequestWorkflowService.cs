using System.Text.Json;

namespace HireZero.Roles.Developer;

public sealed class PullRequestWorkflowService(
    GitHubReviewStore store,
    GitHubReviewClient github,
    GitHubFixCoordinator coordinator,
    IPullRequestReasoner cells,
    RequestStore requests,
    ICodingRunner? coding = null,
    WorkspaceOwner? owner = null,
    BudgetLedger? budgets = null
) : BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _revokedRequests = new();
    private readonly SemaphoreSlim _revocationWake = new(0, 1);

    private void OnRequestChanged(string id)
    {
        _revokedRequests.Enqueue(id);
        try
        {
            _revocationWake.Release();
        }
        catch (SemaphoreFullException) { }
    }

    private WorkspaceOwner Owner => owner ?? new(WorkspaceOwner.LocalIdentity);

    private T InScope<T>(PullRequestWorkflow workflow, Func<T> action)
    {
        var request =
            requests.FindRequest(workflow.RequestId)
            ?? throw new WorkspaceException("The conversation no longer exists.");
        try
        {
            return requests.WithAgentPermission(
                request,
                Owner,
                () =>
                {
                    var current = store.Workflows(workflow.AccountId).SingleOrDefault(item => item.Id == workflow.Id);
                    if (
                        current?.State is "consent-revoked" or "scope-changed"
                        || workflow.ConsentGeneration <= 0
                        || requests.AgentConsentGeneration(workflow.RequestId) != workflow.ConsentGeneration
                        || current?.State is "canceled" or "published" or "merged"
                            && workflow.State != current.State
                            && workflow.State != "merge-pending"
                        || GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id))
                            != workflow.MessageKey
                    )
                        throw new ReviewAccessException(
                            "The conversation or consent changed. Approve a fresh proposal before further work."
                        );
                    using (DeveloperAuthority.Workflow(workflow.Id))
                        return action();
                }
            );
        }
        catch (WorkspaceException error) when (error is not ReviewAccessException)
        {
            throw new ReviewAccessException(error.Message);
        }
    }

    private RequestAgentConsent Lease(PullRequestWorkflow workflow, CancellationToken token) =>
        InScope(workflow, () => requests.LeaseAgentConsent(requests.FindRequest(workflow.RequestId)!, Owner, token));

    private void Save(PullRequestWorkflow workflow) =>
        InScope(
            workflow,
            () =>
            {
                store.SaveWorkflow(workflow);
                return true;
            }
        );

    private async Task StopRevokedAsync(PullRequestWorkflow workflow)
    {
        using var cancellationEffects = AgentEffectBoundary.Enter(action => action());
        var current = store.Workflows(workflow.AccountId).Single(item => item.Id == workflow.Id);
        if (current.State is "consent-revoked" or "scope-changed" or "canceled" or "published" or "merged")
            return;
        var request = requests.FindRequest(workflow.RequestId);
        var state =
            request is null
            || !requests.CanUseAgent(request, Owner)
            || requests.AgentConsentGeneration(workflow.RequestId) != workflow.ConsentGeneration
                ? "consent-revoked"
                : "scope-changed";
        // Keep receipts after an uncertain publication. Revocation never replays its effects.
        store.SaveWorkflow(
            current with
            {
                State = state,
                Error =
                    "Consent or conversation changed. This attempt cannot continue; review its recorded work before a fresh proposal.",
            }
        );
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (current.CodingRunId is { } runId)
            coding?.Cancel(runId);
        if (current.RepositoryId is { } repositoryId && current.FixId is { } fixId)
        {
            var fix = store.Fixes(repositoryId).SingleOrDefault(item => item.Id == fixId);
            if (fix is not null)
                try
                {
                    await coordinator.CancelAsync(fix, stop.Token);
                }
                catch (Exception error) when (error is ReviewAccessException or OperationCanceledException) { }
        }
        else if (current.RepositoryId is { } partialRepository)
        {
            // A handoff may have journaled its fix before returning its ID to the workflow.
            foreach (
                var fix in store
                    .Fixes(partialRepository)
                    .Where(item =>
                        item.Number == current.Number
                        && item.HeadSha == current.HeadSha
                        && item.Snapshot == current.EvidenceSnapshot
                    )
            )
                try
                {
                    await coordinator.CancelAsync(fix, stop.Token);
                }
                catch (Exception error) when (error is ReviewAccessException or OperationCanceledException) { }
        }
    }

    public async Task<PullRequestWorkflow> AuthorizedAsync(string id, CancellationToken token) =>
        store.Workflows((await github.IdentityAsync(token)).Id).SingleOrDefault(w => w.Id == id)
        ?? throw new ReviewAccessException("This PR workflow is not available to the connected account.", 404);

    public async Task<PullRequestWorkflow> ConfirmAsync(
        PullRequestWorkflow workflow,
        CancellationToken token,
        string snapshot
    )
    {
        using var execution = AgentActivity.Begin("workflow:" + workflow.Id);
        using var usage = UsageMeter.For(DeveloperBudget.Role, workflow.RequestId);
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State is not ("proposed" or "needs-evidence" or "handoff-attention"))
            throw new ReviewAccessException("This attempt already started. Approve a fresh proposal for further work.");
        if (snapshot != workflow.ApprovalSnapshot)
            throw new ReviewAccessException("The proposal changed. Refresh and approve the current strategy.");
        // Checked before the approval is consumed, so the same proposal can be approved again once the owner raises the limit.
        if (DeveloperBudget.Blocked(budgets, workflow.RequestId) is { } spent)
            throw new ReviewAccessException(DeveloperBudget.Stop(spent));
        if (workflow.State == "handoff-attention" && HandoffStarted(workflow))
            throw new ReviewAccessException(
                "This attempt already began a SemaForge handoff or used an older checkout location, so it cannot be replayed safely. Ask the agent for a fresh strategy on this PR."
            );
        var request =
            requests.FindRequest(workflow.RequestId)
            ?? throw new ReviewAccessException("The conversation no longer exists.", 404);
        if (GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id)) != workflow.MessageKey)
            throw new ReviewAccessException("The conversation changed. Ask for a fresh strategy before confirming.");
        var evidence = await CurrentAsync(workflow, token);
        if (evidence.Snapshot != workflow.EvidenceSnapshot)
            throw new ReviewAccessException("The PR evidence changed. Ask for a fresh strategy.");
        if (workflow.Strategy.Questions.Length != 0)
            throw new ReviewAccessException(
                "Answer the proposal's questions in chat before confirming a fresh strategy."
            );
        if (workflow.Strategy.BusinessRuleChanges.Length > 0 && !workflow.RulesApproved)
            throw new ReviewAccessException(
                "Approve the specific business-rule changes separately before executing this strategy."
            );
        InScope(
            workflow,
            () =>
            {
                store.ClaimApproval(workflow, "repair", snapshot);
                return true;
            }
        );
        // Consume and journal before any worktree/task creation. An uncertain handoff is never replayed.
        workflow = workflow with
        {
            State = "preparing",
            Error = null,
        };
        Save(workflow);
        try
        {
            var source = await InScope(
                workflow,
                () => github.ResolveSourceAsync(store.Repositories(workflow.AccountId), workflow, token)
            );
            source = InScope(workflow, () => store.Add(source));
            evidence = await InScope(
                workflow,
                () => github.GatherEvidenceAsync(evidence, workflow.Strategy.EvidenceRequests, token)
            );
            // A retry reuses the worktree the failed attempt already created and journaled.
            var checkout =
                workflow.RepositoryId is not null
                && store.Repositories(workflow.AccountId).FirstOrDefault(r => r.Id == workflow.RepositoryId)
                    is { } existing
                    ? existing
                    : store.Add(await InScope(workflow, () => github.CheckoutAsync(source, workflow, token)));
            workflow = workflow with { RepositoryId = checkout.Id };
            Save(workflow);
            if (coding is not null)
            {
                // Direct path: the strategy is already scoped and validated here, so Sol starts in the worktree right away.
                var started = InScope(
                    workflow,
                    () =>
                    {
                        var run = coding.Start(
                            new CodingRunRequest(
                                checkout.RootPath,
                                DirectCoding.Brief(
                                    workflow,
                                    evidence,
                                    workflow.OwnerNotes is { Length: > 0 } notes ? string.Join("\n---\n", notes) : null
                                ),
                                DirectCoding.ResultSchema,
                                PullRequestPolicy.BuilderModel,
                                "high",
                                AllowNetwork: true,
                                Environment: DirectCoding.Environment(checkout.RootPath, workflow.Repository)
                            )
                        );
                        workflow = workflow with { CodingRunId = run.Id, State = "coding", RunState = run.State };
                        store.SaveWorkflow(workflow);
                        return run;
                    }
                );
                requests.LinkConfirmedWork(workflow.RequestId, "coding:" + started.Id);
                Notify(
                    workflow,
                    "Strategy approved. Sol 6.1 high is fixing and testing it in the PR worktree. Publication waits for your separate approval of the reviewed changes."
                );
                return workflow;
            }
            var fix = await InScope(workflow, () => coordinator.PreparePlanAsync(checkout, workflow, token, evidence));
            workflow = workflow with { FixId = fix.Id, State = "scoping" };
            Save(workflow);
            if (fix.WorkItemId is not null)
                requests.LinkConfirmedWork(workflow.RequestId, fix.WorkItemId);
            Notify(
                workflow,
                "Strategy approved. The agent is gathering repository details and validating the scope. Publication waits for your separate approval of the reviewed changes."
            );
            return workflow;
        }
        catch (Exception error)
        {
            try
            {
                InScope(workflow, () => true);
            }
            catch (WorkspaceException)
            {
                await StopRevokedAsync(workflow);
                throw;
            }
            // ReviewAccessException messages are curated and safe to show; anything else stays generic.
            var cause = error is ReviewAccessException access
                ? access.Message
                : "An unexpected error interrupted the checkout or SemaForge handoff.";
            Save(
                workflow with
                {
                    State = "handoff-attention",
                    Error =
                        "Handoff stopped before coding started: "
                        + cause
                        + (
                            HandoffStarted(workflow)
                                ? " Ask the agent for a fresh strategy on this PR."
                                : " Nothing was started, so you can retry."
                        ),
                }
            );
            throw;
        }
    }

    // True once any durable fix row exists (it may hold a SemaForge task), or the checkout sits under AppData,
    // where a packaged launcher hides it from SemaForge.
    private bool HandoffStarted(PullRequestWorkflow workflow) =>
        workflow.FixId is not null
        || workflow.CodingRunId is not null
        || workflow.RepositoryId is not null
            && (
                store.Fixes(workflow.RepositoryId).Length > 0
                || store.Repositories(workflow.AccountId).FirstOrDefault(r => r.Id == workflow.RepositoryId)
                    is { } checkout
                    && GitHubReviewClient.RedirectedPath(checkout.RootPath)
            );

    public async Task<PullRequestWorkflow> ApproveRulesAsync(
        PullRequestWorkflow workflow,
        string snapshot,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State is "proposed" or "needs-evidence")
        {
            if (snapshot != workflow.EvidenceSnapshot || (await CurrentAsync(workflow, token)).Snapshot != snapshot)
                throw new ReviewAccessException("The evidence changed. Ask for a fresh business-rule proposal.");
            if (workflow.Strategy.BusinessRuleChanges.Length == 0)
                throw new ReviewAccessException("This proposal has no business-rule changes to approve.");
            workflow = workflow with { RulesApproved = true };
            Save(workflow);
            Notify(
                workflow,
                "You approved the listed new business-rule changes; coding still waits for Approve strategy and run."
            );
            return workflow;
        }
        // Unexpected changes are never retroactively approved after implementation. Stop and propose a new strategy.
        throw new ReviewAccessException(
            "This run stopped for an unapproved behavior change. Discuss it in chat and confirm a new strategy before further coding."
        );
    }

    // A paused read-only scope can go back to polling; every later gate (evidence snapshot, exact scope,
    // approval) is checked again. Never once a builder exists or its approval handoff is uncertain.
    public async Task<PullRequestWorkflow> ResumeAsync(PullRequestWorkflow workflow, CancellationToken token)
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State != "needs-attention" || workflow.RepositoryId is null || workflow.FixId is null)
            throw new ReviewAccessException("Only a paused scope that hasn't started coding can resume.");
        var fix = store.Fixes(workflow.RepositoryId).SingleOrDefault(f => f.Id == workflow.FixId);
        if (fix is null || fix.BuilderId is not null || fix.Status is "approval-pending" or "canceled")
            throw new ReviewAccessException(
                "Coding already started or its handoff is uncertain. Inspect it in SemaForge instead of resuming."
            );
        await CurrentAsync(workflow, token);
        workflow = workflow with { State = "scoping", Error = null };
        Save(workflow);
        return workflow;
    }

    // Your answer to the read-only scout's question resumes scoping; coding still waits for its exact proposal.
    public async Task<PullRequestWorkflow> AnswerAsync(
        PullRequestWorkflow workflow,
        string questionId,
        string answer,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State != "needs-input" || workflow.RepositoryId is null || workflow.FixId is null)
            throw new ReviewAccessException("This run isn't waiting on a scoping question.");
        if (string.IsNullOrWhiteSpace(answer) || answer.Trim().Length > 2000)
            throw new ReviewAccessException("Answer in 1 to 2,000 characters.", 400);
        await CurrentAsync(workflow, token);
        var repository = await coordinator.AuthorizedAsync(workflow.RepositoryId, token);
        var fix = store.Fixes(repository.Id).Single(f => f.Id == workflow.FixId);
        await InScope(workflow, () => coordinator.AnswerAsync(repository, fix, questionId, answer.Trim(), token));
        workflow = workflow with { State = "scoping", RunState = "queued", Error = null };
        Save(workflow);
        Notify(workflow, "You answered the scout's question; it is re-checking the scope before Sol 6.1 high starts.");
        return workflow;
    }

    // Run the approved strategy again after a stop, with an optional note from the owner. The PR must be unchanged,
    // the previous attempt must have ended, and the attempt restarts from a clean worktree at the PR head.
    public async Task<PullRequestWorkflow> RetryAsync(
        PullRequestWorkflow workflow,
        string? note,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (coding is null)
            throw new ReviewAccessException("Running again needs the direct Codex runner.");
        if (
            workflow.State
            is not ("needs-attention" or "needs-input" or "validation-attention" or "handoff-attention" or "canceled")
        )
            throw new ReviewAccessException(
                workflow.State == "business-rule-attention"
                    ? "This attempt found an unapproved behavior change. Discuss it and approve a new strategy instead of running again."
                    : "Only a stopped fix can run again."
            );
        if (workflow.Strategy.BusinessRuleChanges.Length > 0 && !workflow.RulesApproved)
            throw new ReviewAccessException("Approve the strategy's business-rule changes first.");
        var clean = note?.Trim();
        if (clean is { Length: > 2000 })
            throw new ReviewAccessException("Keep the note under 2,000 characters.", 400);
        var evidence = await CurrentAsync(workflow, token);
        if (evidence.Snapshot != workflow.EvidenceSnapshot)
            throw new ReviewAccessException(
                "The PR changed since you approved this strategy. Ask for a fresh strategy."
            );
        if (workflow.CodingRunId is { } previousRun && coding.Find(previousRun) is { State: "queued" or "running" })
            throw new ReviewAccessException("The previous attempt is still running. Stop it first.");
        if (
            workflow.FixId is not null
            && workflow.RunState
                is not (null or "failed" or "canceled" or "input-required" or "completed" or "interrupted")
        )
            throw new ReviewAccessException(
                "The previous SemaForge attempt may still be running. Cancel the fix first."
            );
        var proposal = workflow with
        {
            Id = Guid.NewGuid().ToString("N"),
            State = "proposed",
            Error = null,
            MessageKey = GitHubRepositoryChat.ProposalKey(
                requests.FindRequest(workflow.RequestId)!,
                requests.Messages(workflow.RequestId)
            ),
            RepositoryId = null,
            FixId = null,
            CodingRunId = null,
            RunState = null,
            ReviewedDiff = null,
            RuleConcerns = null,
            CommitSha = null,
            SensitivePending = null,
            SensitiveFindings = null,
            SensitiveAck = null,
            ProofAck = null,
            OwnerNotes = clean is { Length: > 0 } ? [.. workflow.OwnerNotes ?? [], clean] : workflow.OwnerNotes,
        };
        InScope(
            workflow,
            () =>
            {
                store.SaveWorkflow(proposal);
                return true;
            }
        );
        Notify(
            proposal,
            "A fresh retry proposal is ready. Review its scope and approve it before another isolated worktree or coding run starts."
        );
        return proposal;
    }

    // Sol's proof ran somewhere the publisher can't count (a local database harness, say). The owner reproduced the
    // failing-then-passing tests themselves: record what they ran, bind it to exactly this diff, and check again.
    public async Task<PullRequestWorkflow> ConfirmProofAsync(
        PullRequestWorkflow workflow,
        string? note,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (
            workflow.State != "validation-attention"
            || workflow.Error?.StartsWith("Missing command evidence", StringComparison.Ordinal) != true
            || workflow.CodingRunId is not { } runId
            || coding?.Find(runId) is not { State: "completed" } coded
            || workflow.RepositoryId is null
        )
            throw new ReviewAccessException("Only a finished run that's missing test proof can be verified by you.");
        var clean = note?.Trim();
        if (clean is not { Length: >= 10 and <= 2000 })
            throw new ReviewAccessException(
                "Say what you ran and what it showed (before failing, after passing).",
                400
            );
        var repository = await coordinator.AuthorizedAsync(workflow.RepositoryId, token);
        var diff = await github.DiffAsync(repository, workflow, DirectCoding.AsRun(coded), token);
        workflow = workflow with
        {
            ProofAck = diff.Hash,
            OwnerNotes = [.. workflow.OwnerNotes ?? [], "Owner verified the tests: " + clean],
        };
        Save(workflow);
        Notify(
            workflow with
            {
                State = "proof-confirmed-" + diff.Hash[..12],
            },
            "You verified the tests for this fix ("
                + PullRequestPolicy.Safe(clean, 300)
                + "). Checking Sol's work again."
        );
        return await RevalidateAsync(workflow, null, token);
    }

    // The owner looked at the flagged lines and confirmed they're placeholders: bind that to exactly those lines and check again.
    public async Task<PullRequestWorkflow> ConfirmSensitiveAsync(
        PullRequestWorkflow workflow,
        string? hash,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State != "validation-attention" || workflow.SensitivePending is not { } pending || hash != pending)
            throw new ReviewAccessException("These lines changed since they were flagged. Check the latest notice.");
        workflow = workflow with { SensitiveAck = pending };
        Save(workflow);
        Notify(
            workflow with
            {
                State = "sensitive-confirmed-" + pending[..12],
            },
            "You confirmed the flagged lines are placeholders ("
                + string.Join(", ", workflow.SensitiveFindings ?? [])
                + "). Checking Sol's work again."
        );
        return await RevalidateAsync(workflow, null, token);
    }

    // Re-check a finished run's work (for example after its validation was blocked) without running Sol again.
    public async Task<PullRequestWorkflow> RevalidateAsync(
        PullRequestWorkflow workflow,
        string? note,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        // needs-attention: validation itself failed after Sol finished (a model or GitHub hiccup), so the finished work can be checked again.
        if (
            workflow.State
                is not ("validation-attention" or "business-rule-attention" or "needs-input" or "needs-attention")
            || workflow.CodingRunId is not { } runId
            || coding?.Find(runId) is not { State: "completed" } finished
            || workflow.State == "needs-input" && !DirectCoding.Unstructured(finished)
        )
            throw new ReviewAccessException("Only a finished run whose validation stopped can be checked again.");
        var clean = note?.Trim();
        if (clean is { Length: > 2000 })
            throw new ReviewAccessException("Keep the note under 2,000 characters.", 400);
        if (workflow.State == "business-rule-attention" && clean is not { Length: > 0 })
            throw new ReviewAccessException("Add your decision on the concern above, then check again.", 400);
        var evidence = await CurrentAsync(workflow, token);
        if (evidence.Snapshot != workflow.EvidenceSnapshot)
            throw new ReviewAccessException(
                "The PR changed since you approved this strategy. Run it again or ask for a fresh strategy."
            );
        workflow = workflow with
        {
            State = "validating",
            Error = null,
            ReviewedDiff = null,
            RuleConcerns = null,
            SensitivePending = null,
            SensitiveFindings = null,
            OwnerNotes = clean is { Length: > 0 } ? [.. workflow.OwnerNotes ?? [], clean] : workflow.OwnerNotes,
        };
        Save(workflow);
        if (clean is { Length: > 0 })
            Notify(workflow, "Your decision was recorded on this fix, and its finished work is being checked again.");
        return workflow;
    }

    public async Task ValidateDenialAsync(PullRequestWorkflow workflow, string? snapshot, CancellationToken token)
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        if (
            snapshot
            != (workflow.State == "publication-proposed" ? workflow.PublicationSnapshot : workflow.ApprovalSnapshot)
        )
            throw new ReviewAccessException("This proposal changed. Refresh before denying it.");
        await CurrentAsync(workflow, token);
    }

    public async Task<PullRequestWorkflow> CancelAsync(PullRequestWorkflow workflow, CancellationToken token)
    {
        if (workflow.State is "committing" or "pushing" or "merge-pending" or "published" or "merged")
            throw new ReviewAccessException(
                "A publication handoff cannot be canceled safely. Inspect its recorded revision first."
            );
        if (workflow.RepositoryId is not null && workflow.FixId is not null)
        {
            var fix = store.Fixes(workflow.RepositoryId).Single(f => f.Id == workflow.FixId);
            await coordinator.CancelAsync(fix, token);
        }
        if (workflow.CodingRunId is { } runId)
            coding?.Cancel(runId);
        workflow = workflow with { State = "canceled" };
        store.SaveWorkflow(workflow);
        return workflow;
    }

    public async Task<PullRequestWorkflow> MergeAsync(
        PullRequestWorkflow workflow,
        string headSha,
        string snapshot,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State is not ("proposed" or "published" or "canceled"))
            throw new ReviewAccessException(
                "Finish or cancel active work and reconcile pending publication before merging."
            );
        var evidence = await CurrentAsync(workflow, token, requireOriginalHead: false);
        var gate = PullRequestPolicy.Merge(evidence, await github.IdentityAsync(token));
        if (!gate.Ready || headSha != gate.HeadSha || snapshot != gate.Snapshot)
            throw new ReviewAccessException(
                gate.Ready
                    ? "The changeset or reviews changed. Refresh the merge checks and confirm again."
                    : string.Join(" ", gate.Blockers)
            );
        workflow = workflow with { State = "merge-pending" };
        Save(workflow);
        try
        {
            await InScope(workflow, () => github.MergeAsync(evidence, token));
        }
        catch
        {
            Save(
                workflow with
                {
                    State = "merge-attention",
                    Error = "The merge outcome is uncertain. Inspect GitHub before another action.",
                }
            );
            throw;
        }
        workflow = workflow with { State = "merged", Error = null };
        Save(workflow);
        Notify(
            workflow,
            "You confirmed the merge after green checks, resolved review threads and a clean mergeable revision; GitHub confirmed it merged."
        );
        return workflow;
    }

    public async Task<PullRequestEvidence> CurrentAsync(
        PullRequestWorkflow workflow,
        CancellationToken token,
        bool requireOriginalHead = true
    )
    {
        var account = await github.IdentityAsync(token);
        var evidence = await github.EvidenceAsync(workflow.Repository, workflow.Number, false, token);
        if (
            account.Id != workflow.AccountId
            || evidence.RepositoryId != workflow.RepositoryGitHubId
            || !evidence.Open
            || !evidence.Complete
            || !evidence.Author.Equals(account.Login, StringComparison.OrdinalIgnoreCase)
            || requireOriginalHead && evidence.HeadSha != workflow.HeadSha
        )
            throw new ReviewAccessException(
                "The account, PR ownership, evidence completeness or approved revision changed."
            );
        InScope(workflow, () => true);
        return evidence;
    }

    public async Task AdvanceAsync(PullRequestWorkflow workflow, CancellationToken token)
    {
        try
        {
            using var usage = UsageMeter.For(DeveloperBudget.Role, workflow.RequestId);
            using var consent = Lease(workflow, token);
            using var effects = AgentEffectBoundary.Enter(action =>
                InScope(
                    workflow,
                    () =>
                    {
                        action();
                        return true;
                    }
                )
            );
            await AdvanceAllowedAsync(workflow, consent.Token);
            InScope(workflow, () => true);
        }
        catch (Exception error) when (error is WorkspaceException or OperationCanceledException)
        {
            if (token.IsCancellationRequested)
                throw;
            try
            {
                InScope(workflow, () => true);
            }
            catch (WorkspaceException)
            {
                await StopRevokedAsync(workflow);
                return;
            }
            throw;
        }
    }

    private async Task AdvanceAllowedAsync(PullRequestWorkflow workflow, CancellationToken token)
    {
        if (workflow.CodingRunId is { } codingRunId)
        {
            await AdvanceDirectAsync(workflow, codingRunId, token);
            return;
        }
        if (workflow.State is not ("scoping" or "coding" or "validating" or "validation-attention"))
            return;
        if (workflow.RepositoryId is null || workflow.FixId is null)
            return;
        var repository = await coordinator.AuthorizedAsync(workflow.RepositoryId, token);
        var fix = store.Fixes(repository.Id).Single(f => f.Id == workflow.FixId);
        var run = await coordinator.RunAsync(fix, token);
        var state = run.GetProperty("state").GetString();
        workflow = workflow with { RunState = state };
        Save(workflow);
        if (workflow.State == "scoping" && state == "completed")
        {
            await InScope(workflow, () => coordinator.FollowDeepScopeAsync(repository, fix, token));
            return;
        }
        if (state is "failed" or "canceled" or "input-required")
        {
            var who = fix.BuilderId is null ? "The read-only scout" : "Sol's coding run";
            var asked =
                run.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array
                    ? questions
                        .EnumerateArray()
                        .Where(item =>
                            item.TryGetProperty("state", out var open)
                            && open.GetString() == "open"
                            && item.TryGetProperty("prompt", out var prompt)
                            && prompt.ValueKind == JsonValueKind.String
                        )
                        .Select(item => PullRequestPolicy.Safe(item.GetProperty("prompt").GetString(), 600))
                        .Take(3)
                        .ToArray()
                    : [];
            var reason =
                run.TryGetProperty("error", out var failure) && failure.ValueKind == JsonValueKind.String
                    ? PullRequestPolicy.Safe(failure.GetString(), 400)
                    : null;
            var message =
                state == "input-required"
                    ? who
                        + (
                            fix.BuilderId is null
                                ? " has a question. Answer it below to continue; nothing was changed."
                                : " has a question. It can't resume mid-run: answer it in chat and ask for a fresh strategy. Nothing was committed."
                        )
                    : who
                        + (
                            state == "canceled"
                                ? " was canceled."
                                : " failed before finishing" + (reason is null ? "." : ": " + reason)
                        )
                        + " Nothing was committed or pushed. Ask for a fresh strategy to try again.";
            var stopped = workflow with
            {
                State = state == "input-required" ? "needs-input" : "needs-attention",
                Error = message,
            };
            Save(stopped);
            // The question belongs in the conversation, not only on the card.
            Notify(
                stopped,
                message + (asked.Length == 0 ? "" : "\n\n" + string.Join("\n", asked.Select(text => "- " + text)))
            );
            return;
        }
        if (workflow.State == "scoping")
        {
            if (state != "awaiting-approval")
                return;
            var evidence = await CurrentAsync(workflow, token);
            if (evidence.Snapshot != workflow.EvidenceSnapshot)
                throw new ReviewAccessException("GitHub evidence changed during scoping. Request a fresh strategy.");
            fix = await InScope(
                workflow,
                () =>
                    coordinator.ApproveAsync(
                        repository,
                        fix,
                        run.GetProperty("contextHash").GetString()!,
                        run.GetProperty("proposalVersion").GetInt32(),
                        token,
                        workflow
                    )
            );
            workflow = workflow with { State = "coding", RunState = "queued" };
            Save(workflow);
            Notify(workflow, "Sol 6.1 high is queued to implement and test your confirmed fix.");
            return;
        }
        if (
            run.GetProperty("requestedModel").GetString() != PullRequestPolicy.BuilderModel
            || run.GetProperty("reasoningEffort").GetString() != "high"
        )
        {
            await coordinator.CancelAsync(fix, token);
            throw new ReviewAccessException(
                "The coding run does not match Sol 6.1 high. Fix the worker configuration before confirming another strategy."
            );
        }
        if (state != "completed")
            return;
        await DeliverAsync(workflow, repository, run, token);
    }

    private async Task AdvanceDirectAsync(PullRequestWorkflow workflow, string codingRunId, CancellationToken token)
    {
        if (
            workflow.State is not ("coding" or "validating" or "validation-attention")
            || workflow.RepositoryId is null
            || coding is null
        )
            return;
        var coded =
            coding.Find(codingRunId)
            ?? throw new ReviewAccessException("The coding run record is missing. Ask for a fresh strategy.");
        if (workflow.RunState != coded.State)
        {
            workflow = workflow with { RunState = coded.State };
            Save(workflow);
        }
        if (coded.State is "queued" or "running")
            return;
        budgets?.RecordCoding(coded, DeveloperBudget.Role, workflow.RequestId);
        if (coded.State is not "completed")
        {
            var message =
                "Sol's coding run "
                + (
                    coded.State == "canceled"
                        ? "was canceled."
                        : "stopped before finishing" + (coded.Error is null ? "." : ": " + coded.Error)
                )
                + " Nothing was committed or pushed. Ask for a fresh strategy to try again.";
            var stopped = workflow with { State = "needs-attention", Error = message };
            Save(stopped);
            Notify(stopped, message);
            return;
        }
        var asked = DirectCoding.Questions(coded);
        if (asked.Length > 0)
        {
            var message =
                "Sol has a question before this can be delivered. Answer it in the Run again note on the fix card and run it again; nothing was committed.";
            var stopped = workflow with { State = "needs-input", Error = message };
            Save(stopped);
            Notify(stopped, message + "\n\n" + string.Join("\n", asked.Select(text => "- " + text)));
            return;
        }
        // Sol did the work but its final report came back unreadable: take the changed files from the worktree and let every
        // publication check (proof, flagged lines, business rules on the real diff) decide, instead of calling it a stop.
        if (
            DirectCoding.Unstructured(coded)
            && coded.Events.Any(item => item.Type == "file-changed")
            && workflow.RepositoryId is not null
        )
        {
            var checkout = await coordinator.AuthorizedAsync(workflow.RepositoryId, token);
            string[] changed =
            [
                .. (
                    await github.GitAsync(
                        checkout.RootPath,
                        ["ls-files", "-m", "-o", "--exclude-standard", "-z"],
                        token
                    )
                )
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Concat(
                        (await github.GitAsync(checkout.RootPath, ["ls-files", "-d", "-z"], token)).Split(
                            '\0',
                            StringSplitOptions.RemoveEmptyEntries
                        )
                    )
                    .Where(path => !path.StartsWith(DirectCoding.EvidenceFolder + "/", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal),
            ];
            if (changed.Length > 0)
            {
                coded = DirectCoding.WithChangedFiles(coded, changed);
                AgentActivity.Note(
                    "Sol's final report was unreadable; checking the "
                        + changed.Length
                        + " changed file"
                        + (changed.Length == 1 ? "" : "s")
                        + " in the worktree instead."
                );
            }
        }
        if (DirectCoding.StoppedWithoutChanges(coded) is { } stop)
        {
            var message =
                "Sol stopped before changing anything and needs your decision. Nothing was committed. Answer in the Run again note (you can widen the scope there), or ask for a fresh strategy.";
            var stopped = workflow with { State = "needs-input", Error = message + "\n\n" + stop.Summary };
            Save(stopped);
            Notify(
                stopped,
                message
                    + "\n\n"
                    + stop.Summary
                    + (stop.Risks.Length == 0 ? "" : "\n\n" + string.Join("\n", stop.Risks.Select(text => "- " + text)))
            );
            return;
        }
        if (workflow.State == "validation-attention")
            return;
        var repository = await coordinator.AuthorizedAsync(workflow.RepositoryId!, token);
        AgentActivity.Note(
            "Checking Sol's work for " + workflow.Repository + " #" + workflow.Number + " before publishing."
        );
        await DeliverAsync(workflow, repository, DirectCoding.AsRun(coded), token);
    }

    // Shared by both coding paths: proof, business-rule review, exact diff, commit, push and GitHub confirmation.
    private async Task DeliverAsync(
        PullRequestWorkflow workflow,
        ReviewRepository repository,
        JsonElement run,
        CancellationToken token,
        string? publicationApproval = null
    )
    {
        if (publicationApproval is null)
        {
            workflow = workflow with { State = "validating" };
            Save(workflow);
        }
        var proof = GitHubPublication.Evidence(run);
        var ownerVerified = !proof.Reproduced && proof.Broken.Length == 0 && workflow.ProofAck is not null;
        if ((!proof.Reproduced || proof.Broken.Length > 0) && !ownerVerified)
        {
            var reason =
                proof.Broken.Length > 0
                    ? "A check that passed earlier in the run fails at the end, so this isn't published: "
                        + string.Join(", ", proof.Broken.Take(3))
                        + ". Run it again with a note, or ask for a fresh strategy."
                    : "Missing command evidence for a regression that fails before and passes after. Sol's narrative alone cannot authorize publication.";
            Save(workflow with { State = "validation-attention", Error = reason });
            Notify(workflow with { State = "validation-attention" }, reason);
            return;
        }
        (string Diff, string Hash, string[] Files) diff;
        try
        {
            diff = await github.DiffAsync(repository, workflow, run, token);
        }
        catch (SensitiveContentException sensitive)
        {
            var paused = workflow with
            {
                State = "validation-attention",
                Error = sensitive.Message,
                SensitivePending = sensitive.Hash,
                SensitiveFindings = [.. sensitive.Lines.Select(PullRequestPolicy.Describe)],
            };
            Save(paused);
            Notify(paused, sensitive.Message);
            return;
        }
        if (ownerVerified && diff.Hash != workflow.ProofAck)
        {
            const string changed =
                "The code changed after you verified its tests. Verify it again before it's published.";
            var paused = workflow with
            {
                State = "validation-attention",
                Error = "Missing command evidence for a regression that fails before and passes after. " + changed,
                ProofAck = null,
            };
            Save(paused);
            Notify(paused, changed);
            return;
        }
        if (workflow.ReviewedDiff != diff.Hash)
        {
            if (publicationApproval is not null)
                throw new ReviewAccessException(
                    "Local code changed after publication was proposed. Review a fresh proposal."
                );
            using var audit = JsonDocument.Parse(
                await InScope(
                    workflow,
                    () =>
                        cells.ReviewBusinessRulesAsync(
                            DirectCoding.ReviewScope(workflow),
                            DirectCoding.ForRuleReview(diff.Diff),
                            token
                        )
                )
            );
            var concerns = audit
                .RootElement.GetProperty("concerns")
                .EnumerateArray()
                .Select(c => PullRequestPolicy.Safe(c.GetString(), 600))
                .Take(5)
                .ToArray();
            if (!audit.RootElement.GetProperty("clear").GetBoolean() || concerns.Length != 0)
            {
                Save(
                    workflow with
                    {
                        State = "business-rule-attention",
                        ReviewedDiff = diff.Hash,
                        RuleConcerns = concerns,
                        Error =
                            "An unapproved behavior change or ambiguity was found. Discuss it and approve a new strategy before further coding; nothing was committed.",
                    }
                );
                Notify(
                    workflow,
                    "The fix paused for a business-rule question; please review the proposed behavior in chat before further coding or publication."
                );
                return;
            }
            workflow = workflow with { ReviewedDiff = diff.Hash };
            Save(workflow);
        }
        var current = await CurrentAsync(workflow, token);
        if (current.Snapshot != workflow.EvidenceSnapshot)
            throw new ReviewAccessException("The PR or review evidence changed before publication.");
        var verified = await github.DiffAsync(repository, workflow, run, token);
        if (verified.Hash != workflow.ReviewedDiff)
            throw new ReviewAccessException("Local code changed after review. Publication paused.");
        if (publicationApproval is null)
        {
            workflow = workflow with { State = "publication-proposed", Error = null };
            Save(workflow);
            InScope(
                workflow,
                () =>
                {
                    store.RegisterPublication(workflow);
                    return true;
                }
            );
            Notify(
                workflow,
                "The fix passed validation. Review the changes and approve publication before this workspace commits or pushes."
            );
            return;
        }
        InScope(
            workflow,
            () =>
            {
                store.ClaimApproval(workflow, "publish", publicationApproval);
                return true;
            }
        );
        workflow = workflow with { State = "committing", Error = null };
        Save(workflow);
        var commit = await InScope(workflow, () => github.CommitAsync(repository, workflow, verified.Files, token));
        workflow = workflow with { CommitSha = commit, State = "pushing" };
        store.SaveWorkflow(workflow);
        InScope(workflow, () => true);
        await InScope(workflow, () => github.PushAsync(repository, current, commit, token));
        if (await DeliveredHeadAsync(workflow, token) != commit)
            throw new ReviewAccessException(
                "GitHub has not confirmed the delivered commit. Inspect the publication receipt before retrying."
            );
        workflow = workflow with { State = "published", Error = null };
        Save(workflow);
        Notify(
            workflow,
            "Sol’s fix passed the recorded regression, was committed and updated the PR; merge remains gated by current checks, review threads and GitHub mergeability."
                + (
                    proof.Unverified.Length == 0
                        ? ""
                        : "\n\nNot verified on this computer (the PR's CI runs them):\n"
                            + string.Join("\n", proof.Unverified.Take(8).Select(command => "- " + command))
                )
        );
    }

    public async Task<PullRequestWorkflow> PublishAsync(
        PullRequestWorkflow workflow,
        string snapshot,
        CancellationToken token
    )
    {
        using var consent = Lease(workflow, token);
        token = consent.Token;
        using var effects = AgentEffectBoundary.Enter(action =>
            InScope(
                workflow,
                () =>
                {
                    action();
                    return true;
                }
            )
        );
        if (workflow.State != "publication-proposed" || snapshot != workflow.PublicationSnapshot)
            throw new ReviewAccessException("Approve the current reviewed changes before publication.");
        var request =
            requests.FindRequest(workflow.RequestId)
            ?? throw new ReviewAccessException("The conversation no longer exists.", 404);
        if (GitHubRepositoryChat.ProposalKey(request, requests.Messages(request.Id)) != workflow.MessageKey)
            throw new ReviewAccessException("The conversation changed. Review a fresh publication proposal.");
        var repository = store.Repositories(workflow.AccountId).Single(item => item.Id == workflow.RepositoryId);
        JsonElement run;
        if (workflow.CodingRunId is { } id && coding?.Find(id) is { State: "completed" } completed)
            run = DirectCoding.AsRun(completed);
        else if (workflow.FixId is { } fixId)
            run = await coordinator.RunAsync(store.Fixes(repository.Id).Single(item => item.Id == fixId), token);
        else
            throw new ReviewAccessException("The completed repair evidence is unavailable.");
        await DeliverAsync(workflow, repository, run, token, snapshot);
        return store.Workflows(workflow.AccountId).Single(item => item.Id == workflow.Id);
    }

    // Right after a push GitHub is still updating the PR, so reading it back can race; retry briefly before calling it unconfirmed.
    private async Task<string?> DeliveredHeadAsync(PullRequestWorkflow workflow, CancellationToken token)
    {
        string? head = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            try
            {
                head = (await github.EvidenceAsync(workflow.Repository, workflow.Number, false, token)).HeadSha;
            }
            catch (ReviewAccessException) when (attempt < 5)
            {
                continue;
            }
            if (head == workflow.CommitSha)
                return head;
        }
        return head;
    }

    // A push whose confirmation was lost: if GitHub's PR head is exactly the commit we pushed, it was delivered.
    public async Task<PullRequestWorkflow> ReconcilePublicationAsync(
        PullRequestWorkflow workflow,
        CancellationToken token
    )
    {
        if (workflow.State != "publication-attention" || workflow.CommitSha is not { } commit)
            throw new ReviewAccessException("Only a fix whose push wasn't confirmed can be checked against GitHub.");
        if (await DeliveredHeadAsync(workflow, token) != commit)
            throw new ReviewAccessException(
                "GitHub's PR head isn't the pushed commit "
                    + commit[..8]
                    + ". Inspect the PR before doing anything else."
            );
        workflow = workflow with { State = "published", Error = null };
        Save(workflow);
        Notify(
            workflow,
            "GitHub confirms commit "
                + commit[..8]
                + " is the PR head, so the fix was delivered. Merge remains gated by current checks, review threads and GitHub mergeability."
        );
        return workflow;
    }

    // When GitHub signs out, every conversation with PR work in flight hears about it once, with how to reconnect.
    public void NotifySignedOut()
    {
        var since = GitHubHealth.SignedOutSince ?? DateTimeOffset.UtcNow.ToString("O");
        foreach (
            var requestId in store
                .Workflows()
                .Where(w => w.State is not ("merged" or "canceled" or "published"))
                .Select(w => w.RequestId)
                .Distinct()
                .Take(5)
        )
            requests.AddOwnerAgentNotice(requestId, GitHubHealth.Message, "github-signed-out:" + since);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GitHubHealth.SignedOut += NotifySignedOut;
        requests.AgentWorkRevoked += OnRequestChanged;
        try
        {
            await WorkAsync(stoppingToken);
        }
        finally
        {
            GitHubHealth.SignedOut -= NotifySignedOut;
            requests.AgentWorkRevoked -= OnRequestChanged;
        }
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        // No inference or Git operations when there is no explicitly confirmed workflow.
        while (!stoppingToken.IsCancellationRequested)
        {
            while (_revokedRequests.TryDequeue(out var requestId))
                foreach (
                    var workflow in store
                        .Workflows()
                        .Where(item =>
                            item.RequestId == requestId
                            && item.State is not ("consent-revoked" or "scope-changed" or "merged" or "canceled")
                        )
                )
                {
                    try
                    {
                        InScope(workflow, () => true);
                    }
                    catch (WorkspaceException)
                    {
                        await StopRevokedAsync(workflow);
                    }
                }
            // Signed out: work waits where it is; a reconnect lets it continue.
            if (GitHubHealth.SignedOutSince is not null)
            {
                await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
                continue;
            }
            foreach (var workflow in store.Workflows().Where(w => w.State is "scoping" or "coding" or "validating"))
            {
                await coordinator.Gate.WaitAsync(stoppingToken);
                try
                {
                    var current = store.Workflows(workflow.AccountId).Single(w => w.Id == workflow.Id);
                    using var execution = AgentActivity.Begin("workflow:" + workflow.Id);
                    await AdvanceAsync(current, stoppingToken);
                }
                catch (GitHubSignInException) { }
                catch (Exception error)
                    when (error
                            is WorkspaceException
                                or OperationCanceledException
                                or ModelUnavailableException
                                or JsonException
                                or InvalidOperationException
                    )
                {
                    var current = store.Workflows(workflow.AccountId).Single(w => w.Id == workflow.Id);
                    if (current.State is "consent-revoked" or "scope-changed")
                        continue;
                    var stopped = current with
                    {
                        State = current.State is "committing" or "pushing"
                            ? "publication-attention"
                            : "needs-attention",
                        Error = error is ReviewAccessException access
                            ? access.Message
                            : "Workflow validation is unavailable ("
                                + PullRequestPolicy.Safe(error.GetType().Name + ": " + error.Message, 300)
                                + "). No further action will run until it is reviewed.",
                    };
                    Save(stopped);
                    // Never stop quietly: the conversation says why, and whether finished work is still there.
                    Notify(
                        stopped,
                        "The fix for "
                            + stopped.Repository
                            + " #"
                            + stopped.Number
                            + " stopped: "
                            + stopped.Error
                            + (
                                current.CodingRunId is not null && current.State == "validating"
                                    ? " Sol's finished work is still in the worktree; running again starts over from a clean checkout."
                                    : ""
                            )
                    );
                }
                finally
                {
                    coordinator.Gate.Release();
                }
            }
            await _revocationWake.WaitAsync(TimeSpan.FromSeconds(8), stoppingToken);
        }
    }

    private void Notify(PullRequestWorkflow workflow, string body) =>
        requests.AddOwnerAgentNotice(
            workflow.RequestId,
            body,
            "pr-workflow:" + workflow.Id + ":" + workflow.State + (workflow.CodingRunId is { } run ? ":" + run : "")
        );
}
