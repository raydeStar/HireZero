// Each synthetic check group runs as one xUnit test; tests in one class run sequentially.
public sealed class DeveloperSuite
{
    [Fact]
    public Task Inline() => DeveloperInlineChecks.RunAsync();

    [Fact]
    public Task ShiftScope() => ShiftScopeChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ConfiguredShift() => ConfiguredShiftChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ProviderLifecycle() => ProviderLifecycleChecks.RunAsync(Verify.Check);

    [Fact]
    public Task PullRequestWorkflow() => PullRequestWorkflowChecks.RunAsync(Verify.Check);

    [Fact]
    public Task RepositoryCheckoutResolver() => RepositoryCheckoutResolverChecks.RunAsync(Verify.Check);

    [Fact]
    public void GitCloneTransport() => GitCloneTransportChecks.Run(Verify.Check);

    [Fact]
    public void NativeCloneDirectory() => NativeCloneDirectoryChecks.Run(Verify.Check);

    [Fact]
    public Task WorkflowConsent() => WorkflowConsentChecks.RunAsync(Verify.Check);

    [Fact]
    public Task LegacyConsent() => LegacyConsentChecks.RunAsync(Verify.Check);

    [Fact]
    public Task BacklogPipeline() => BacklogPipelineChecks.RunAsync(Verify.Check);

    [Fact]
    public Task WorkflowRace() => WorkflowRaceChecks.RunAsync(Verify.Check);

    [Fact]
    public Task PreparationConsent() => PreparationConsentChecks.RunAsync(Verify.Check);

    [Fact]
    public Task WorkflowApproval() => WorkflowApprovalChecks.RunAsync(Verify.Check);

    [Fact]
    public Task HomeApproval() => HomeApprovalChecks.RunAsync(Verify.Check);

    [Fact]
    public Task PipelineWatch() => PipelineWatchChecks.RunAsync(Verify.Check);

    [Fact]
    public Task WorkspaceRateLimit() => WorkspaceRateLimitChecks.RunAsync(Verify.Check);

    [Fact]
    public Task DirectCoding() => DirectCodingChecks.RunAsync(Verify.Check);

    [Fact]
    public Task FixLoop() => FixLoopChecks.RunAsync(Verify.Check);

    [Fact]
    public Task DeveloperBudget() => DeveloperBudgetChecks.RunAsync(Verify.Check);

    [Fact]
    public Task TrustedPostgres() => TrustedPostgresChecks.RunAsync(Verify.Check);

    [Fact]
    public Task ActivityRecord() => ActivityRecordChecks.RunAsync(Verify.Check);
}
