using System.Net;
using System.Text.RegularExpressions;
using HireZero.Tools.Web;

namespace HireZero.Integrations.Web;

public sealed partial class WebReader
{
    // Serialize research across reader instances. A courteous butler knocks once, then waits.
    private static readonly SemaphoreSlim ResearchGate = new(1, 1);
    private static DateTimeOffset NextResearchRequest;
    private static DateTimeOffset LastResearchRequest;
    private const string Agent = "HireZero-source-reader";

    public async Task<WebPage> ResearchAsync(WebResearchRequest request, CancellationToken token)
    {
        var origin = Checked(request.Origin);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        await ResearchGate.WaitAsync(deadline.Token);
        try
        {
            return await ReadPageAsync(request.Url, origin, deadline.Token);
        }
        finally
        {
            ResearchGate.Release();
        }
    }

    private static void SameOrigin(Uri url, Uri origin)
    {
        if (url.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
            throw Refused(
                "Research left the confirmed website. Confirm its final website address before importing it."
            );
    }

    private static async Task PaceResearchAsync(CancellationToken token, double seconds = 1)
    {
        var earliest = LastResearchRequest.AddSeconds(Math.Max(1, seconds));
        if (NextResearchRequest > earliest)
            earliest = NextResearchRequest;
        var wait = earliest - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, token);
        LastResearchRequest = DateTimeOffset.UtcNow;
        NextResearchRequest = LastResearchRequest.AddSeconds(Math.Max(1, seconds));
    }

    private async Task CheckResearchAsync(Uri url, Uri origin, CancellationToken token)
    {
        SameOrigin(url, origin);
        var robotsUrl = new Uri(origin.GetLeftPart(UriPartial.Authority) + "/robots.txt");
        string policy = "";
        for (var hop = 0; ; hop++)
        {
            SameOrigin(robotsUrl, origin);
            await PaceResearchAsync(token);
            using var response = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, robotsUrl),
                HttpCompletionOption.ResponseHeadersRead,
                token
            );
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                break;
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (hop >= MaxRedirects)
                    throw Refused("The website's crawl policy redirects too many times.");
                robotsUrl = Checked(new Uri(robotsUrl, location).ToString());
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw Refused(
                    "The website's crawl policy is unavailable or access is restricted. Try a permitted source later."
                );
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
                throw Refused("The website did not return a readable crawl policy.");
            policy = await ReadBoundedAsync(response.Content, token);
            break;
        }
        var rules = ResearchRules(policy, url);
        if (!rules.Allowed)
            throw Refused(
                "This page is excluded by the website's crawl policy. Use a permitted page or enter your own facts."
            );
        if (rules.Delay > 10)
            throw Refused("This website requests a longer crawl delay than this bounded research session supports.");
        NextResearchRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, rules.Delay));
        await PaceResearchAsync(token, Math.Max(1, rules.Delay));
    }

    // Longest matching agent group, then longest matching path; Allow wins ties. Unknown directives confer no access.
    public static (bool Allowed, double Delay) ResearchRules(string text, Uri url)
    {
        if (text.Length > 128 * 1024 || text.Count(c => c == '\n') > 2000)
            return (false, 1);
        var groups = new List<(List<string> Agents, List<(string Name, string Value)> Rules)>();
        List<string> agents = [];
        List<(string Name, string Value)> rules = [];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim().TrimStart('\uFEFF');
            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;
            var name = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (name == "user-agent")
            {
                if (rules.Count > 0)
                {
                    groups.Add((agents, rules));
                    agents = [];
                    rules = [];
                }
                agents.Add(value);
            }
            else if (agents.Count > 0)
                rules.Add((name, value));
        }
        groups.Add((agents, rules));
        int Score(string candidate) =>
            candidate == "*" ? 0
            : Agent.Contains(candidate, StringComparison.OrdinalIgnoreCase) && candidate.Length > 0 ? candidate.Length
            : -1;
        var best = groups.SelectMany(g => g.Agents).Select(Score).DefaultIfEmpty(-1).Max();
        if (best < 0)
            return (true, 1);
        var selected = groups.Where(g => g.Agents.Any(a => Score(a) == best)).SelectMany(g => g.Rules).ToArray();
        var path = Uri.UnescapeDataString(url.PathAndQuery);
        var matches = new List<(int Length, bool Allow)>();
        var delay = 1.0;
        foreach (var rule in selected)
        {
            if (
                rule.Name == "crawl-delay"
                && double.TryParse(
                    rule.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds
                )
            )
                delay = Math.Max(delay, double.IsFinite(seconds) ? seconds : 60);
            if (rule.Name is not ("allow" or "disallow") || rule.Value.Length == 0)
                continue;
            var pattern = Uri.UnescapeDataString(rule.Value);
            var end = pattern.EndsWith('$');
            if (end)
                pattern = pattern[..^1];
            var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + (end ? "$" : "");
            try
            {
                if (Regex.IsMatch(path, expression, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    matches.Add((pattern.Count(c => c != '*'), rule.Name == "allow"));
            }
            catch (RegexMatchTimeoutException)
            {
                return (false, delay);
            }
        }
        var longest = matches.Select(m => m.Length).DefaultIfEmpty(-1).Max();
        return (longest < 0 || matches.Any(m => m.Length == longest && m.Allow), delay);
    }
}
