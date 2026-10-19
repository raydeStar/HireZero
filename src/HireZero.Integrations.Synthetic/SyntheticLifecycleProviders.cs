using System.Security.Cryptography;
using System.Text;
using HireZero.Tools.Crm;
using HireZero.Tools.Email;

namespace HireZero.Integrations.Synthetic;

// Deterministic fictional email campaigns and CRM pipeline behind the neutral email.* and crm.* contracts
// (docs/adr/0035-email-and-crm-read-contracts.md), for demos and checks. Aggregates only, like the contracts; money is
// in XTS. Planted situations: a campaign to an imported list with high bounces every sixth week, a complaint spike every
// ninth week, a lead source that brings contacts but no deals, and a win rate that sags over a repeating cycle.
internal static class Fiction
{
    public static double Noise(string key, int day)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + day));
        return BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
    }
}

public sealed class SyntheticEmailProvider : IToolProvider, IIntegrationStatusSource
{
    public const string Source = "Synthetic fictional email";

    public IntegrationStatus Status() =>
        new(
            "synthetic-email",
            "Synthetic email",
            true,
            "Fictional, deterministic email campaign totals for demos and checks."
        );

    public void Register(ToolRegistry registry)
    {
        registry.Register(EmailTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(EmailTools.Campaigns, (request, _) => Task.FromResult(Campaigns(request)));
    }

    // One newsletter every Tuesday; the sixth week's goes to an imported list, the ninth week's draws complaints.
    private static EmailCampaignRow Campaign(DateOnly sent)
    {
        var week = sent.DayNumber / 7;
        var imported = week % 6 == 0;
        var noisy = week % 9 == 0;
        var recipients = 12000L + sent.DayNumber % 365 * 4 + (imported ? 6000 : 0);
        var bounceRate = imported ? 0.045 : 0.004 + 0.003 * Fiction.Noise("bounce", sent.DayNumber);
        var delivered = (long)Math.Round(recipients * (1 - bounceRate));
        var opens = (long)Math.Round(delivered * (0.38 + 0.08 * Fiction.Noise("open", sent.DayNumber)));
        var clicks = (long)Math.Round(delivered * (0.025 + 0.015 * Fiction.Noise("click", sent.DayNumber)));
        var unsubscribes = (long)
            Math.Round(delivered * (imported ? 0.009 : 0.002 + 0.002 * Fiction.Noise("unsub", sent.DayNumber)));
        var complaints = (long)
            Math.Round(delivered * (noisy ? 0.0025 : 0.0003 * Fiction.Noise("complaint", sent.DayNumber)));
        return new(
            "nl-" + sent.ToString("yyyyMMdd"),
            imported ? "Spring offer to imported list" : "Weekly newsletter " + sent.ToString("yyyy-MM-dd"),
            sent,
            recipients,
            delivered,
            opens,
            clicks,
            unsubscribes,
            recipients - delivered,
            complaints
        );
    }

    public static EmailReport Campaigns(EmailCampaignsRequest request)
    {
        EmailTools.Check(request);
        var rows = new List<EmailCampaignRow>();
        for (var day = request.End; day >= request.Start; day = day.AddDays(-1))
            if (day.DayOfWeek == DayOfWeek.Tuesday)
                rows.Add(Campaign(day));
        var totals = new EmailCampaignRow(
            "total",
            null,
            null,
            rows.Sum(r => r.Recipients),
            rows.Sum(r => r.Delivered),
            rows.Sum(r => r.Opens),
            rows.Sum(r => r.Clicks),
            rows.Sum(r => r.Unsubscribes),
            rows.Sum(r => r.Bounces),
            rows.Sum(r => r.Complaints)
        );
        return new(Source, request.Start, request.End, totals, [.. rows.Take(request.Limit)]);
    }
}

public sealed class SyntheticCrmProvider : IToolProvider, IIntegrationStatusSource
{
    public const string Source = "Synthetic fictional CRM";

    private static readonly (string Name, double ContactsPerDay, double DealRate, double WinRate)[] Sources =
    [
        ("Organic search", 9, 0.12, 0.30),
        ("Paid search", 6, 0.10, 0.22),
        ("Email", 4, 0.15, 0.35),
        ("Referral", 2, 0.30, 0.45),
        ("Events", 3, 0, 0),
    ];

    public IntegrationStatus Status() =>
        new(
            "synthetic-crm",
            "Synthetic CRM",
            true,
            "Fictional, deterministic pipeline totals in XTS for demos and checks."
        );

    public void Register(ToolRegistry registry)
    {
        registry.Register(CrmTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(CrmTools.Pipeline, (request, _) => Task.FromResult(Pipeline(request)));
    }

    // Win rates sag by up to 60% over a repeating four-month cycle that starts on 2026-01-01.
    private static double Cycle(int day)
    {
        var months = (day - new DateOnly(2026, 1, 1).DayNumber) / 30.0 % 4;
        return 1 - 0.15 * (months < 0 ? months + 4 : months);
    }

    public static PipelineReport Pipeline(PipelineRequest request)
    {
        CrmTools.Check(request);
        var sources = new List<SourceRow>();
        foreach (var (name, perDay, dealRate, winRate) in Sources)
        {
            double contacts = 0,
                deals = 0,
                won = 0;
            for (var day = request.Start; day <= request.End; day = day.AddDays(1))
            {
                var c = perDay * (0.7 + 0.6 * Fiction.Noise(name + "contacts", day.DayNumber));
                contacts += c;
                deals += c * dealRate;
                won += c * dealRate * winRate * Cycle(day.DayNumber);
            }
            var wonDeals = (long)Math.Round(won);
            sources.Add(new(name, (long)Math.Round(contacts), (long)Math.Round(deals), wonDeals, wonDeals * 2400m));
        }
        var newDeals = sources.Sum(s => s.NewDeals);
        var wonTotal = sources.Sum(s => s.WonDeals);
        var lost = (long)Math.Round(Math.Max(0, newDeals * 0.55 - wonTotal * 0.2));
        var end = request.End.DayNumber;
        PipelineStage Stage(string stage, int order, double open) =>
            new(
                stage,
                order,
                (long)Math.Round(open * (0.9 + 0.2 * Fiction.Noise(stage, end))),
                (decimal)Math.Round(open * 2400 * (0.9 + 0.2 * Fiction.Noise(stage + "v", end)))
            );
        return new(
            Source,
            "XTS",
            request.Start,
            request.End,
            [Stage("Lead", 1, 140), Stage("Qualified", 2, 60), Stage("Proposal", 3, 25), Stage("Negotiation", 4, 9)],
            sources.Sum(s => s.NewContacts),
            newDeals,
            wonTotal,
            sources.Sum(s => s.WonValue),
            lost,
            lost * 2100m,
            wonTotal == 0 ? null : Math.Round(34 + 8 * Fiction.Noise("close", end), 1),
            [.. sources]
        );
    }
}
