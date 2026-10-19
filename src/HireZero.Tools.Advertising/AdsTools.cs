namespace HireZero.Tools.Advertising;

// Vendor-neutral ads tools (docs/adr/0031-ads-read-contracts.md, docs/adr/0033-approved-ad-changes.md). Reads are Read
// tools; the one change tool is External and runs only as an owner-approved publication of one exact call. Dates are inclusive days in the ad account's
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

public sealed record AdsCampaignRequest(string CampaignId);

// One campaign's live state. Budget is the daily amount in the account's currency; BudgetShared is true when other
// campaigns draw on the same budget, so changing it would change them too.
public sealed record AdsCampaign(
    string Id,
    string Name,
    string Status,
    string? BudgetId,
    decimal? DailyBudget,
    bool BudgetShared,
    string Currency
);

// One exact change, carrying the state it was approved against so a provider can refuse when the campaign moved.
// Kind is pause (Status must be enabled) or budget (DailyBudget becomes NewDailyBudget).
public sealed record AdsChange(
    string CampaignId,
    string Kind,
    string ExpectedStatus,
    string? BudgetId,
    decimal? ExpectedDailyBudget,
    decimal? NewDailyBudget
);

public sealed record AdsChangeResult(string Reference);

public static class AdsTools
{
    public const string Pause = "pause";
    public const string Budget = "budget";

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

    public static readonly ToolContract<AdsCampaignRequest, AdsCampaign> Campaign = new(
        "ads.campaign",
        ToolRisk.Read,
        "Read one campaign's live status and daily budget."
    );

    public static readonly ToolContract<AdsChange, AdsChangeResult> Change = new(
        "ads.change",
        ToolRisk.External,
        "Pause one campaign or set its daily budget, only as an owner-approved publication."
    );

    // The rules every provider applies to a change before it sends anything.
    public static void Check(AdsChange change)
    {
        if (!change.CampaignId.All(char.IsAsciiDigit) || change.CampaignId.Length is 0 or > 20)
            throw new ToolFailureException("refused", "A campaign id is digits.", 422);
        var valid = change.Kind switch
        {
            Pause => change.ExpectedStatus == "enabled",
            Budget => change.BudgetId is { Length: > 0 }
                && change.ExpectedDailyBudget > 0
                && change.NewDailyBudget > 0
                && change.NewDailyBudget != change.ExpectedDailyBudget,
            _ => false,
        };
        if (!valid)
            throw new ToolFailureException("refused", "The change is not a valid pause or budget change.", 422);
    }

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
