using HireZero.Core;

public sealed partial class CompanyTaskCostTests
{
    private static CompanyProgressTaskCosts ProgressCosts(CompanyStore store) => store.Progress(Owner).TaskCosts!;

    [Fact]
    public void ProgressCostCoverageKeepsMissingCostsUnknownAndUsesOnlyTheLatestRecordedCorrection()
    {
        var s = Store();
        var empty = ProgressCosts(s);
        Assert.Equal(0, empty.CompletedTasks);
        Assert.Null(empty.ReconciledSubtotalUsd);
        Assert.Null(empty.AverageUsdPerCompletion);
        Assert.All(empty.OperatingCosts, c => Assert.Null(c.Usd));

        var first = Completed(s);
        var missing = ProgressCosts(s);
        Assert.Equal(1, missing.MissingReviews);
        Assert.Equal(first.Id, missing.NextReview!.TaskId);
        Assert.Equal("not-recorded", missing.NextReview.Reason);
        Assert.Null(missing.ReconciledSubtotalUsd);
        var draft = A<CompanyTaskCostReview>(s, Save(first, Costs(null)));
        Assert.Equal(1, ProgressCosts(s).MissingReviews);
        draft = A<CompanyTaskCostReview>(s, Save(first, Costs(.1m), draft));
        s.Apply(Record(draft));
        Assert.Equal(.52m, ProgressCosts(s).AverageUsdPerCompletion);

        var second = Completed(s);
        var partial = ProgressCosts(s);
        Assert.Equal(2, partial.CompletedTasks);
        Assert.Equal(1, partial.ReconciledTasks);
        Assert.Equal(.52m, partial.ReconciledSubtotalUsd);
        Assert.Null(partial.AverageUsdPerCompletion);
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(second))));
        var complete = ProgressCosts(s);
        Assert.Equal(2, complete.ReconciledTasks);
        Assert.Equal(.54m, complete.ReconciledSubtotalUsd);
        Assert.Equal(.27m, complete.AverageUsdPerCompletion);
        Assert.Null(complete.NextReview);

        var correction = A<CompanyTaskCostReview>(s, Save(first, Costs(.2m)));
        Assert.Equal(.54m, ProgressCosts(s).ReconciledSubtotalUsd);
        s.Apply(Record(correction));
        var corrected = ProgressCosts(Store());
        Assert.Equal(1.04m, corrected.ReconciledSubtotalUsd);
        Assert.Equal(.52m, corrected.AverageUsdPerCompletion);
        Assert.All(corrected.OperatingCosts, c => Assert.Equal(.2m, c.Usd));
        Assert.Equal(CompanyJson.Encode(corrected), CompanyJson.Encode(ProgressCosts(s)));
    }

    [Fact]
    public void ProgressCostsInvalidateChangedUsageAndNeverHideUnpricedAttemptsOrCompanyKeyEstimates()
    {
        var s = Store();
        var t = Completed(s);
        ExtraUsage(s, t, reported: .03m);
        ExtraUsage(s, t, estimated: .04m, companyKey: true);
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(t, Costs(.1m)))));
        var costs = ProgressCosts(s);
        Assert.Equal(.59m, costs.ReconciledSubtotalUsd);
        Assert.Equal(.05m, costs.ReportedModelUsd);
        Assert.Equal(.04m, costs.EstimatedModelUsd);
        Assert.Equal(.04m, costs.CompanyKeyModelUsd);
        Assert.Equal(1, costs.IncludesModelEstimates);

        ExtraUsage(s, t);
        costs = ProgressCosts(s);
        Assert.Equal(1, costs.StaleReviews);
        Assert.Equal("usage-changed", costs.NextReview!.Reason);
        Assert.Null(costs.ReconciledSubtotalUsd);
        Assert.Null(costs.AverageUsdPerCompletion);
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(t, Costs(.1m)))));
        costs = ProgressCosts(s);
        Assert.Equal(0, costs.StaleReviews);
        Assert.Equal(1, costs.IncompleteUsage);
        Assert.Equal("usage-incomplete", costs.NextReview!.Reason);
        Assert.Null(costs.ReconciledSubtotalUsd);
        Assert.All(costs.OperatingCosts, c => Assert.Null(c.Usd));
    }

    [Fact]
    public void ProgressOperatingCostsRequireTheCurrentCompanyOwnerAndExportNoReceiptText()
    {
        var s = Store();
        var completed = Completed(s);
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(completed, Costs(.1m)))));
        _ = MeasuredQueueChecks.Seed(s); // Unfinished tasks must not dilute the completed-task denominator.
        Assert.Equal(1, ProgressCosts(s).CompletedTasks);
        Assert.Equal(.52m, ProgressCosts(s).AverageUsdPerCompletion);
        Assert.Null(s.Progress().TaskCosts);
        Assert.Equal(0, ProgressCosts(Store("co-neighbor")).CompletedTasks);
        Assert.Null(ProgressCosts(Store("co-neighbor")).ReconciledSubtotalUsd);
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => s.Progress("unknown-member")).Status);
        foreach (var role in new[] { "viewer", "approver" })
        {
            var email = role + "@example.invalid";
            s.Apply(C("member.invite") with { Body = email, Decision = role });
            var member = s.SignIn("fictional-" + role, email, true, "Fictional member")!;
            Assert.Null(s.Progress(member.Subject).TaskCosts);
        }
        var exported = CompanyJson.Encode(s.Progress(Owner));
        Assert.DoesNotContain("invoice allocation", exported);
        Assert.DoesNotContain("internal work", exported);
        Assert.DoesNotContain(Owner, exported);
        var events = s.AuditEntries().Length;
        _ = ProgressCosts(s);
        Assert.Equal(events, s.AuditEntries().Length);
    }
}
