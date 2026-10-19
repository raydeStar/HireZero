using HireZero.Core;
using HireZero.Roles.Marketing;

// A/B test arithmetic (docs/adr/0030-ab-test-design-and-significance.md) against reference values computed
// independently with Python's statistics.NormalDist: normal quantiles, sample sizes, a significant result, and a sample
// ratio mismatch. Numbers are fictional.
public sealed class MarketingExperimentsTests
{
    [Theory]
    [InlineData(0.975, 1.9599639845400536)]
    [InlineData(0.8, 0.8416212335729144)]
    [InlineData(0.999, 3.090232306167813)]
    [InlineData(0.001, -3.090232306167813)]
    public void NormalQuantilesMatchTheReference(double p, double expected) =>
        Assert.Equal(expected, Normal.Quantile(p), 6);

    [Fact]
    public void TheNormalDistributionMatchesTheReference() => Assert.Equal(0.9750021048517796, Normal.Cdf(1.96), 6);

    [Theory]
    [InlineData(0.10, 0.20, 0.05, 0.8, 3841)]
    [InlineData(0.032, 0.10, 0.05, 0.8, 49777)]
    [InlineData(0.032, 0.10, 0.025, 0.8, 60280)]
    [InlineData(0.05, 0.05, 0.05, 0.9, 163489)]
    public void SampleSizesMatchTheReference(double baseline, double lift, double alpha, double power, long expected) =>
        Assert.Equal(expected, MarketingExperiments.SampleSizePerVariant(baseline, lift, alpha, power));

    [Fact]
    public void ASignificantResultHasItsIntervalAndPValue()
    {
        var r = MarketingExperiments.Compare(1000, 100, 1000, 130, 0.05);
        Assert.Equal(2.102740605622114, r.Z, 6);
        Assert.Equal(0.03548845046647475, r.PValue, 5);
        Assert.Equal(0.0020679344393763725, r.Low, 6);
        Assert.Equal(0.05793206556062362, r.High, 6);
        Assert.Equal(0.3, r.RelativeLift, 9);

        var text = MarketingExperiments.SignificanceText(
            Input(
                ("control-visitors", "1,000"),
                ("control-conversions", "100"),
                ("variant-visitors", "1000"),
                ("variant-conversions", "130"),
                ("alpha", "5")
            )
        );
        Assert.Contains("Statistically significant at the 5% level: the variant converts better", text);
        Assert.Contains("p = 0.0355", text);
        Assert.Contains("+30.0% relative", text);
        Assert.DoesNotContain("WARNING", text);
    }

    [Fact]
    public void AnUnevenSplitIsCalledOutFirst()
    {
        var r = MarketingExperiments.Compare(10000, 500, 10600, 560, 0.05);
        Assert.Equal(2.9099921115882665e-05, r.SampleRatioPValue, 7);
        var text = MarketingExperiments.SignificanceText(
            Input(
                ("control-visitors", "10000"),
                ("control-conversions", "500"),
                ("variant-visitors", "10600"),
                ("variant-conversions", "560"),
                ("alpha", "5")
            )
        );
        Assert.StartsWith("- WARNING: sample ratio mismatch", text.Split('\n')[1]);
        Assert.Contains("Not statistically significant", text);
    }

    [Fact]
    public void ADesignSharesTheSignificanceLevelAndRunsWholeWeeks()
    {
        var text = MarketingExperiments.DesignText(
            Input(
                ("baseline", "3.2"),
                ("lift", "10%"),
                ("daily", "9000"),
                ("variants", "3"),
                ("alpha", "5"),
                ("power", "80")
            )
        );
        Assert.Contains("shared among 2 comparisons with the control, so 2.5% each (Bonferroni)", text);
        Assert.Contains("Visitors needed: 60,280 per variant, 180,840 in total.", text);
        Assert.Contains("that is 21 day(s); run 3 full week(s) (21 days)", text);
        Assert.DoesNotContain("Warning", text);

        var slow = MarketingExperiments.DesignText(
            Input(
                ("baseline", "3.2"),
                ("lift", "10"),
                ("daily", "500"),
                ("variants", "2"),
                ("alpha", "5"),
                ("power", "80")
            )
        );
        Assert.Contains("Warning: longer than eight weeks", slow);
    }

    [Theory]
    [InlineData("baseline", "0")]
    [InlineData("baseline", "100")]
    [InlineData("lift", "abc")]
    [InlineData("variants", "6")]
    [InlineData("daily", "-5")]
    [InlineData("power", "40")]
    public void ADesignRefusesNumbersOutOfRange(string field, string value)
    {
        var input = Input(
            ("baseline", "3.2"),
            ("lift", "10"),
            ("daily", "9000"),
            ("variants", "2"),
            ("alpha", "5"),
            ("power", "80")
        );
        input[field] = value;
        Assert.Equal(422, Assert.Throws<WorkspaceException>(() => MarketingExperiments.DesignText(input)).Status);
    }

    [Fact]
    public void AResultRefusesMoreConversionsThanVisitorsAndALiftPast100Percent()
    {
        Assert.Throws<WorkspaceException>(() =>
            MarketingExperiments.SignificanceText(
                Input(
                    ("control-visitors", "10"),
                    ("control-conversions", "11"),
                    ("variant-visitors", "10"),
                    ("variant-conversions", "1"),
                    ("alpha", "5")
                )
            )
        );
        Assert.Throws<WorkspaceException>(() =>
            MarketingExperiments.DesignText(
                Input(
                    ("baseline", "60"),
                    ("lift", "80"),
                    ("daily", "100"),
                    ("variants", "2"),
                    ("alpha", "5"),
                    ("power", "80")
                )
            )
        );
    }

    private static Dictionary<string, string> Input(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);
}
