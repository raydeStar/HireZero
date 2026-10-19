using System.Globalization;
using System.Text;

namespace HireZero.Roles.Marketing;

// A/B tests for conversion rates (docs/adr/0030-ab-test-design-and-significance.md). Two deterministic actions do the
// arithmetic, so no model ever computes a statistic: ab-design sizes a fixed-horizon test (two-sided two-proportion
// z-test, Bonferroni-corrected for several variants against one control) and ab-significance reads its result (lift,
// confidence interval, p-value and a sample-ratio check). The measured ab-test skill writes the plan or the readout from
// the approved results.
public static class MarketingExperiments
{
    public const string DesignAction = "ab-design";
    public const string SignificanceAction = "ab-significance";
    public const string Skill = "ab-test";

    public static readonly EmployeeAction Design = new(
        DesignAction,
        "Size an A/B test",
        "Compute the visitors each variant needs and how long the test must run to detect the lift you care about.",
        [
            new("baseline", "Current conversion rate % (for example 3.2)", 8),
            new("lift", "Smallest relative lift worth detecting % (for example 10)", 8),
            new("daily", "Visitors entering the test per day", 10),
            new("variants", "Variants including control (2 to 5)", 1),
            new("alpha", "Significance level % (usually 5)", 4),
            new("power", "Power % (usually 80)", 4),
        ]
    );

    public static readonly EmployeeAction Significance = new(
        SignificanceAction,
        "Read an A/B test result",
        "Compute the lift, its confidence interval and p-value, and check the traffic split, for a control and one variant.",
        [
            new("control-visitors", "Control visitors", 12),
            new("control-conversions", "Control conversions", 12),
            new("variant-visitors", "Variant visitors", 12),
            new("variant-conversions", "Variant conversions", 12),
            new("alpha", "Significance level % (usually 5)", 4),
        ]
    );

    public const string Instructions =
        "You plan or read out an A/B test for the owner from the approved evidence: a test sizing and, when the test has run, its computed result. "
        + "Every number you state must appear in the evidence; never compute, round differently or invent statistics, and say unknown when the evidence is silent. "
        + "For a plan: state one hypothesis, the single primary metric, the variants, the audience and randomization unit, the guardrail metrics, the planned sample per variant and the run length from the evidence, and the stop rule (run the full planned length; no early stopping on a peek). "
        + "For a readout: say whether the result is statistically significant at the stated level, give the lift with its confidence interval, call out a sample-ratio warning before anything else, and separate what the data shows from hypotheses about why. "
        + "Recommend ship, iterate or stop with one sentence of reasoning. You cannot launch, change or stop tests. Return plain text.";

    // The visitors each variant needs to detect a relative lift with a two-sided two-proportion z-test.
    public static long SampleSizePerVariant(double baseline, double relativeLift, double alpha, double power)
    {
        var p1 = baseline;
        var p2 = baseline * (1 + relativeLift);
        var mean = (p1 + p2) / 2;
        var zAlpha = Normal.Quantile(1 - alpha / 2);
        var zBeta = Normal.Quantile(power);
        var numerator = zAlpha * Math.Sqrt(2 * mean * (1 - mean)) + zBeta * Math.Sqrt(p1 * (1 - p1) + p2 * (1 - p2));
        return (long)Math.Ceiling(numerator * numerator / ((p2 - p1) * (p2 - p1)));
    }

    public sealed record Result(
        double ControlRate,
        double VariantRate,
        double Difference,
        double RelativeLift,
        double Low,
        double High,
        double Z,
        double PValue,
        double SampleRatioPValue
    );

    public static Result Compare(
        long controlVisitors,
        long controlConversions,
        long variantVisitors,
        long variantConversions,
        double alpha
    )
    {
        double n1 = controlVisitors,
            n2 = variantVisitors;
        var p1 = controlConversions / n1;
        var p2 = variantConversions / n2;
        var pooled = (controlConversions + variantConversions) / (n1 + n2);
        var se = Math.Sqrt(pooled * (1 - pooled) * (1 / n1 + 1 / n2));
        var z = se == 0 ? 0 : (p2 - p1) / se;
        var unpooled = Math.Sqrt(p1 * (1 - p1) / n1 + p2 * (1 - p2) / n2);
        var margin = Normal.Quantile(1 - alpha / 2) * unpooled;
        // Sample ratio: a chi-square test (one degree of freedom) that the split matches the planned 50/50.
        var expected = (n1 + n2) / 2;
        var chi = Math.Sqrt(
            (n1 - expected) * (n1 - expected) / expected + (n2 - expected) * (n2 - expected) / expected
        );
        return new(
            p1,
            p2,
            p2 - p1,
            p1 == 0 ? double.NaN : (p2 - p1) / p1,
            p2 - p1 - margin,
            p2 - p1 + margin,
            z,
            2 * (1 - Normal.Cdf(Math.Abs(z))),
            2 * (1 - Normal.Cdf(chi))
        );
    }

    public static string DesignText(IReadOnlyDictionary<string, string> input)
    {
        var baseline = Number(input, "baseline", "The conversion rate", 0, 100, exclusive: true) / 100;
        var lift = Number(input, "lift", "The lift", 0, 1000, exclusive: true) / 100;
        var daily = Whole(input, "daily", "Daily visitors", 1, 100_000_000);
        var variants = (int)Whole(input, "variants", "Variants", 2, 5);
        var alpha = Level(input);
        var power = Number(input, "power", "Power", 50, 99.9, exclusive: false) / 100;
        if (baseline * (1 + lift) >= 1)
            throw new WorkspaceException("That lift would take the conversion rate to 100% or more.", 422);

        // Each variant is compared with the control, so the significance level is shared among the comparisons.
        var comparisons = variants - 1;
        var corrected = alpha / comparisons;
        var perVariant = SampleSizePerVariant(baseline, lift, corrected, power);
        var total = perVariant * variants;
        var days = (long)Math.Ceiling(total / (double)daily);
        var weeks = Math.Max(1, (long)Math.Ceiling(days / 7.0));
        var text = new StringBuilder();
        text.AppendLine(
            "A/B test sizing, computed by the ab-design action (two-sided two-proportion z-test, fixed horizon)."
        );
        text.AppendLine(
            $"- Current conversion rate: {Pct(baseline)}; smallest lift worth detecting: {Pct(lift)} relative, so {Pct(baseline)} to {Pct(baseline * (1 + lift))}."
        );
        text.AppendLine(
            $"- Variants: {variants} including control; significance level {Pct(alpha)}{(comparisons > 1 ? $", shared among {comparisons} comparisons with the control, so {Pct(corrected)} each (Bonferroni)" : "")}; power {Pct(power)}."
        );
        text.AppendLine($"- Visitors needed: {perVariant:N0} per variant, {total:N0} in total.");
        text.AppendLine(
            $"- At {daily:N0} visitors a day that is {days:N0} day(s); run {weeks} full week(s) ({weeks * 7} days) so every weekday is covered equally."
        );
        if (weeks * 7 > 56)
            text.AppendLine(
                "- Warning: longer than eight weeks. Results drift as audiences and seasons change; consider a larger lift, a higher-traffic page or fewer variants."
            );
        text.AppendLine(
            "- Rules for a valid result: split traffic evenly and at random by visitor; pick one primary metric now; do not stop early or extend because of a peek; check the split before reading the result."
        );
        return text.ToString();
    }

    public static string SignificanceText(IReadOnlyDictionary<string, string> input)
    {
        var controlVisitors = Whole(input, "control-visitors", "Control visitors", 1, long.MaxValue / 4);
        var controlConversions = Whole(input, "control-conversions", "Control conversions", 0, controlVisitors);
        var variantVisitors = Whole(input, "variant-visitors", "Variant visitors", 1, long.MaxValue / 4);
        var variantConversions = Whole(input, "variant-conversions", "Variant conversions", 0, variantVisitors);
        var alpha = Level(input);
        var r = Compare(controlVisitors, controlConversions, variantVisitors, variantConversions, alpha);
        var text = new StringBuilder();
        text.AppendLine("A/B test result, computed by the ab-significance action (two-sided two-proportion z-test).");
        if (r.SampleRatioPValue < 0.001)
            text.AppendLine(
                $"- WARNING: sample ratio mismatch. The split {controlVisitors:N0} / {variantVisitors:N0} is very unlikely from an even random split (p = {P(r.SampleRatioPValue)}). Find the cause before trusting this result."
            );
        text.AppendLine(
            $"- Control: {controlConversions:N0} of {controlVisitors:N0} converted ({Pct(r.ControlRate, 2)})."
        );
        text.AppendLine(
            $"- Variant: {variantConversions:N0} of {variantVisitors:N0} converted ({Pct(r.VariantRate, 2)})."
        );
        text.AppendLine(
            $"- Difference: {Points(r.Difference)} ({(double.IsNaN(r.RelativeLift) ? "relative lift undefined: the control has no conversions" : Signed(r.RelativeLift) + " relative")})."
        );
        text.AppendLine(
            $"- {Pct(1 - alpha)} confidence interval for the difference: {Points(r.Low)} to {Points(r.High)}."
        );
        text.AppendLine($"- z = {r.Z.ToString("0.00", CultureInfo.InvariantCulture)}, p = {P(r.PValue)}.");
        text.AppendLine(
            r.PValue < alpha
                ? $"- Statistically significant at the {Pct(alpha)} level: the variant {(r.Difference > 0 ? "converts better" : "converts worse")} than the control."
                : $"- Not statistically significant at the {Pct(alpha)} level: this test cannot tell the variant from the control."
        );
        text.AppendLine(
            $"- Sample ratio check: p = {P(r.SampleRatioPValue)} ({(r.SampleRatioPValue < 0.001 ? "mismatch" : "consistent with an even split")})."
        );
        text.AppendLine(
            "- Valid only if the test ran its full planned length without stopping early on a peek, and these are the planned primary metric's counts."
        );
        return text.ToString();
    }

    private static double Level(IReadOnlyDictionary<string, string> input) =>
        Number(input, "alpha", "The significance level", 0.1, 20, exclusive: false) / 100;

    private static double Number(
        IReadOnlyDictionary<string, string> input,
        string field,
        string name,
        double min,
        double max,
        bool exclusive
    )
    {
        var raw = input.TryGetValue(field, out var value) ? value.Trim().TrimEnd('%') : "";
        if (
            !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number)
            || (exclusive ? number <= min || number >= max : number < min || number > max)
        )
            throw new WorkspaceException(
                $"{name} must be a number {(exclusive ? "above" : "from")} {min.ToString(CultureInfo.InvariantCulture)} {(exclusive ? "and below" : "to")} {max.ToString(CultureInfo.InvariantCulture)}.",
                422
            );
        return number;
    }

    private static long Whole(IReadOnlyDictionary<string, string> input, string field, string name, long min, long max)
    {
        var raw = input.TryGetValue(field, out var value)
            ? value.Trim().Replace(",", "", StringComparison.Ordinal)
            : "";
        if (
            !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < min
            || number > max
        )
            throw new WorkspaceException($"{name} must be a whole number from {min:N0} to {max:N0}.", 422);
        return number;
    }

    private static string Pct(double fraction, int decimals = 1) =>
        (fraction * 100).ToString("0." + new string('#', Math.Max(decimals, 1)), CultureInfo.InvariantCulture) + "%";

    private static string Signed(double fraction) =>
        (fraction * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";

    private static string Points(double fraction) =>
        (fraction * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " points";

    private static string P(double p) => p < 0.0001 ? "< 0.0001" : p.ToString("0.0000", CultureInfo.InvariantCulture);
}

// The standard normal distribution, to well under the precision any test report shows.
public static class Normal
{
    // Cumulative distribution, from erfc (W. J. Cody's rational approximations as given in Numerical Recipes, about
    // 1.2e-7 relative error).
    public static double Cdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2));

    private static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + 0.5 * z);
        var r =
            t
            * Math.Exp(
                -z * z
                    - 1.26551223
                    + t
                        * (
                            1.00002368
                            + t
                                * (
                                    0.37409196
                                    + t
                                        * (
                                            0.09678418
                                            + t
                                                * (
                                                    -0.18628806
                                                    + t
                                                        * (
                                                            0.27886807
                                                            + t
                                                                * (
                                                                    -1.13520398
                                                                    + t
                                                                        * (
                                                                            1.48851587
                                                                            + t * (-0.82215223 + t * 0.17087277)
                                                                        )
                                                                )
                                                        )
                                                )
                                        )
                                )
                        )
            );
        return x >= 0 ? r : 2 - r;
    }

    // Inverse cumulative distribution (Acklam's algorithm, about 1.15e-9 relative error).
    public static double Quantile(double p)
    {
        if (p is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(p));
        double[] a =
        [
            -3.969683028665376e+01,
            2.209460984245205e+02,
            -2.759285104469687e+02,
            1.383577518672690e+02,
            -3.066479806614716e+01,
            2.506628277459239e+00,
        ];
        double[] b =
        [
            -5.447609879822406e+01,
            1.615858368580409e+02,
            -1.556989798598866e+02,
            6.680131188771972e+01,
            -1.328068155288572e+01,
        ];
        double[] c =
        [
            -7.784894002430293e-03,
            -3.223964580411365e-01,
            -2.400758277161838e+00,
            -2.549732539343734e+00,
            4.374664141464968e+00,
            2.938163982698783e+00,
        ];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
        const double low = 0.02425;
        double x;
        if (p < low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            x =
                (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= 1 - low)
        {
            var q = p - 0.5;
            var r = q * q;
            x =
                (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5])
                * q
                / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            x =
                -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        return x;
    }
}
