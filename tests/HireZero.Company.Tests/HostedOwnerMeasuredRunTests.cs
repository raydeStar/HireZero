using HireZero.Core;

public sealed class HostedOwnerMeasuredRunTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("hosted-owner-run-");

    [Fact]
    public async Task AHostedOwnerSignedInBySubjectCanRunTheApprovedJob()
    {
        var provider = new MeasuredQueueChecks.SlowProvider();
        var store = new CompanyStore(
            Path.Combine(_root.Path, "data.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: provider.RuntimeIdentity
        );
        var task = MeasuredQueueChecks.Seed(store);
        // The first hosted sign-in re-keys the configured owner login to the provider's subject.
        Assert.NotNull(store.SignIn("fictional-subject", "owner@example.invalid", true, "Fictional owner"));
        Assert.Throws<WorkspaceException>(() => store.Actor("owner@example.invalid"));
        var proposal = CompanyJson.Decode<CompanyAttempt>(
            store.Apply(
                MeasuredQueueChecks.C("measured.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = task.Version,
                    CreditAllowance = 1,
                },
                "fictional-subject",
                out _
            )
        );
        provider.Completion.SetResult(new("A fictional draft", 120, "fictional-receipt", new(100, 20, 5)));
        var receipt = await new CompanyScheduler(store, provider).RunMeasuredOnceAsync(
            MeasuredQueueChecks.RunCommand(proposal),
            provider,
            TestContext.Current.CancellationToken,
            "fictional-subject"
        );
        Assert.False(string.IsNullOrEmpty(receipt));
        Assert.True(provider.Entered.Task.IsCompleted);
    }

    public async ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
