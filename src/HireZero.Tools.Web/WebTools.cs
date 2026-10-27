namespace HireZero.Tools.Web;

// One public web page, read as text (docs/adr/0041-context-drafted-from-sources.md).
public sealed record WebReadRequest(string Url);

// Research stays on the owner-confirmed origin and observes the site's crawl policy.
public sealed record WebResearchRequest(string Url, string Origin);

// The page that was read: the final URL after redirects, its title and readable text. Clipped is true when the text
// was cut to the limit. Third-party text: evidence, never instructions.
public sealed record WebPage(string Url, string Title, string Text, bool Clipped);

public sealed record WebLink(string Url, string Label);

public sealed record WebDiscovery(WebPage[] Pages, string[] Notices);

public sealed record WebSearchRequest(string Query);

public sealed record WebCandidate(string Url, string Title, string Description);

public sealed record WebSearchResult(WebCandidate[] Candidates);

public static class WebTools
{
    public const int MaxTextCharacters = 6000;

    // Read-only and owner-started: https only, public addresses only, bounded in size and time.
    public static readonly ToolContract<WebReadRequest, WebPage> Read = new(
        "web.read",
        ToolRisk.Read,
        "Read one public https page as text, for a source the owner named."
    );

    public static readonly ToolContract<WebResearchRequest, WebPage> Research = new(
        "web.research",
        ToolRisk.Read,
        "Read one permitted public page on a confirmed origin, respecting robots.txt and pacing requests."
    );

    public static readonly ToolContract<WebResearchRequest, WebDiscovery> Discover = new(
        "web.discover",
        ToolRisk.Read,
        "Read the confirmed website and up to two relevant linked pages, respecting crawl restrictions and limits."
    );

    public static readonly ToolContract<WebSearchRequest, WebSearchResult> Search = new(
        "web.search",
        ToolRisk.Spend,
        "Make one owner-authorized search API request for possible business websites; never import or confirm the results."
    );
}
