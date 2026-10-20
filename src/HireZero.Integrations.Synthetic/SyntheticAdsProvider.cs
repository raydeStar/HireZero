using System.Security.Cryptography;
using System.Text;
using HireZero.Tools.Advertising;

namespace HireZero.Integrations.Synthetic;

// Deterministic fictional ads behind the neutral ads.* contracts (docs/adr/0031-ads-read-contracts.md), for demos and
// checks when HIREZERO_SYNTHETIC_ADS=1. Money is in XTS, the ISO 4217 code reserved for testing, so it can never be
// mistaken for real spend. Planted situations: a campaign that spends without converting, one whose cost per
// conversion worsens over time, a paused campaign, and a spend spike every 41st day of the year.
public sealed class SyntheticAdsProvider : IToolProvider, IIntegrationStatusSource
{
    public const string Source = "Synthetic fictional ads";
    public const string Currency = "XTS";

    private sealed record Campaign(
        string Id,
        string Name,
        string Status,
        double DailySpend,
        double Ctr,
        double Cpc,
        double ConversionRate,
        double ValuePerConversion,
        double ConversionDrift
    );

    private static readonly Campaign[] Campaigns =
    [
        new("cmp-101", "Brand search", "enabled", 60, 0.12, 0.6, 0.11, 140, 0),
        new("cmp-102", "Generic search", "enabled", 140, 0.045, 1.9, 0.045, 120, 0),
        new("cmp-103", "Retargeting display", "enabled", 45, 0.009, 0.7, 0.03, 110, 0),
        new("cmp-104", "Prospecting social", "enabled", 90, 0.011, 0.95, 0.02, 95, 0.22),
        new("cmp-105", "Video awareness", "enabled", 35, 0.006, 0.4, 0, 0, 0),
        new("cmp-106", "Spring promotion", "paused", 0, 0.03, 1.2, 0.04, 100, 0),
    ];

    // Changes apply to this process's fictional campaigns only, so the approval flow can be tried end to end.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (string Status, decimal Budget)
    > _changed = new();

    public IntegrationStatus Status() =>
        new(
            "synthetic-ads",
            "Synthetic ads",
            true,
            "Fictional, deterministic ad data in XTS for demos and checks; approved changes apply only to this fictional data."
        );

    public void Register(ToolRegistry registry)
    {
        registry.Register(AdsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AdsTools.Performance, (request, _) => Task.FromResult(Performance(request)));
        registry.Register(AdsTools.Campaign, (request, _) => Task.FromResult(ReadCampaign(request)));
        registry.Register(AdsTools.Change, (change, _) => Task.FromResult(Change(change)));
    }

    // Generic search and Retargeting display draw on one shared budget, so a budget change to either is refused.
    private static (string? Id, bool Shared) BudgetOf(string campaign) =>
        campaign switch
        {
            "102" or "103" => ("901", true),
            "105" => (null, false),
            _ => ("9" + campaign, false),
        };

    // The id ads.campaign and ads.change accept; the "cmp-" form only seeds the fixture's noise, so its numbers stay put.
    private static string Number(string id) => id.Replace("cmp-", "", StringComparison.Ordinal);

    public AdsCampaign ReadCampaign(AdsCampaignRequest request)
    {
        var c =
            Campaigns.FirstOrDefault(c => Number(c.Id) == request.CampaignId)
            ?? throw new ToolFailureException(
                "refused",
                "No campaign " + request.CampaignId + " in this account.",
                404
            );
        var (budgetId, shared) = BudgetOf(request.CampaignId);
        var budget = budgetId is null ? (decimal?)null : Math.Round((decimal)Math.Max(c.DailySpend, 20) * 1.2m, 2);
        if (_changed.TryGetValue(request.CampaignId, out var changed))
            return new(request.CampaignId, c.Name, changed.Status, budgetId, changed.Budget, shared, Currency);
        return new(request.CampaignId, c.Name, c.Status, budgetId, budget, shared, Currency);
    }

    public AdsChangeResult Change(AdsChange change)
    {
        AdsTools.Check(change);
        var current = ReadCampaign(new(change.CampaignId));
        _changed[change.CampaignId] =
            change.Kind == AdsTools.Pause
                ? ("paused", current.DailyBudget ?? 0)
                : (current.Status, change.NewDailyBudget!.Value);
        return new("synthetic/campaigns/" + change.CampaignId);
    }

    private static double Noise(string key, DateOnly day)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + day.DayNumber));
        return BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
    }

    // One campaign on one day. A drifting campaign's conversion rate falls by ConversionDrift of itself per 30 days,
    // over a four-month cycle that then starts again, so every date shows a campaign getting worse.
    private static AdsRow Day(Campaign c, DateOnly day)
    {
        var spike = day.DayOfYear % 41 == 0 ? 2.2 : 1;
        var spend = Math.Round(c.DailySpend * (0.85 + 0.3 * Noise(c.Id + "spend", day)) * spike, 2);
        var clicks = c.Cpc == 0 ? 0 : (long)Math.Round(spend / c.Cpc);
        var impressions = c.Ctr == 0 ? 0 : (long)Math.Round(clicks / c.Ctr);
        var months = (day.DayNumber - new DateOnly(2026, 1, 1).DayNumber) / 30.0 % 4;
        if (months < 0)
            months += 4;
        var rate = c.ConversionRate * Math.Max(0.05, 1 - c.ConversionDrift * months);
        var conversions = Math.Round(clicks * rate * (0.8 + 0.4 * Noise(c.Id + "conv", day)), 1);
        return new(
            day.ToString("yyyy-MM-dd"),
            c.Name,
            c.Status,
            impressions,
            clicks,
            (decimal)spend,
            conversions,
            (decimal)Math.Round(conversions * c.ValuePerConversion, 2)
        );
    }

    private static AdsRow Sum(string key, string? name, string? status, IEnumerable<AdsRow> rows)
    {
        var list = rows.ToArray();
        return new(
            key,
            name,
            status,
            list.Sum(r => r.Impressions),
            list.Sum(r => r.Clicks),
            list.Sum(r => r.Spend),
            Math.Round(list.Sum(r => r.Conversions), 1),
            list.Sum(r => r.ConversionValue)
        );
    }

    public static AdsReport Performance(AdsRequest request)
    {
        AdsTools.Check(request);
        var days = new List<DateOnly>();
        for (var day = request.Start; day <= request.End; day = day.AddDays(1))
            days.Add(day);
        var byCampaign = Campaigns.ToDictionary(c => c, c => days.Select(d => Day(c, d)).ToArray());
        var totals = Sum("total", null, null, byCampaign.Values.SelectMany(r => r));
        AdsRow[] rows =
            request.Breakdown == "date"
                ?
                [
                    .. days.Select(d =>
                        Sum(d.ToString("yyyy-MM-dd"), null, null, byCampaign.Values.Select(r => r[days.IndexOf(d)]))
                    ),
                ]
                :
                [
                    .. byCampaign
                        .Select(p => Sum(Number(p.Key.Id), p.Key.Name, p.Key.Status, p.Value))
                        .OrderByDescending(r => r.Spend),
                ];
        return new(
            Source,
            Currency,
            request.Start,
            request.End,
            request.Breakdown,
            totals,
            [.. rows.Take(request.Limit)]
        );
    }
}
