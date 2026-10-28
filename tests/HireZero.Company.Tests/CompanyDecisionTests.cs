using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyDecisionTests : IDisposable
{
    private const string Owner = "owner@example.invalid",
        Model = "fictional/decision-v1";
    private readonly TemporaryRoot _root = new("company-decisions-");
    private DateTimeOffset _now = new(2026, 10, 31, 23, 58, 0, TimeSpan.Zero);
    private string _revision = "fictional-connection-1";
    private bool _connected = true;

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions(), companyId: company)
        {
            Clock = () => _now,
            PrepareDecisionPacket = input =>
                new(
                    company,
                    "fictional-decisions",
                    input.Model,
                    CompanyJson.Encode(input),
                    ContentGuard.Digest(CompanyJson.Encode(input)),
                    input.Criteria.Keys.ToArray(),
                    new("company-key", "fictional-decisions", _revision)
                ),
            CurrentDecisionPacket = packet =>
                _connected && packet.CompanyId == company && packet.Funding.ConnectionRevision == _revision,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static CompanyCommand PrepareCommand() =>
        C("decision.prepare") with
        {
            CreditAllowance = 1,
            Body = CompanyJson.Encode(
                new CompanyDecisionInput(
                    Model,
                    "{\"note\":\"Fictional private bakery note\"}",
                    "Classify without granting authority.",
                    new() { ["act"] = "Propose work", ["review"] = "Needs owner review" }
                )
            ),
        };

    private static CompanyDecision Prepare(CompanyStore s) => Apply<CompanyDecision>(s, PrepareCommand());

    private static CompanyCommand Run(CompanyDecision d) =>
        C("decision.run") with
        {
            Id = d.Id,
            ExpectedVersion = d.Version,
            Digest = d.Digest,
            Decision = "accept-soft-limits",
        };

    private static CompanyDecision Start(CompanyStore s)
    {
        var d = Prepare(s);
        s.Apply(Run(d));
        return s.TakeDecisionDispatch(d.Id)!;
    }

    private static CompanyDecisionOutcome Answer(decimal cost = 0.001m, long input = 100) =>
        new(
            "completed",
            "review",
            0.8,
            new() { ["act"] = 0.2, ["review"] = 0.8 },
            new(input, 20, BilledUsd: cost),
            "fictional:receipt-1",
            Model,
            cost
        );

    private static CompanyCommand Reconcile(
        CompanyDecision d,
        long input,
        decimal cost,
        bool stopped = true,
        string receipt = "fictional:receipt-1"
    ) =>
        C("decision.reconcile") with
        {
            Id = d.Id,
            ExpectedVersion = d.Version,
            Digest = d.Digest,
            Decision = "confirmed-usage-and-termination",
            Body = CompanyJson.Encode(
                new CompanyDecisionReconciliation(
                    receipt,
                    new(input, 20, BilledUsd: cost),
                    stopped,
                    "Fictional provider receipt checked."
                )
            ),
        };

    [Fact]
    public void ExactPreparedPacketSurvivesReloadAndCompanyIsolationWithoutAnyUsage()
    {
        var s = Store();
        var command = PrepareCommand();
        var d = Apply<CompanyDecision>(s, command);
        Assert.Equal("prepared", d.State);
        Assert.Contains("Fictional private bakery note", d.Packet.Json);
        Assert.Equal(CompanyJson.Encode(d), CompanyJson.Encode(Store().Decision(d.Id)));
        Assert.True(s.IsDecisionOperation(command.RequestId));
        Assert.False(Store("neighbor").IsDecisionOperation(command.RequestId));
        Assert.Throws<WorkspaceException>(() => Store("neighbor").Decision(d.Id));
        Assert.Empty(s.Usage().Entries);
    }

    [Fact]
    public void StaleConnectionWrongDigestMissingConsentAndExpiredReviewsCannotReserveUsage()
    {
        var s = Store();
        var d = Prepare(s);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d) with { Digest = "wrong" }));
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d) with { Decision = "approve" }));
        _revision = "rotated";
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d)));
        _revision = "fictional-connection-1";
        _now = _now.AddMinutes(11);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d)));
        Assert.Empty(s.Usage().Entries);
    }

    [Fact]
    public void OneDurableClaimWinsEvenAcrossTwoStoreInstancesAndCommandReplay()
    {
        var s = Store();
        var d = Prepare(s);
        var run = Run(d);
        var receipt = s.Apply(run, out var fresh);
        Assert.True(fresh);
        var other = Store();
        Assert.Equal(receipt, other.Apply(run, out var replay));
        Assert.False(replay);
        var claims = new CompanyDecision?[2];
        Parallel.Invoke(
            () => claims[0] = s.TakeDecisionDispatch(d.Id),
            () => claims[1] = other.TakeDecisionDispatch(d.Id)
        );
        Assert.Single(claims.OfType<CompanyDecision>());
        Assert.Null(s.TakeDecisionDispatch(d.Id));
        Assert.Single(s.Usage().Entries);
        Assert.Equal(CompanyStore.DecisionPlanningTokens, s.Usage().Current.HeldTokens);
    }

    [Fact]
    public void ACompleteDecisionSettlesOnceWithoutCreatingTasksOrApprovals()
    {
        var s = Store();
        var d = Start(s);
        s.FinishDecision(d.Id, Answer());
        s.FinishDecision(d.Id, Answer(0.009m));
        var saved = s.Decision(d.Id);
        Assert.Equal("completed", saved.State);
        Assert.Equal("review", saved.Result!.Choice);
        var usage = Assert.Single(s.Usage().Entries);
        Assert.Equal("settled", usage.State);
        Assert.Equal(120, usage.ActualTokens);
        Assert.Equal(0.001m, usage.ReportedUsd);
        Assert.Equal("company-key", usage.Funding!.Source);
        Assert.Equal("fictional-decisions", usage.Funding.Provider);
        Assert.Equal(0, s.Usage().Current.HeldJobs);
        using var dashboard = System.Text.Json.JsonDocument.Parse(CompanyJson.Encode(s.Dashboard()));
        Assert.Empty(dashboard.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Empty(dashboard.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.True(s.VerifyAudit().Intact);
        Assert.All(s.AuditEntries().Where(a => a.Kind == "decision"), a => Assert.DoesNotContain("bakery", a.Body));
    }

    [Fact]
    public void UnknownPartialAccountingHoldsTheMonthlyAllowanceAcrossMonthAndRestart()
    {
        var s = Store();
        s.Apply(C("usage.configure") with { Body = CompanyJson.Encode(new CompanyUsageLimits(1, 100000)) });
        var d = Start(s);
        s.FinishDecision(d.Id, new("unknown", ReportedCostUsd: 0.002m, Receipt: "fictional:receipt-1"));
        Assert.Null(s.Decision(d.Id).Usage);
        Assert.Equal(0.002m, Assert.Single(s.Usage().Entries).ReportedUsd);
        _now = _now.AddMinutes(3);
        var restarted = Store();
        restarted.Recover();
        Assert.Equal(1, restarted.Usage().Current.CarriedJobs);
        Assert.Equal(CompanyStore.DecisionPlanningTokens, restarted.Usage().Current.CarriedTokens);
        Assert.Null(restarted.TakeDecisionDispatch(d.Id));
        Assert.Throws<WorkspaceException>(() => restarted.Apply(Run(Prepare(restarted))));
        Assert.Equal("unknown", restarted.Decision(d.Id).State);
    }

    [Fact]
    public void ARestartOrFenceWithholdsLateAdviceButKeepsItsActualUsage()
    {
        var s = Store();
        var d = Start(s);
        var restarted = Store();
        restarted.Recover();
        Assert.Equal("unknown", restarted.Decision(d.Id).State);
        s.FinishDecision(d.Id, Answer());
        Assert.Equal("withheld", restarted.Decision(d.Id).State);
        Assert.Null(restarted.Decision(d.Id).Result);
        Assert.Equal(120, restarted.Usage().Current.Tokens);
        var second = Start(s);
        s.FenceDecision(second.Id);
        s.FinishDecision(second.Id, Answer());
        Assert.Equal("withheld", s.Decision(second.Id).State);
        Assert.Equal(240, s.Usage().Current.Tokens);
    }

    [Fact]
    public void RevocationBeforeClaimReleasesZeroAndAfterClaimWithholdsTheAnswer()
    {
        var s = Store();
        var d = Prepare(s);
        s.Apply(Run(d));
        _connected = false;
        Assert.Null(s.TakeDecisionDispatch(d.Id));
        Assert.Equal("not_sent", s.Decision(d.Id).State);
        Assert.Equal(0, Assert.Single(s.Usage().Entries).ActualTokens);
        _connected = true;
        var second = Start(s);
        _connected = false;
        s.FinishDecision(second.Id, Answer());
        Assert.Equal("withheld", s.Decision(second.Id).State);
        Assert.Null(s.Decision(second.Id).Result);
        Assert.Equal(120, s.Usage().Current.Tokens);
    }

    [Fact]
    public void OverspendingRequiresExactNonReducingOwnerReconciliation()
    {
        var s = Store();
        var d = Start(s);
        s.FinishDecision(d.Id, Answer(0.02m, 22000));
        d = s.Decision(d.Id);
        Assert.Equal("unknown", d.State);
        Assert.Equal(1, s.Usage().Current.HeldJobs);
        Assert.Throws<WorkspaceException>(() => s.Apply(Reconcile(d, 21999, 0.02m)));
        Assert.Throws<WorkspaceException>(() => s.Apply(Reconcile(d, 22000, 0.019m)));
        Assert.Throws<WorkspaceException>(() => s.Apply(Reconcile(d, 22000, 0.02m, false)));
        Assert.Throws<WorkspaceException>(() => s.Apply(Reconcile(d, 22000, 0.02m, receipt: "fictional:another")));
        var reconciled = Apply<CompanyDecision>(s, Reconcile(d, 22000, 0.02m));
        Assert.Equal("reconciled", reconciled.State);
        Assert.Null(reconciled.Result);
        Assert.Equal(22020, s.Usage().Current.Tokens);
        Assert.Equal(0.02m, s.Usage().Current.ReportedUsd);
        Assert.Equal(0, s.Usage().Current.HeldJobs);
        s.FinishDecision(d.Id, Answer());
        Assert.Equal("reconciled", s.Decision(d.Id).State);
        Assert.Throws<WorkspaceException>(() => s.Apply(Reconcile(reconciled, 22000, 0.02m)));
    }

    [Fact]
    public void UnstartedCancellationDoesNotSpendAndCannotCancelAdmittedWork()
    {
        var s = Store();
        var d = Prepare(s);
        var cancel = C("decision.cancel") with { Id = d.Id, ExpectedVersion = d.Version, Digest = d.Digest };
        Assert.Equal("canceled", Apply<CompanyDecision>(s, cancel).State);
        Assert.Empty(s.Usage().Entries);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d)));
        var started = Start(s);
        Assert.Throws<WorkspaceException>(() =>
            s.Apply(
                cancel with
                {
                    RequestId = Guid.NewGuid().ToString("D"),
                    Id = started.Id,
                    ExpectedVersion = started.Version,
                    Digest = started.Digest,
                }
            )
        );
    }

    [Fact]
    public void OnlyCurrentOwnersCanPrepareAuthorizeCancelOrReconcile()
    {
        var s = Store();
        s.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var actor = s.SignIn("fictional-reviewer", "reviewer@example.invalid", true, "Reviewer")!;
        foreach (var action in new[] { "decision.prepare", "decision.run", "decision.cancel", "decision.reconcile" })
            Assert.Throws<WorkspaceException>(() =>
                s.Apply(PrepareCommand() with { Action = action }, actor.Subject, out _)
            );
        Assert.Empty(s.Usage().Entries);
    }

    [Fact]
    public void InvalidAdviceIsWithheldWhileItsKnownChargeIsSettled()
    {
        var s = Store();
        var d = Start(s);
        s.FinishDecision(d.Id, Answer() with { Choice = "publish" });
        Assert.Equal("withheld", s.Decision(d.Id).State);
        Assert.Null(s.Decision(d.Id).Result);
        Assert.Equal(0.001m, s.Usage().Current.ReportedUsd);
    }

    [Fact]
    public void UnclaimedCompletionAndIncompleteUsageNeverTurnIntoFreeCompletedWork()
    {
        var s = Store();
        var d = Prepare(s);
        s.Apply(Run(d));
        s.FinishDecision(d.Id, Answer());
        Assert.Equal("running", s.Decision(d.Id).State);
        Assert.NotNull(s.TakeDecisionDispatch(d.Id));
        s.FinishDecision(d.Id, Answer() with { Usage = null });
        Assert.Equal("unknown", s.Decision(d.Id).State);
        Assert.Equal(1, s.Usage().Current.HeldJobs);
        Assert.Equal(0.001m, Assert.Single(s.Usage().Entries).ReportedUsd);
    }

    [Fact]
    public void MonthlyAndCompanyPauseControlsRefuseDecisionAdmission()
    {
        var s = Store();
        var d = Prepare(s);
        s.Apply(C("usage.configure") with { Body = CompanyJson.Encode(new CompanyUsageLimits(1, 1)) });
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d)));
        Assert.Empty(s.Usage().Entries);
        s.Apply(
            C("usage.configure") with
            {
                ExpectedVersion = s.Usage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyUsageLimits()),
            }
        );
        s.Apply(
            C("company.work.configure") with
            {
                ExpectedVersion = s.WorkControl().Version,
                Decision = "pause-new-work",
            }
        );
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(d)));
        Assert.Empty(s.Usage().Entries);
    }

    [Fact]
    public async Task TheRunnerDispatchesOneReviewedPacketAndNeverReplaysItsOperation()
    {
        var s = Store();
        var d = Prepare(s);
        var command = Run(d);
        var calls = 0;
        using var scheduler = new CompanyScheduler(s, new UnavailableModelProvider());
        Task<CompanyDecisionOutcome> Dispatch(CompanyDecision claim, CancellationToken token)
        {
            calls++;
            Assert.True(claim.DispatchTaken);
            Assert.Equal(d.Packet.Json, claim.Packet.Json);
            Assert.Equal(d.Digest, claim.Digest);
            Assert.Equal(1, s.Usage().Current.HeldJobs);
            return Task.FromResult(Answer());
        }
        var first = await scheduler.RunDecisionOnceAsync(
            command,
            Owner,
            Dispatch,
            TestContext.Current.CancellationToken
        );
        var second = await scheduler.RunDecisionOnceAsync(
            command,
            Owner,
            Dispatch,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(first, second);
        Assert.Equal(1, calls);
        Assert.Equal("completed", s.Decision(d.Id).State);
    }

    [Fact]
    public async Task AProviderExceptionRetainsUnknownAccountingAndDoesNotRetry()
    {
        var s = Store();
        var d = Prepare(s);
        var command = Run(d);
        var calls = 0;
        using var scheduler = new CompanyScheduler(s, new UnavailableModelProvider());
        Task<CompanyDecisionOutcome> Dispatch(CompanyDecision claim, CancellationToken token)
        {
            calls++;
            throw new IOException("Fictional lost reply");
        }
        await scheduler.RunDecisionOnceAsync(command, Owner, Dispatch, TestContext.Current.CancellationToken);
        await scheduler.RunDecisionOnceAsync(command, Owner, Dispatch, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        Assert.Equal("unknown", s.Decision(d.Id).State);
        Assert.Equal(1, s.Usage().Current.HeldJobs);
        Assert.Null(s.Decision(d.Id).Usage);
    }

    [Fact]
    public async Task CancellationReturnsWithAHoldAndLateProviderAdviceCannotBecomeAccepted()
    {
        var s = Store();
        var d = Prepare(s);
        using var scheduler = new CompanyScheduler(s, new UnavailableModelProvider());
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var reply = new TaskCompletionSource<CompanyDecisionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CompanyDecisionOutcome> Dispatch(CompanyDecision claim, CancellationToken token)
        {
            entered.SetResult();
            return reply.Task;
        }
        var run = scheduler.RunDecisionOnceAsync(Run(d), Owner, Dispatch, cancel.Token);
        await entered.Task;
        cancel.Cancel();
        await run;
        Assert.Equal("unknown", s.Decision(d.Id).State);
        Assert.Equal(1, s.Usage().Current.HeldJobs);
        reply.SetResult(Answer());
        for (var i = 0; i < 100 && s.Decision(d.Id).State == "unknown"; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("withheld", s.Decision(d.Id).State);
        Assert.Null(s.Decision(d.Id).Result);
        Assert.Equal(120, s.Usage().Current.Tokens);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
