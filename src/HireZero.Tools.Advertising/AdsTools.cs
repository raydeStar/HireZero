namespace HireZero.Tools.Advertising;

// Vendor-neutral ads reads (docs/adr/0031-ads-read-contracts.md). Read only: changing campaigns, budgets or bids will be
// separate External tools that run only as an owner-approved publication. Dates are inclusive days in the ad account's
// time zone; money is in the account's currency. Names in results are untrusted third-party text.

public sealed record AdsStatusRequest;

// Breakdown is one of AdsTools.Breakdowns. Limit is 1 to 100 rows.
public sealed record AdsRequest(DateOnly Start, DateOnly End, string Breakdown, int Limit);

// Key is the campaign id or the date (yyyy-MM-dd). Name and Status are set for campaigns: Status is enabled, paused or
// removed. Conversions can be fractional where the platform attributes partially.
public sealed record AdsRow(
    string Key,
    string? Name,
    string? Status,
    long Impressions,
    long Clicks,
    decimal Spend,
    double Conversions,
    decimal ConversionValue
);

// Source names the account, for example "Synthetic fictional ads"; Currency is its ISO 4217 code.
public sealed record AdsReport(
    string Source,
    string Currency,
    DateOnly Start,
    DateOnly End,
    string Breakdown,
    AdsRow Totals,
    AdsRow[] Rows
);

public static class AdsTools
{
    public static readonly string[] Breakdowns = ["campaign", "date"];
    public static readonly string[] Statuses = ["enabled", "paused", "removed"];
    public const int MaxDays = 400;

    public static readonly ToolContract<AdsStatusRequest, IntegrationStatus> Status = new(
        "ads.status",
        ToolRisk.Read,
        "Which ads account is connected, and why not."
    );

    public static readonly ToolContract<AdsRequest, AdsReport> Performance = new(
        "ads.performance",
        ToolRisk.Read,
        "Read ad spend, impressions, clicks, conversions and conversion value by campaign or day."
    );

    // The request rules every ads provider applies before it sends anything.
    public static void Check(AdsRequest request)
    {
        if (request.End < request.Start)
            throw new ToolFailureException("refused", "The range ends before it starts.", 422);
        if (request.End.DayNumber - request.Start.DayNumber + 1 > MaxDays)
            throw new ToolFailureException("refused", "Read at most " + MaxDays + " days at once.", 422);
        if (request.Limit is < 1 or > 100)
            throw new ToolFailureException("refused", "Read between 1 and 100 rows.", 422);
        if (!Breakdowns.Contains(request.Breakdown))
            throw new ToolFailureException("refused", "Unknown ads breakdown: " + request.Breakdown, 422);
    }
}
