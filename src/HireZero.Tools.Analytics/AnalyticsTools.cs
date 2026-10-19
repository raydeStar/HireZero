namespace HireZero.Tools.Analytics;

// Vendor-neutral analytics reads (docs/adr/0029-marketing-analytics-reads.md). Dates are inclusive calendar days in
// the property's own time zone. Key and page text in results is untrusted third-party data.

public sealed record AnalyticsStatusRequest;

// Breakdown is null for totals only, or one of TrafficBreakdowns. Limit is 1 to 100 rows.
public sealed record TrafficRequest(DateOnly Start, DateOnly End, string? Breakdown, int Limit);

public sealed record TrafficRow(
    string Key,
    long Sessions,
    long Users,
    long NewUsers,
    long PageViews,
    long KeyEvents,
    double EngagementRate
);

// Source names where the data came from, for example "GA4 property 123" or "Synthetic fictional data".
public sealed record TrafficReport(
    string Source,
    DateOnly Start,
    DateOnly End,
    string? Breakdown,
    TrafficRow Totals,
    TrafficRow[] Rows
);

// Breakdown is one of SearchBreakdowns. Limit is 1 to 100 rows.
public sealed record SearchRequest(DateOnly Start, DateOnly End, string Breakdown, int Limit);

public sealed record SearchRow(string Key, long Clicks, long Impressions, double Ctr, double Position);

public sealed record SearchReport(
    string Source,
    DateOnly Start,
    DateOnly End,
    string Breakdown,
    SearchRow Totals,
    SearchRow[] Rows
);

public static partial class AnalyticsTools
{
    public static readonly string[] TrafficBreakdowns =
    [
        "date",
        "channel",
        "landing-page",
        "device",
        "country",
        "source",
    ];
    public static readonly string[] SearchBreakdowns = ["query", "page", "date", "country", "device"];

    // The longest range one read may cover.
    public const int MaxDays = 400;

    public static readonly ToolContract<AnalyticsStatusRequest, IntegrationStatus> Status = new(
        "analytics.status",
        ToolRisk.Read,
        "Which web analytics and search source is connected, and why not."
    );

    public static readonly ToolContract<TrafficRequest, TrafficReport> Traffic = new(
        "analytics.traffic",
        ToolRisk.Read,
        "Read website traffic (sessions, users, page views, key events, engagement) for a date range."
    );

    public static readonly ToolContract<SearchRequest, SearchReport> Search = new(
        "analytics.search",
        ToolRisk.Read,
        "Read search performance (clicks, impressions, click-through rate, position) for a date range."
    );
}

// The request rules every analytics provider applies before it sends anything, so all providers refuse alike.
public static partial class AnalyticsTools
{
    private static void Range(DateOnly start, DateOnly end, int limit)
    {
        if (end < start)
            throw new ToolFailureException("refused", "The range ends before it starts.", 422);
        if (end.DayNumber - start.DayNumber + 1 > AnalyticsTools.MaxDays)
            throw new ToolFailureException("refused", "Read at most " + AnalyticsTools.MaxDays + " days at once.", 422);
        if (limit is < 1 or > 100)
            throw new ToolFailureException("refused", "Read between 1 and 100 rows.", 422);
    }

    public static void Check(TrafficRequest request)
    {
        Range(request.Start, request.End, request.Limit);
        if (request.Breakdown is { } b && !AnalyticsTools.TrafficBreakdowns.Contains(b))
            throw new ToolFailureException("refused", "Unknown traffic breakdown: " + b, 422);
    }

    public static void Check(SearchRequest request)
    {
        Range(request.Start, request.End, request.Limit);
        if (!AnalyticsTools.SearchBreakdowns.Contains(request.Breakdown))
            throw new ToolFailureException("refused", "Unknown search breakdown: " + request.Breakdown, 422);
    }
}
