namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private static bool DecisionReceiptValid(string? receipt) =>
        receipt is { Length: > 0 and <= 240 }
        && receipt.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.');

    private static long? DecisionTokens(ModelUsage? usage) =>
        usage is not null
        && usage.InputTokens >= 0
        && usage.OutputTokens >= 0
        && usage.InputTokens <= long.MaxValue - usage.OutputTokens
        && usage.Consistent(usage.InputTokens + usage.OutputTokens)
        && usage.BilledUsd is not < 0
            ? usage.InputTokens + usage.OutputTokens
            : null;

    public void FenceDecision(string id)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var d = Need<CompanyDecision>(db, id);
                if (d.State != "running")
                    return false;
                SaveDecisionOutcome(
                    db,
                    d with
                    {
                        State = "unknown",
                        Result = null,
                        Error =
                            "Provider completion is uncertain. The allowance is retained; do not replay this request.",
                    }
                );
                return true;
            }
        );
    }

    // A terminated invocation may settle accounting after interruption, but its late advice is never promoted.
    public void FinishDecision(string id, CompanyDecisionOutcome outcome)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var d = Need<CompanyDecision>(db, id);
                if (d.State is not ("running" or "unknown") || !d.DispatchTaken)
                    return false;
                var wasRunning = d.State == "running";
                var total = DecisionTokens(outcome.Usage);
                var costs = new[] { d.ReportedCostUsd, outcome.ReportedCostUsd, outcome.Usage?.BilledUsd }
                    .Where(x => x is >= 0)
                    .Select(x => x!.Value)
                    .ToArray();
                decimal? cost = costs.Length > 0 ? costs.Max() : null;
                var validReceipt = DecisionReceiptValid(outcome.Receipt);
                var receiptConflict =
                    d.ProviderReceipt is not null && validReceipt && d.ProviderReceipt != outcome.Receipt;
                var earlierObserved = d.ReportedUsage;
                d = d with
                {
                    Usage = total is not null && total >= (d.ReportedUsage ?? 0) ? outcome.Usage : d.Usage,
                    ReportedUsage = total is not null ? Math.Max(total.Value, d.ReportedUsage ?? 0) : d.ReportedUsage,
                    ReportedCostUsd = cost,
                    ProviderReceipt = d.ProviderReceipt ?? (validReceipt ? outcome.Receipt : null),
                    Result = null,
                };
                var notSent =
                    outcome.State == "not_sent"
                    && total == 0
                    && cost == 0
                    && earlierObserved is null or 0
                    && outcome.Usage?.BilledUsd == 0
                    && outcome.ReportedCostUsd is null or 0
                    && !receiptConflict;
                var completeAccounting =
                    total is not null
                    && total >= (earlierObserved ?? 0)
                    && cost is not null
                    && outcome.Usage?.BilledUsd == cost
                    && outcome.ReportedCostUsd is not < 0
                    && validReceipt
                    && !receiptConflict;
                if (
                    !notSent
                    && (
                        !completeAccounting
                        || outcome.State == "unknown"
                        || outcome.State is not ("completed" or "answer_withheld")
                        || total > d.PlanningTokens
                        || cost > d.AllowanceCents / 100m
                    )
                )
                {
                    SaveDecisionOutcome(
                        db,
                        d with
                        {
                            State = "unknown",
                            Error =
                                "Usage, termination or the reviewed allowance needs reconciliation. No advice was accepted.",
                        }
                    );
                    return false;
                }
                Release(db, d.Id, total);
                var advice =
                    !notSent
                    && wasRunning
                    && DecisionCurrent(db, d)
                    && d.ExpiresAt > Clock()
                    && outcome.State == "completed"
                    && DecisionAdviceValid(d.Packet, outcome);
                SaveDecisionOutcome(
                    db,
                    d with
                    {
                        State =
                            notSent ? "not_sent"
                            : advice ? "completed"
                            : "withheld",
                        Result = advice ? outcome : null,
                        Error =
                            notSent ? "The provider confirmed no request was processed."
                            : advice ? null
                            : "The advice was invalid, late, or its authority changed. Usage was recorded without accepting advice.",
                    }
                );
                return true;
            }
        );
    }

    private static bool DecisionAdviceValid(CompanyDecisionPacket packet, CompanyDecisionOutcome answer) =>
        answer.Choice is not null
        && packet.Choices.Contains(answer.Choice, StringComparer.Ordinal)
        && answer.Confidence is { } confidence
        && double.IsFinite(confidence)
        && confidence is >= 0 and <= 1
        && answer.ResolvedModel is { Length: > 0 and <= 192 }
        && answer.Probabilities is { } probabilities
        && probabilities.Keys.Order(StringComparer.Ordinal).SequenceEqual(packet.Choices.Order(StringComparer.Ordinal))
        && probabilities.Values.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && Math.Abs(probabilities.Values.Sum() - 1) <= 0.001;

    private object ReconcileDecision(StoreConnection db, CompanyCommand c)
    {
        DecisionOwner(db);
        var d = Need<CompanyDecision>(db, c.Id);
        Version(d.Version, c.ExpectedVersion);
        if (d.State != "unknown" || c.Digest != d.Digest || c.Decision != "confirmed-usage-and-termination")
            throw new WorkspaceException(
                "Reconcile this exact uncertain decision with confirmed provider usage and termination.",
                409
            );
        var evidence = CompanyJson.Decode<CompanyDecisionReconciliation>(c.Body ?? "null");
        var total = DecisionTokens(evidence.Usage);
        if (
            !evidence.ProviderStopped
            || total is null
            || !DecisionReceiptValid(evidence.Receipt)
            || evidence.Usage.InputTokens > 1_000_000_000
            || evidence.Usage.OutputTokens > 1_000_000_000
            || evidence.Usage.BilledUsd is null or < 0 or > 1_000_000_000
            || total < (d.ReportedUsage ?? 0)
            || evidence.Usage.BilledUsd < (d.ReportedCostUsd ?? 0)
            || d.ProviderReceipt is not null && d.ProviderReceipt != evidence.Receipt
        )
            throw new WorkspaceException(
                "Confirm termination and complete receipt-bound usage without reducing any observed charge or tokens.",
                422
            );
        var note = Text(evidence.Evidence, 500);
        var reservation = Need<CompanyReservation>(db, d.Id);
        if (reservation.State != "reserved")
            throw new WorkspaceException("The decision is already settled.", 409);
        foreach (var id in reservation.Scopes!)
        {
            var budget = Need<CompanyBudget>(db, id);
            Save(
                db,
                budget with
                {
                    Reserved = budget.Reserved - reservation.Amount,
                    Used = checked(budget.Used + total.Value),
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
            d.Id,
            reservation.RootId,
            state: "settled",
            version: reservation.Version + 1
        );
        SaveDecisionOutcome(
            db,
            d with
            {
                State = "reconciled",
                Usage = evidence.Usage,
                ReportedUsage = total,
                ReportedCostUsd = evidence.Usage.BilledUsd,
                ProviderReceipt = evidence.Receipt,
                Result = null,
                Error = "Owner reconciled usage and termination: " + note,
            }
        );
        return Need<CompanyDecision>(db, d.Id);
    }

    private void RecoverDecisions(StoreConnection db)
    {
        foreach (var d in All<CompanyDecision>(db).Where(d => d.State == "running"))
            SaveDecisionOutcome(
                db,
                d with
                {
                    State = "unknown",
                    Result = null,
                    Error =
                        "The host restarted after authorization. The request is not replayed; reconcile provider usage and termination.",
                }
            );
    }
}

public sealed partial class CompanyScheduler
{
    public async Task<string> RunDecisionOnceAsync(
        CompanyCommand command,
        string subject,
        Func<CompanyDecision, CancellationToken, Task<CompanyDecisionOutcome>> dispatch,
        CancellationToken token = default
    )
    {
        if (command.Action != "decision.run" || command.Decision != "accept-soft-limits")
            throw new WorkspaceException("Explicit one-job decision approval is required.", 409);
        await store.ProviderGate.WaitAsync(token);
        try
        {
            var receipt = store.Apply(command, subject, out var fresh);
            if (!fresh)
                return receipt;
            var prepared = CompanyJson.Decode<CompanyDecision>(receipt);
            var claim = store.TakeDecisionDispatch(prepared.Id);
            if (claim is null)
                return receipt;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var watching = new CancellationTokenSource();
            async Task Watch()
            {
                try
                {
                    while (!watching.IsCancellationRequested)
                    {
                        if (!store.DecisionIsCurrent(claim.Id))
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
            Task<CompanyDecisionOutcome>? completion = null;
            try
            {
                if (deadline.IsCancellationRequested)
                    store.FinishDecision(claim.Id, new("not_sent", Usage: new(0, 0, BilledUsd: 0), ReportedCostUsd: 0));
                else
                {
                    completion = dispatch(claim, deadline.Token);
                    var result = await completion.WaitAsync(deadline.Token);
                    if (deadline.IsCancellationRequested)
                        store.FenceDecision(claim.Id);
                    store.FinishDecision(claim.Id, result);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                store.FenceDecision(claim.Id);
                if (completion is not null)
                    _ = ObserveDecisionAsync(completion, claim.Id);
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

    private async Task ObserveDecisionAsync(Task<CompanyDecisionOutcome> completion, string id)
    {
        try
        {
            store.FinishDecision(id, await completion);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
}
