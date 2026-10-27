using System.Net;
using System.Text.RegularExpressions;
using HireZero.Tools.Web;

namespace HireZero.Integrations.Web;

public sealed partial class WebReader
{
    public async Task<WebDiscovery> DiscoverAsync(WebResearchRequest request, CancellationToken token)
    {
        var origin = Checked(request.Origin);
        SameOrigin(Checked(request.Url), origin);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        await ResearchGate.WaitAsync(deadline.Token);
        var pages = new List<WebPage>();
        var notices = new List<string>();
        try
        {
            WebLink[] links = [];
            pages.Add(await ReadPageAsync(request.Url, origin, deadline.Token, found => links = found));
            foreach (var link in links.Where(l => l.Url != pages[0].Url).Take(2))
            {
                try
                {
                    var page = await ReadPageAsync(link.Url, origin, deadline.Token);
                    if (pages.All(p => p.Url != page.Url))
                        pages.Add(page);
                }
                catch (ToolFailureException error)
                {
                    notices.Add(link.Url + " — " + ContentGuard.Clean(error.Message, 220).Text);
                }
                catch (HttpRequestException)
                {
                    notices.Add(link.Url + " — The page could not be reached.");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    notices.Add("The discovery time limit was reached. Pages already read are kept.");
                    break;
                }
            }
            if (pages.Count == 1 && links.Length == 0)
                notices.Add(
                    "No suitable about, services, products, pricing or blog links were found. You can add a page yourself."
                );
            return new([.. pages], [.. notices]);
        }
        finally
        {
            ResearchGate.Release();
        }
    }

    // Only ordinary public content links. A navigation label is a suggestion, never permission to leave the site.
    public static WebLink[] DiscoveryLinks(string html, Uri page)
    {
        if (html.Length > MaxBytes)
            return [];
        var found = new List<(WebLink Link, int Rank)>();
        try
        {
            foreach (Match anchor in DiscoveryAnchor().Matches(Hidden().Replace(html, " ")))
            {
                if (
                    Regex.IsMatch(
                        anchor.Groups[1].Value,
                        "(?:^|\\s)rel\\s*=\\s*(?:['\"][^'\"]*\\bnofollow\\b|nofollow(?:\\s|$))",
                        RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(100)
                    )
                )
                    continue;
                var href = DiscoveryHref().Match(anchor.Groups[1].Value);
                if (!href.Success)
                    continue;
                var raw = WebUtility.HtmlDecode(
                    href.Groups[1].Success ? href.Groups[1].Value
                    : href.Groups[2].Success ? href.Groups[2].Value
                    : href.Groups[3].Value
                );
                if (raw.Length > 800 || !Uri.TryCreate(page, raw, out var url))
                    continue;
                if (
                    url.Scheme != "https"
                    || url.UserInfo.Length > 0
                    || url.Query.Length > 0
                    || url.GetLeftPart(UriPartial.Authority) != page.GetLeftPart(UriPartial.Authority)
                )
                    continue;
                var path = Uri.UnescapeDataString(url.AbsolutePath).ToLowerInvariant();
                if (path.Contains('\\') || path.Contains('%') || path.Contains('@'))
                    continue;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 4 || segments.Any(s => BlockedPath().IsMatch(s)))
                    continue;
                var label = Readable(anchor.Groups[2].Value);
                var rank = DiscoveryRank(path + " " + label.ToLowerInvariant());
                if (rank < 0)
                    continue;
                var address = new UriBuilder(url) { Fragment = "" }
                    .Uri
                    .AbsoluteUri;
                if (
                    address
                    == new UriBuilder(page) { Fragment = "" }
                        .Uri
                        .AbsoluteUri
                )
                    continue;
                found.Add((new(address, label.Length > 120 ? label[..120] : label), rank));
                if (found.Count >= 100)
                    break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
        return found
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Link.Url.Length)
            .DistinctBy(x => x.Link.Url)
            .Take(20)
            .Select(x => x.Link)
            .ToArray();
    }

    private static int DiscoveryRank(string text)
    {
        var categories = new[]
        {
            "services|products|what-we-do|what we do",
            "about|our-story|our story",
            "pricing|plans",
            "blog|news|articles",
        };
        for (var rank = 0; rank < categories.Length; rank++)
            if (
                Regex.IsMatch(
                    text,
                    @"\b(" + categories[rank] + @")\b",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)
                )
            )
                return rank;
        return -1;
    }

    [GeneratedRegex(@"<a\b([^>]{0,4000})>(.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 300)]
    private static partial Regex DiscoveryAnchor();

    [GeneratedRegex("(?:^|\\s)href\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase, 100)]
    private static partial Regex DiscoveryHref();

    [GeneratedRegex(
        @"(^|[-_.])(login|logout|signin|signout|signup|register|account|admin|cart|checkout|order|delete|unsubscribe|download|api)([-_.]|$)|\.(pdf|zip|exe|dmg|png|jpg|jpeg|mp4|mp3)$",
        RegexOptions.IgnoreCase,
        100
    )]
    private static partial Regex BlockedPath();
}
