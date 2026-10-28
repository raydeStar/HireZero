using HireZero.Core;

public sealed partial class BusinessDraftTests
{
    private static CompanyCommand LocalRefusal(CompanyBusinessDraft draft) =>
        Cmd("business.draft.reconcile") with
        {
            Id = draft.Id,
            ExpectedVersion = draft.Version,
            Digest = draft.Job.Digest,
            Decision = "confirmed-local-refusal",
            Body = CompanyJson.Encode(
                new CompanyBusinessReconciliation(
                    "fictional-local-diagnostic",
                    new(0, 0),
                    "Fictional dispatch log confirms executable validation failed before process creation.",
                    true,
                    "local-refusal"
                )
            ),
        };

    [Fact]
    public async Task LocalRefusalSettlesOnlyZeroUsageAndSurvivesRestartWithoutReplayOrImport()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var model = new Model((_, _) => throw new HttpRequestException("Fictional lost local refusal"));
        using var scheduler = new CompanyScheduler(store, model);
        var run = Run(prepared);
        await scheduler.RunBusinessDraftOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        var uncertain = store.BusinessDraft(prepared.Id);
        var reconciliation = LocalRefusal(uncertain);
        store.Apply(reconciliation);
        var reopened = Store();
        reopened.Recover();
        var resolved = reopened.BusinessDraft(prepared.Id);
        Assert.Equal("reconciled", resolved.State);
        Assert.Equal("fictional-local-diagnostic", resolved.LocalRefusalReceipt);
        Assert.Null(resolved.ProviderReceipt);
        Assert.Equal(0, resolved.ReportedUsage);
        Assert.Contains("no model request was sent", resolved.Error);
        var budget = Items<CompanyBudget>(reopened, "budgets").Single(b => b.Id == "company-total");
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(0, budget.Used);
        Assert.Empty(Items<CompanyBusinessProfile>(reopened, "businessProfiles"));
        reopened.Apply(reconciliation); // A repeated command returns its receipt, not a second settlement.
        Assert.Throws<WorkspaceException>(() => reopened.Apply(LocalRefusal(resolved)));
        using var restarted = new CompanyScheduler(reopened, model);
        await restarted.RunBusinessDraftOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public void LocalRefusalRequiresExactAuthorityEvidenceAndZeroUsage()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        store.Apply(Run(prepared));
        store.FenceBusinessDraft(prepared.Id, "Fictional uncertain request");
        var uncertain = store.BusinessDraft(prepared.Id);
        var command = LocalRefusal(uncertain);
        var evidence = CompanyJson.Decode<CompanyBusinessReconciliation>(command.Body!);
        foreach (
            var invalid in new[]
            {
                command with
                {
                    Digest = "different-request",
                },
                command with
                {
                    ExpectedVersion = uncertain.Version - 1,
                },
                command with
                {
                    Decision = "confirmed-usage-and-termination",
                },
                command with
                {
                    Body = CompanyJson.Encode(evidence with { Kind = "unknown-kind" }),
                },
                command with
                {
                    Body = CompanyJson.Encode(evidence with { ProviderStopped = false }),
                },
                command with
                {
                    Body = CompanyJson.Encode(evidence with { Evidence = "" }),
                },
                command with
                {
                    Body = CompanyJson.Encode(evidence with { Receipt = "" }),
                },
                command with
                {
                    Body = CompanyJson.Encode(evidence with { Usage = new(1, 0) }),
                },
                command with
                {
                    Body = CompanyJson.Encode(evidence with { Usage = new(0, 0, BilledUsd: 0.01m) }),
                },
            }
        )
            Assert.Throws<WorkspaceException>(() => store.Apply(invalid));
        Assert.Equal("unknown", store.BusinessDraft(prepared.Id).State);
        Assert.Equal(26000, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == "company-total").Reserved);
    }

    [Fact]
    public void LocalRefusalCannotReplaceAProviderReceiptOrAnyObservedUsage()
    {
        foreach (
            var observed in new[]
            {
                new ModelReply("", 0, "fictional-provider-receipt", new(0, 0)),
                new ModelReply("", 1, null, new(1, 0)),
                new ModelReply("", null, null, new(0, 0, BilledUsd: 0.01m)),
            }
        )
        {
            var store = Store(Guid.NewGuid().ToString("N"));
            var (identity, source) = Setup(store);
            var prepared = Prepare(store, identity, source);
            store.Apply(Run(prepared));
            store.FinishBusinessDraft(prepared.Id, observed, accountingViolation: true);
            var uncertain = store.BusinessDraft(prepared.Id);
            Assert.Throws<WorkspaceException>(() => store.Apply(LocalRefusal(uncertain)));
            Assert.Equal(CompanyJson.Encode(uncertain), CompanyJson.Encode(store.BusinessDraft(prepared.Id)));
            Assert.Equal(26000, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == "company-total").Reserved);
        }
    }
}
