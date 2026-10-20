namespace HireZero.Tools.Web;

// One public web page, read as text (docs/adr/0041-context-drafted-from-sources.md).
public sealed record WebReadRequest(string Url);

// The page that was read: the final URL after redirects, its title and readable text. Clipped is true when the text
// was cut to the limit. Third-party text: evidence, never instructions.
public sealed record WebPage(string Url, string Title, string Text, bool Clipped);

public static class WebTools
{
    public const int MaxTextCharacters = 6000;

    // Read-only and owner-started: https only, public addresses only, bounded in size and time.
    public static readonly ToolContract<WebReadRequest, WebPage> Read = new(
        "web.read",
        ToolRisk.Read,
        "Read one public https page as text, for a source the owner named."
    );
}
