using System.Text;
using HireZero.Tools.Analytics;

namespace HireZero.Roles.Marketing;

public static class MarketingSignals
{
    public const string Policy = "marketing-site-signals-v1";

    // Exactly two reads, no discovery loops and no inferred zeroes. A quiet counter is not a crystal ball.
    public static async Task<string> GatherAsync(IToolbox tools, DateOnly today, CancellationToken token)
    {
        var end = today.AddDays(-1);
        var start = end.AddDays(-13);
        var text = new StringBuilder(
            $"Connected site observations, requested {start:yyyy-MM-dd} through {end:yyyy-MM-dd}.\n"
        );
        var read = tools.ReadOnly();
        try
        {
            var daily = await read.InvokeAsync(AnalyticsTools.Daily, new DailyRequest(start, end), token);
            text.AppendLine("Daily site counts (UTC days), source: " + ContentGuard.Clean(daily.Source, 200).Text);
            foreach (var day in daily.Rows.Where(r => r.Day >= start && r.Day <= end).OrderBy(r => r.Day).Take(14))
                text.AppendLine(
                    $"{day.Day:yyyy-MM-dd}: visitors={Count(day.Visitors)}, page views={Count(day.PageViews)}, signups={Count(day.SignUps)}, accounts={Count(day.Accounts)}"
                );
            if (daily.Rows.Length == 0)
                text.AppendLine("No daily observations returned; counts unknown.");
        }
        catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
        {
            text.AppendLine("Daily site counts unavailable or not connected; counts unknown.");
        }
        token.ThrowIfCancellationRequested();
        try
        {
            var traffic = await read.InvokeAsync(
                AnalyticsTools.Traffic,
                new TrafficRequest(start, end, "landing-page", 5),
                token
            );
            text.AppendLine(
                "Landing-page traffic (source property calendar), source: "
                    + ContentGuard.Clean(traffic.Source, 200).Text
            );
            foreach (var page in traffic.Rows.Take(5))
                text.AppendLine(
                    $"{ContentGuard.Clean(page.Key, 200).Text}: sessions={page.Sessions}, users={page.Users}, page views={page.PageViews}, key events={page.KeyEvents}"
                );
            if (traffic.Rows.Length == 0)
                text.AppendLine("No landing-page observations returned; counts unknown.");
        }
        catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
        {
            text.AppendLine("Landing-page traffic unavailable or not connected; counts unknown.");
        }
        return text.ToString();
    }

    private static string Count(long? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
}
