namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private sealed record BusinessDraftAnswer(CompanyProfileField[] Fields);

    public void FenceBusinessDraft(string id, string reason)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var draft = Need<CompanyBusinessDraft>(db, id);
                if (draft.State is not ("running" or "unknown"))
                    return false;
                SaveBusinessDraftOutcome(db, draft with { State = "unknown", Error = Text(reason, 500) });
                return true;
            }
        );
    }

    private void SaveBusinessDraftOutcome(StoreConnection db, CompanyBusinessDraft draft)
    {
        draft = draft with
        {
            Version = draft.Version + 1,
            UpdatedAt = Clock(),
            Job = draft.Job with { State = draft.State },
        };
        Save(db, draft, draft.Id, BusinessIdentityId, state: draft.State, version: draft.Version);
        SyncUsage(db, draft.Id);
        Event(
            db,
            "business.draft-" + draft.State,
            draft.Id,
            null,
            draft.State == "completed" ? "A sourced brief draft is ready for review. It is not approved knowledge."
                : draft.State == "unknown"
                    ? "The profile request is uncertain; its allowance is retained and no replay is allowed."
                : draft.LocalRefusalReceipt is not null
                    ? "Owner recorded local evidence that no request was sent; zero usage was settled without replay."
                : "Profile output was withheld. Recorded usage remains visible."
        );
    }

    // Called only once the adapter's invocation has terminated. Late receipts may settle cost, never create content.
    public void FinishBusinessDraft(
        string id,
        ModelReply reply,
        string? refusal = null,
        bool accountingViolation = false
    )
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var draft = Need<CompanyBusinessDraft>(db, id);
                if (draft.State is not ("running" or "unknown"))
                    return false;
                var wasRunning = draft.State == "running";
                draft = draft with
                {
                    Usage = reply.Usage,
                    ReportedUsage = reply.TotalTokens,
                    ProviderReceipt = ReceiptId(reply.Receipt),
                };
                if (
                    accountingViolation
                    || reply.TotalTokens is null or < 0
                    || reply.Usage is null
                    || !reply.Usage.Consistent(reply.TotalTokens)
                    || reply.Usage.BilledUsd is < 0
                    || reply.TotalTokens > draft.Job.Policy.PlanningTokens
                    || reply.Receipt is not null && ReceiptId(reply.Receipt) != reply.Receipt.Trim()
                )
                {
                    SaveBusinessDraftOutcome(
                        db,
                        draft with
                        {
                            State = "unknown",
                            Error =
                                "Provider usage is incomplete or outside the authorized planning allowance. Reconcile its receipt before more work.",
                        }
                    );
                    return false;
                }
                Release(db, draft.Id, reply.TotalTokens);
                if (!wasRunning || !BusinessDraftCurrent(db, draft) || refusal is not null)
                {
                    SaveBusinessDraftOutcome(
                        db,
                        draft with
                        {
                            State = "withheld",
                            Error = refusal is null
                                ? "The request expired, was interrupted, or its business context changed. The response was withheld."
                                : ContentGuard.Clean(refusal, 500).Text,
                        }
                    );
                    return false;
                }
                try
                {
                    var answer = CompanyJson.Decode<BusinessDraftAnswer>(
                        Text(reply.Body, draft.Job.Policy.VisibleCharacterStop)
                    );
                    if (
                        answer.Fields is null
                        || answer.Fields.Length != BusinessProfileFields.Length
                        || answer.Fields.Select(f => f?.Key).Order().SequenceEqual(BusinessProfileFields.Order())
                            == false
                        || answer.Fields.Any(f =>
                            f is null
                            || !draft.OwnerFields.Any(o => o.Key == f.Key)
                                && (
                                    f.Verification == "owner"
                                    || f.Verification == "unknown" && f.Value != "Unknown"
                                    || (f.Citations?.Length ?? 0) > 1
                                    || (f.Citations ?? []).Any(c => c is null || !draft.SourceIds.Contains(c.SourceId))
                                )
                        )
                    )
                        throw new WorkspaceException(
                            "The model must return bounded source-supported facts or explicit unknowns, never owner authority.",
                            422
                        );
                    var candidate = answer
                        .Fields.Select(f => draft.OwnerFields.SingleOrDefault(o => o.Key == f.Key) ?? f)
                        .ToArray();
                    var fields = ValidateProfile(db, new(draft.BusinessVersion, candidate));
                    var profile = (CompanyBusinessProfile)SaveBusinessProfile(
                        db,
                        new(
                            draft.Id,
                            "business.profile.save",
                            Body: CompanyJson.Encode(new CompanyProfileInput(draft.BusinessVersion, fields))
                        ),
                        draft.Id
                    );
                    SaveBusinessDraftOutcome(
                        db,
                        draft with
                        {
                            State = "completed",
                            ProfileId = profile.Id,
                            Error = null,
                        }
                    );
                }
                catch (Exception error) when (error is WorkspaceException or System.Text.Json.JsonException)
                {
                    SaveBusinessDraftOutcome(
                        db,
                        draft with
                        {
                            State = "invalid",
                            Error =
                                "No brief was imported. "
                                + (
                                    error is WorkspaceException
                                        ? ContentGuard.Clean(error.Message, 500).Text
                                        : "The model response was not valid brief JSON. It must contain only the expected fields object."
                                ),
                            // Keep the rejected draft in the evidence tray, never in the employee's knowledge.
                            RejectedResponse = ContentGuard
                                .Clean(reply.Body, draft.Job.Policy.VisibleCharacterStop)
                                .Text,
                        }
                    );
                }
                return true;
            }
        );
    }

    private object ReconcileBusinessDraft(StoreConnection db, CompanyCommand command)
    {
        var draft = Need<CompanyBusinessDraft>(db, command.Id);
        Version(draft.Version, command.ExpectedVersion);
        var evidence = CompanyJson.Decode<CompanyBusinessReconciliation>(command.Body ?? "null");
        var localRefusal = evidence.Kind == "local-refusal";
        if (
            draft.State != "unknown"
            || command.Digest != draft.Job.Digest
            || evidence.Kind is not ("provider" or "local-refusal")
            || command.Decision != (localRefusal ? "confirmed-local-refusal" : "confirmed-usage-and-termination")
        )
            throw new WorkspaceException(
                "Reconcile the exact uncertain request with the selected evidence and explicit confirmation.",
                409
            );
        var receipt = Text(evidence.Receipt, 160);
        if (
            evidence.Usage is null
            || evidence.Usage.InputTokens is < 0 or > 1_000_000_000
            || evidence.Usage.OutputTokens is < 0 or > 1_000_000_000
        )
            throw new WorkspaceException(
                "Provide bounded, non-negative input and output usage from the provider receipt.",
                422
            );
        var total = evidence.Usage.InputTokens + evidence.Usage.OutputTokens;
        // A local diagnostic is not a provider bill. The owner must establish that nothing was sent.
        if (
            localRefusal
            && (
                total != 0
                || evidence.Usage.ReasoningTokens is > 0
                || evidence.Usage.BilledUsd is > 0
                || draft.ProviderReceipt is not null
                || draft.ReportedUsage is > 0
                || draft.Usage
                    is { InputTokens: > 0 }
                        or { OutputTokens: > 0 }
                        or { ReasoningTokens: > 0 }
                        or { BilledUsd: > 0 }
            )
        )
            throw new WorkspaceException(
                "A local refusal requires evidence that no model request was sent. It cannot replace a provider receipt or observed usage.",
                422
            );
        if (
            !evidence.ProviderStopped
            || !evidence.Usage.Consistent(total)
            || evidence.Usage.BilledUsd is < 0
            || ReceiptId(receipt) != receipt
            || draft.ReportedUsage is { } observed && total < observed
            || evidence.Usage.BilledUsd is { } supplied && draft.Usage?.BilledUsd is { } billed && supplied < billed
        )
            throw new WorkspaceException(
                "Provide complete usage without reducing observed usage, and confirm the provider stopped.",
                422
            );
        var note = Text(evidence.Evidence, 500);
        var reconciledUsage = evidence.Usage with { BilledUsd = evidence.Usage.BilledUsd ?? draft.Usage?.BilledUsd };
        var reservation = Need<CompanyReservation>(db, draft.Id);
        if (reservation.State != "reserved")
            throw new WorkspaceException("This request has already been settled.", 409);
        foreach (var id in reservation.Scopes!)
        {
            var budget = Need<CompanyBudget>(db, id);
            Save(
                db,
                budget with
                {
                    Reserved = budget.Reserved - reservation.Amount,
                    Used = checked(budget.Used + total),
                    Version = budget.Version + 1,
                },
                id,
                version: budget.Version + 1
            );
        }
        Save(
            db,
            reservation with
            {
                State = "settled",
                Actual = total,
                Version = reservation.Version + 1,
            },
            draft.Id,
            reservation.RootId,
            state: "settled",
            version: reservation.Version + 1
        );
        SaveBusinessDraftOutcome(
            db,
            draft with
            {
                State = "reconciled",
                Usage = reconciledUsage,
                ReportedUsage = total,
                ProviderReceipt = localRefusal ? null : receipt,
                LocalRefusalReceipt = localRefusal ? receipt : null,
                Error = localRefusal
                    ? "Owner recorded evidence that no model request was sent: " + note
                    : "Owner reconciled provider usage and termination: " + note,
            }
        );
        return Need<CompanyBusinessDraft>(db, draft.Id);
    }

    private void RecoverBusinessDrafts(StoreConnection db)
    {
        foreach (var draft in All<CompanyBusinessDraft>(db).Where(d => d.State == "running"))
            SaveBusinessDraftOutcome(
                db,
                draft with
                {
                    State = "unknown",
                    Error =
                        "The host restarted after dispatch. The request will not be replayed; reconcile provider usage and termination.",
                }
            );
    }
}

public sealed partial class CompanyScheduler
{
    public async Task<string> RunBusinessDraftOnceAsync(
        CompanyCommand command,
        string subject,
        IMeasuredModelProvider measured,
        CancellationToken token = default
    )
    {
        // ADR 0036: Qualified governs automatic work; both real adapters intentionally return false.
        // Like RunMeasuredOnceAsync, this entry point consumes exact owner approval in store.Apply below.
        // Requiring that decision here as well keeps a company draft from becoming an automatic fallback.
        if (
            command.Action != "business.draft.run"
            || command.Decision != "accept-soft-limits"
            || measured.RuntimeIdentity != store.MeasuredRuntimeIdentity
        )
            throw new WorkspaceException(
                "The selected measured provider and explicit one-job approval are required.",
                409
            );
        await store.ProviderGate.WaitAsync(token);
        try
        {
            var receipt = store.Apply(command, subject, out var fresh);
            if (!fresh)
                return receipt;
            var draft = CompanyJson.Decode<CompanyBusinessDraft>(receipt);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(draft.Job.Policy.LocalDeadlineSeconds));
            using var watching = new CancellationTokenSource();
            async Task Watch()
            {
                try
                {
                    while (!watching.IsCancellationRequested)
                    {
                        if (!store.BusinessDraftIsCurrent(draft.Id))
                        {
                            deadline.Cancel();
                            return;
                        }
                        await Task.Delay(100, watching.Token);
                    }
                }
                catch (OperationCanceledException) when (watching.IsCancellationRequested) { }
                catch
                {
                    deadline.Cancel();
                }
            }
            var monitor = Watch();
            Task<ModelReply>? completion = null;
            try
            {
                completion = measured.CompleteMeasuredAsync(draft.Call, draft.Job, deadline.Token);
                var reply = await completion.WaitAsync(deadline.Token);
                if (deadline.IsCancellationRequested)
                    store.FenceBusinessDraft(
                        draft.Id,
                        "Local execution was interrupted; a late receipt cannot become a brief."
                    );
                store.FinishBusinessDraft(draft.Id, reply);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                store.FenceBusinessDraft(
                    draft.Id,
                    "The local deadline or authority changed. Provider usage and termination need confirmation."
                );
                if (completion is not null)
                {
                    try
                    {
                        store.FinishBusinessDraft(draft.Id, await completion.WaitAsync(TimeSpan.FromSeconds(8)));
                    }
                    catch (TimeoutException)
                    {
                        _ = ObserveBusinessDraftAsync(completion, draft.Id);
                    }
                    catch (ModelTransportException error)
                    {
                        SettleBusinessError(draft.Id, error);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
                }
            }
            catch (ModelTransportException error)
            {
                SettleBusinessError(draft.Id, error);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                store.FenceBusinessDraft(
                    draft.Id,
                    "Provider outcome is unconfirmed. Check its receipt; this request is not retried."
                );
            }
            finally
            {
                watching.Cancel();
                await monitor;
            }
            return receipt;
        }
        finally
        {
            store.ProviderGate.Release();
        }
    }

    private void SettleBusinessError(string id, ModelTransportException error) =>
        store.FinishBusinessDraft(
            id,
            new("", error.KnownUsage, error.Receipt, error.Usage),
            error.Message,
            error.AccountingViolation
        );

    private async Task ObserveBusinessDraftAsync(Task<ModelReply> completion, string id)
    {
        try
        {
            store.FinishBusinessDraft(id, await completion);
        }
        catch (ModelTransportException error)
        {
            SettleBusinessError(id, error);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
}
