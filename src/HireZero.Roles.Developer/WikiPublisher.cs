namespace HireZero.Roles.Developer;

/// <summary>Applies the owner's publication policy through native Wiki APIs; never exposed as an agent tool.</summary>
public sealed class WikiPublisher(WikiDraftStore store, RequestStore requests, WikiProposalAdapter adapter)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<WikiDraft> PublishAsync(string id, int version, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var prior = store.Find(id)!;
            if (prior.Version != version) return prior;
            if (prior.Status is "publishing" or "approved" or "quarantined" or "needs-recheck") return prior;
            var draft = store.Reserve(id, version);
            if (draft is null) return prior;
            try
            {
                var receipt = await adapter.SubmitAsync(draft, prior.Status == "uncertain" || prior.RevisionId is not null, token);
                draft = store.Complete(id, receipt.State == "Quarantined" ? "quarantined" : "publishing", receipt);
                if (draft.Status == "quarantined") return draft;
                await adapter.ApproveAutomaticAsync(draft, receipt, token);
                return await ConfirmCoreAsync(draft, token);
            }
            catch (WikiAdapterException error)
            {
                return store.Complete(id, error.Status == 409 ? "publication-blocked" : "uncertain");
            }
            catch (OperationCanceledException)
            {
                store.Complete(id, "uncertain"); throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<WikiDraft> ConfirmAsync(WikiDraft draft, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { return await ConfirmCoreAsync(draft, token); }
        finally { _gate.Release(); }
    }

    private async Task<WikiDraft> ConfirmCoreAsync(WikiDraft draft, CancellationToken token)
    {
        draft = store.Find(draft.Id)!;
        if (draft.RevisionId is null) return draft;
        var targets = await adapter.TargetsAsync(draft.Destination, token);
        var current = targets.FirstOrDefault(item => item.EntryId == draft.EntryId && item.RevisionId == draft.RevisionId && item.Status == "current");
        var affected = draft.Content.Kind is "Rule" or "Project"
            ? requests.Requests(null).Where(item => item.Project == draft.Project).Select(item => item.Id).ToArray() : [];
        if (current is not null) return store.ConfirmKnowledge(draft.Id, "approved", affected);
        return draft.Status == "approved" ? store.ConfirmKnowledge(draft.Id, "needs-recheck", affected) : store.Find(draft.Id)!;
    }

    public async Task AdvanceAsync(WikiDraft draft, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            draft = store.Find(draft.Id)!;
            if (draft.Status is "publishing" or "uncertain")
            {
                if (!WikiDraftPolicy.CanSubmit(draft)) return;
                var receipt = await adapter.SubmitAsync(draft, retry: true, token);
                if (receipt.State == "Quarantined") { store.Complete(draft.Id, "quarantined", receipt); return; }
                draft = store.Complete(draft.Id, "publishing", receipt);
                await adapter.ApproveAutomaticAsync(draft, receipt, token);
            }
            if (draft.Status is "publishing" or "approved") await ConfirmCoreAsync(draft, token);
        }
        catch (WikiAdapterException error) when (error.Status == 409 && draft.Status is "publishing" or "uncertain")
        {
            store.Complete(draft.Id, "publication-blocked");
        }
        finally { _gate.Release(); }
    }
}

public sealed class WikiPublicationMonitor(WikiDraftStore store, WikiPublisher publisher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!WikiProposalAdapter.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var draft in store.Drafts().Where(item => item.Status is "publishing" or "uncertain" or "approved"))
            {
                try { await publisher.AdvanceAsync(draft, stoppingToken); }
                catch (WikiAdapterException) { /* Unavailable services retain the receipt; no success notice is invented. */ }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }
}
