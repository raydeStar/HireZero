namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private sealed record MemoryAnalysisAnswer(CompanyMemoryFinding[] Findings);

    public void FenceMemoryAnalysis(string id, string reason)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var draft = Need<CompanyMemoryAnalysis>(db, id);
                if (draft.State is not ("running" or "unknown"))
                    return false;
                SaveMemoryAnalysisOutcome(db, draft with { State = "unknown", Error = Text(reason, 500) });
                return true;
            }
        );
    }

    private void SaveMemoryAnalysisOutcome(StoreConnection db, CompanyMemoryAnalysis draft)
    {
        draft = draft with
        {
            Version = draft.Version + 1,
            UpdatedAt = Clock(),
            Job = draft.Job with { State = draft.State },
        };
        Save(db, draft, draft.Id, state: draft.State, version: draft.Version);
        SyncUsage(db, draft.Id);
        Event(
            db,
            "memory.analysis-" + draft.State,
            draft.Id,
            null,
            draft.State == "completed"
                    ? "A cited memory report is ready for owner review. Company knowledge was not changed."
                : draft.State == "unknown"
                    ? "The memory check is uncertain; its allowance is retained and no replay is allowed."
                : "Memory analysis output was withheld. Recorded usage remains visible."
        );
    }

    // Called only once the adapter's invocation has terminated. Late receipts may settle cost, never create content.
    public void FinishMemoryAnalysis(
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
                var draft = Need<CompanyMemoryAnalysis>(db, id);
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
                    SaveMemoryAnalysisOutcome(
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
                if (!wasRunning || !MemoryAnalysisCurrent(db, draft) || refusal is not null)
                {
                    SaveMemoryAnalysisOutcome(
                        db,
                        draft with
                        {
                            State = "withheld",
                            Error = refusal is null
                                ? "The request expired, was interrupted, or its memory sources or owner authority changed. The response was withheld."
                                : ContentGuard.Clean(refusal, 500).Text,
                        }
                    );
                    return false;
                }
                try
                {
                    var answer = CompanyJson.Decode<MemoryAnalysisAnswer>(
                        Text(reply.Body, draft.Job.Policy.VisibleCharacterStop)
                    );
                    var findings = ValidateMemoryFindings(db, draft, answer.Findings);
                    SaveMemoryAnalysisOutcome(
                        db,
                        draft with
                        {
                            State = "completed",
                            Findings = findings,
                            Error = null,
                        }
                    );
                }
                catch (Exception error) when (error is WorkspaceException or System.Text.Json.JsonException)
                {
                    SaveMemoryAnalysisOutcome(
                        db,
                        draft with
                        {
                            State = "invalid",
                            Error =
                                "The response failed the structured evidence checks. No report was imported and no memory was changed. Review the selected records before preparing another check.",
                        }
                    );
                }
                return true;
            }
        );
    }

    private CompanyMemoryFinding[] ValidateMemoryFindings(
        StoreConnection db,
        CompanyMemoryAnalysis analysis,
        CompanyMemoryFinding[]? findings
    )
    {
        if (findings is null || findings.Length > 6)
            throw new WorkspaceException("A report contains at most six cited findings.", 422);
        foreach (var finding in findings)
        {
            if (
                finding is null
                || finding.Kind is not ("possible-conflict" or "possible-duplicate" or "needs-review")
                || string.IsNullOrWhiteSpace(finding.Explanation)
                || finding.Explanation.Length > 600
                || finding.Citations is not { Length: >= 1 and <= 4 }
            )
                throw new WorkspaceException("The report has an invalid finding.", 422);
            foreach (var citation in finding.Citations)
            {
                var source = citation is null ? null : analysis.Sources.SingleOrDefault(s => s.Id == citation.MemoryId);
                if (
                    source is null
                    || string.IsNullOrWhiteSpace(citation!.Quote)
                    || citation.Quote.Length > 300
                    || !source.Definition.Statement.Contains(citation.Quote, StringComparison.Ordinal)
                )
                    throw new WorkspaceException("Every citation must quote a selected record exactly.", 422);
            }
            if (finding.Kind != "needs-review" && finding.Citations.Select(c => c.MemoryId).Distinct().Count() < 2)
                throw new WorkspaceException("Compare at least two distinct records for a conflict or duplicate.", 422);
            if (finding.Revision is { } revision)
            {
                var target = analysis.Sources.SingleOrDefault(s => s.Id == revision.MemoryId);
                if (
                    target is null
                    || !finding.Citations.Any(c => c.MemoryId == revision.MemoryId)
                    || string.IsNullOrWhiteSpace(revision.Statement)
                    || revision.Statement.Length > 1200
                    || string.IsNullOrWhiteSpace(revision.Rationale)
                    || revision.Rationale.Length > 600
                    || revision.Statement.Trim() == target.Definition.Statement
                    || analysis.Sources.Any(s => !MemoryRevisionAudienceAllows(db, s.Definition, target.Definition))
                )
                    throw new WorkspaceException(
                        "A revision needs supported new wording for a cited record within its existing audience.",
                        422
                    );
            }
        }
        return findings;
    }

    private object ReconcileMemoryAnalysis(StoreConnection db, CompanyCommand command)
    {
        MemoryAnalysisOwner(db);
        var draft = Need<CompanyMemoryAnalysis>(db, command.Id);
        Version(draft.Version, command.ExpectedVersion);
        if (
            draft.State != "unknown"
            || command.Digest != draft.Job.Digest
            || command.Decision != "confirmed-usage-and-termination"
        )
            throw new WorkspaceException(
                "Reconcile the exact uncertain request with its provider receipt and confirmed termination.",
                409
            );
        var evidence = CompanyJson.Decode<CompanyMemoryAnalysisReconciliation>(command.Body ?? "null");
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
        SaveMemoryAnalysisOutcome(
            db,
            draft with
            {
                State = "reconciled",
                Usage = reconciledUsage,
                ReportedUsage = total,
                ProviderReceipt = receipt,
                Error = "Owner reconciled provider usage and termination: " + note,
            }
        );
        return Need<CompanyMemoryAnalysis>(db, draft.Id);
    }

    private void RecoverMemoryAnalyses(StoreConnection db)
    {
        foreach (var draft in All<CompanyMemoryAnalysis>(db).Where(d => d.State == "running"))
            SaveMemoryAnalysisOutcome(
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
    public async Task<string> RunMemoryAnalysisOnceAsync(
        CompanyCommand command,
        string subject,
        IMeasuredModelProvider measured,
        CancellationToken token = default
    )
    {
        // ADR 0036: Qualified governs automatic work; both real adapters intentionally return false.
        // Like RunMeasuredOnceAsync, this entry point consumes exact owner approval in store.Apply below.
        // Requiring that decision here as well keeps a memory check from becoming an automatic fallback.
        if (
            command.Action != "memory.analysis.run"
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
            var draft = CompanyJson.Decode<CompanyMemoryAnalysis>(receipt);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(draft.Job.Policy.LocalDeadlineSeconds));
            using var watching = new CancellationTokenSource();
            async Task Watch()
            {
                try
                {
                    while (!watching.IsCancellationRequested)
                    {
                        if (!store.MemoryAnalysisIsCurrent(draft.Id))
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
                    store.FenceMemoryAnalysis(
                        draft.Id,
                        "Local execution was interrupted; a late receipt cannot become an analysis report."
                    );
                store.FinishMemoryAnalysis(draft.Id, reply);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                store.FenceMemoryAnalysis(
                    draft.Id,
                    "The local deadline or authority changed. Provider usage and termination need confirmation."
                );
                if (completion is not null)
                {
                    try
                    {
                        store.FinishMemoryAnalysis(draft.Id, await completion.WaitAsync(TimeSpan.FromSeconds(8)));
                    }
                    catch (TimeoutException)
                    {
                        _ = ObserveMemoryAnalysisAsync(completion, draft.Id);
                    }
                    catch (ModelTransportException error)
                    {
                        SettleMemoryAnalysisError(draft.Id, error);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
                }
            }
            catch (ModelTransportException error)
            {
                SettleMemoryAnalysisError(draft.Id, error);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                store.FenceMemoryAnalysis(
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

    private void SettleMemoryAnalysisError(string id, ModelTransportException error) =>
        store.FinishMemoryAnalysis(
            id,
            new("", error.KnownUsage, error.Receipt, error.Usage),
            error.Message,
            error.AccountingViolation
        );

    private async Task ObserveMemoryAnalysisAsync(Task<ModelReply> completion, string id)
    {
        try
        {
            store.FinishMemoryAnalysis(id, await completion);
        }
        catch (ModelTransportException error)
        {
            SettleMemoryAnalysisError(id, error);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
}
