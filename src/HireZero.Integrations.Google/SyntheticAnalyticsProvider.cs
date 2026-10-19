using System.Security.Cryptography;
using System.Text;
using HireZero.Tools.Analytics;

namespace HireZero.Integrations.Google;

// Deterministic fictional analytics behind the same analytics.* contracts (docs/adr/0029-marketing-analytics-reads.md),
// for demos and browser checks when HIREZERO_SYNTHETIC_ANALYTICS=1. Every report says it is synthetic. The same date
// always gives the same numbers; a planted spike every 37th day of the year gives anomaly detection something to find.
public sealed class SyntheticAnalyticsProvider : IToolProvider, IIntegrationStatusSource
{
    public const string Source = "Synthetic fictional data";

    private static readonly (string Key, double Share)[] Channels =
    [
        ("Organic Search", 0.42),
        ("Direct", 0.24),
        ("Referral", 0.12),
        ("Organic Social", 0.1),
        ("Email", 0.07),
        ("Paid Search", 0.05),
    ];

    private static readonly (string Key, double Share)[] LandingPages =
    [
        ("/", 0.35),
        ("/pricing", 0.18),
        ("/blog/fictional-field-guide", 0.16),
        ("/features", 0.14),
        ("/contact", 0.07),
        ("/blog/example-case-study", 0.1),
    ];

    private static readonly (string Key, double Share)[] Devices =
    [
        ("desktop", 0.58),
        ("mobile", 0.38),
        ("tablet", 0.04),
    ];

    private static readonly (string Key, double Share)[] Countries =
    [
        ("Fictionland", 0.5),
        ("Exampleton", 0.3),
        ("Samplestan", 0.2),
    ];

    private static readonly (string Key, double Share)[] Sources =
    [
        ("search.example", 0.42),
        ("(direct)", 0.24),
        ("partner.example", 0.12),
        ("social.example", 0.1),
        ("newsletter", 0.07),
        ("ads.example", 0.05),
    ];

    private static readonly (string Key, double Share)[] Queries =
    [
        ("fictional widget", 0.3),
        ("example widget pricing", 0.2),
        ("how to use a fictional widget", 0.15),
        ("widget alternatives", 0.12),
        ("fictional company reviews", 0.1),
        ("widget setup guide", 0.08),
        ("sample widget vs gadget", 0.05),
    ];

    public IntegrationStatus Status() =>
        new("synthetic-analytics", "Synthetic analytics", true, "Fictional, deterministic data for demos and checks.");

    public void Register(ToolRegistry registry)
    {
        registry.Register(AnalyticsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AnalyticsTools.Traffic, (request, _) => Task.FromResult(Traffic(request)));
        registry.Register(AnalyticsTools.Search, (request, _) => Task.FromResult(Search(request)));
    }

    private static double Noise(string key, DateOnly day)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + day.DayNumber));
        return BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue; // 0..1
    }

    private static double Spike(DateOnly day) => day.DayOfYear % 37 == 0 ? 2.6 : 1;

    // One day's site-wide traffic: weekday rhythm, slow growth, noise and the planted spike.
    private static TrafficRow Day(DateOnly day)
    {
        var weekday = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.7 : 1.0;
        var growth = 1 + (day.DayNumber % 365) / 3650.0;
        var sessions = Math.Round(420 * weekday * growth * (0.9 + 0.2 * Noise("sessions", day)) * Spike(day));
        var users = Math.Round(sessions * 0.82);
        return new(
            day.ToString("yyyy-MM-dd"),
            (long)sessions,
            (long)users,
            (long)Math.Round(users * 0.61),
            (long)Math.Round(sessions * 2.3),
            (long)Math.Round(sessions * (0.021 + 0.01 * Noise("key", day))),
            Math.Round(0.55 + 0.1 * Noise("engagement", day), 4)
        );
    }

    private static IEnumerable<DateOnly> Days(DateOnly start, DateOnly end)
    {
        for (var day = start; day <= end; day = day.AddDays(1))
            yield return day;
    }

    private static TrafficRow Sum(string key, IEnumerable<TrafficRow> rows)
    {
        var list = rows.ToArray();
        var sessions = list.Sum(r => r.Sessions);
        return new(
            key,
            sessions,
            list.Sum(r => r.Users),
            list.Sum(r => r.NewUsers),
            list.Sum(r => r.PageViews),
            list.Sum(r => r.KeyEvents),
            sessions == 0 ? 0 : Math.Round(list.Sum(r => r.EngagementRate * r.Sessions) / sessions, 4)
        );
    }

    private static TrafficRow Scale(string key, TrafficRow total, double share) =>
        new(
            key,
            (long)Math.Round(total.Sessions * share),
            (long)Math.Round(total.Users * share),
            (long)Math.Round(total.NewUsers * share),
            (long)Math.Round(total.PageViews * share),
            (long)Math.Round(total.KeyEvents * share),
            total.EngagementRate
        );

    public static TrafficReport Traffic(TrafficRequest request)
    {
        AnalyticsRequests.Check(request);
        var days = Days(request.Start, request.End).Select(Day).ToArray();
        var totals = Sum("total", days);
        TrafficRow[] rows = request.Breakdown switch
        {
            null => [],
            "date" => days,
            "channel" => [.. Channels.Select(c => Scale(c.Key, totals, c.Share))],
            "landing-page" => [.. LandingPages.Select(c => Scale(c.Key, totals, c.Share))],
            "device" => [.. Devices.Select(c => Scale(c.Key, totals, c.Share))],
            "country" => [.. Countries.Select(c => Scale(c.Key, totals, c.Share))],
            _ => [.. Sources.Select(c => Scale(c.Key, totals, c.Share))],
        };
        if (request.Breakdown is not (null or "date"))
            rows = [.. rows.OrderByDescending(r => r.Sessions)];
        return new(Source, request.Start, request.End, request.Breakdown, totals, [.. rows.Take(request.Limit)]);
    }

    private static SearchRow SearchDay(DateOnly day)
    {
        var impressions = Math.Round(
            2600 * (0.9 + 0.2 * Noise("impressions", day)) * (1 + (day.DayNumber % 365) / 3000.0)
        );
        var ctr = 0.045 + 0.01 * Noise("ctr", day);
        return new(
            day.ToString("yyyy-MM-dd"),
            (long)Math.Round(impressions * ctr * Spike(day)),
            (long)impressions,
            Math.Round(ctr, 4),
            Math.Round(8 + 3 * Noise("position", day), 1)
        );
    }

    public static SearchReport Search(SearchRequest request)
    {
        AnalyticsRequests.Check(request);
        var days = Days(request.Start, request.End).Select(SearchDay).ToArray();
        var clicks = days.Sum(d => d.Clicks);
        var impressions = days.Sum(d => d.Impressions);
        var totals = new SearchRow(
            "total",
            clicks,
            impressions,
            impressions == 0 ? 0 : Math.Round(clicks / (double)impressions, 4),
            impressions == 0 ? 0 : Math.Round(days.Sum(d => d.Position * d.Impressions) / impressions, 1)
        );
        SearchRow Share(string key, double share, int rank) =>
            new(
                key,
                (long)Math.Round(clicks * share),
                (long)Math.Round(impressions * share * 0.9),
                totals.Ctr,
                Math.Round(totals.Position - 2 + rank, 1)
            );
        SearchRow[] rows = request.Breakdown switch
        {
            "date" => days,
            "query" => [.. Queries.Select((q, i) => Share(q.Key, q.Share, i))],
            "page" => [.. LandingPages.Select((p, i) => Share("https://www.example.invalid" + p.Key, p.Share, i))],
            "country" => [.. Countries.Select((c, i) => Share(c.Key, c.Share, i))],
            _ => [.. Devices.Select((d, i) => Share(d.Key, d.Share, i))],
        };
        if (request.Breakdown != "date")
            rows = [.. rows.OrderByDescending(r => r.Clicks)];
        return new(Source, request.Start, request.End, request.Breakdown, totals, [.. rows.Take(request.Limit)]);
    }
}
