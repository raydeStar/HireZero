using HireZero.Core;
using HireZero.Integrations.Codex;

public sealed partial class BusinessDraftTests
{
    [Fact]
    public Task MissingPinnedRuntimeRefusesBeforeDispatchAndReleasesTheDraftAllowance() =>
        CheckUnavailableRuntime(Path.Combine(_root.Path, "missing-codex.exe"), "missing-pin");

    [Fact]
    public Task ChangedPinnedRuntimeRefusesBeforeDispatchAndReleasesTheDraftAllowance()
    {
        Assert.True(File.Exists(MeasuredQueueChecks.Fake));
        return CheckUnavailableRuntime(MeasuredQueueChecks.Fake, "different-pin");
    }

    private async Task CheckUnavailableRuntime(string executable, string identity)
    {
        var scratch = Path.Combine(_root.Path, "must-not-start");
        var model = new CompanyCodexConnection(new(executable, identity, scratch));
        var store = new CompanyStore(
            Path.Combine(_root.Path, "unavailable-runtime.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: model.RuntimeIdentity
        );
        var (business, source) = Setup(store);
        var draft = Prepare(store, business, source);
        using var scheduler = new CompanyScheduler(store, model);
        var command = Run(draft);
        await scheduler.RunBusinessDraftOnceAsync(command, Owner, model, TestContext.Current.CancellationToken);
        var result = store.BusinessDraft(draft.Id);
        Assert.Equal("withheld", result.State);
        Assert.Contains("pinned Codex runtime is missing or changed", result.Error);
        Assert.Contains("No model request was sent", result.Error);
        Assert.Equal(0L, result.ReportedUsage);
        Assert.Equal(new ModelUsage(0, 0), result.Usage);
        Assert.Null(result.ProviderReceipt);
        Assert.Empty(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.All(
            Items<CompanyBudget>(store, "budgets"),
            budget => Assert.Equal((0L, 0L), (budget.Used, budget.Reserved))
        );
        Assert.False(Directory.Exists(scratch));
        await scheduler.RunBusinessDraftOnceAsync(command, Owner, model, TestContext.Current.CancellationToken);
        Assert.Equal(CompanyJson.Encode(result), CompanyJson.Encode(store.BusinessDraft(draft.Id)));
        Assert.False(Directory.Exists(scratch));
    }
}
